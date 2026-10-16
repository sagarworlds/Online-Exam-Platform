import { randomUUID } from 'node:crypto';
import { readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import type { APIRequestContext, PlaywrightWorkerArgs } from '@playwright/test';
import type { AuthResult, RegisterCandidateResponse } from '../../src/app/auth/auth.models';
import type { AttemptDto, MyExamDto } from '../../src/app/candidate/candidate.models';

/** What `run-api.mjs` wrote about the API it started: who the administrator is, where it listens and where its log is. */
export interface RunSettings {
  adminEmail: string;
  adminPassword: string;
  apiUrl: string;
  apiLog: string;
}

export function runSettings(): RunSettings {
  return JSON.parse(readFileSync(join(__dirname, '..', '.run', 'settings.json'), 'utf8')) as RunSettings;
}

/**
 * The sign-in codes. The API in Development writes each code to its log instead of sending it, as a JSON line from the logging sender.
 * A test notes where the log ends, asks for a code, and takes the first code written after that point. That holds because one test
 * runs at a time (`workers: 1`), so the next code written is the one just asked for.
 */
export class SignInCodes {
  constructor(private readonly logPath: string) {}

  /** Where the log ends now. */
  mark(): number {
    return statSync(this.logPath).size;
  }

  /** The first code the API wrote after `mark`, waiting for it if it is not there yet. */
  async nextAfter(mark: number, timeoutMs = 15_000): Promise<string> {
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      const code = this.firstCodeAfter(mark);
      if (code !== null) {
        return code;
      }
      if (Date.now() > deadline) {
        throw new Error(`The API wrote no sign-in code to ${this.logPath} within ${timeoutMs} ms.`);
      }
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
  }

  private firstCodeAfter(mark: number): string | null {
    const written = readFileSync(this.logPath).subarray(mark).toString('utf8');
    for (const line of written.split('\n')) {
      if (!line.includes('LoggingOtpSender')) {
        continue;
      }
      try {
        const code = (JSON.parse(line) as { properties?: { Code?: string } }).properties?.Code;
        if (code) {
          return code;
        }
      } catch {
        // The last line may be half written when it is read; the next read has all of it.
      }
    }
    return null;
  }
}

/** A question to put in an exam: its text, the option that is right, and one that is wrong. */
export interface QuestionSpec {
  text: string;
  right: string;
  wrong: string;
}

export interface PublishedExam {
  id: string;
  name: string;
  questions: QuestionSpec[];
}

export interface EnrolledCandidate {
  email: string;
  accessToken: string;
}

/**
 * The real API, for setting a test up and for checking what it stored afterwards. It does what a person would do to get an exam and a
 * candidate in place: sign in as the administrator (password, then the code), write the questions, build and publish the exam, and
 * register a candidate and enrol them with an invitation. The browser then does the candidate's part.
 */
export class RealApi {
  private adminToken: string | null = null;

  private constructor(
    private readonly http: APIRequestContext,
    private readonly codes: SignInCodes,
    private readonly settings: RunSettings,
  ) {}

  static async connect(playwright: PlaywrightWorkerArgs['playwright']): Promise<RealApi> {
    const settings = runSettings();
    const http = await playwright.request.newContext({ baseURL: settings.apiUrl });
    return new RealApi(http, new SignInCodes(settings.apiLog), settings);
  }

  get signInCodes(): SignInCodes {
    return this.codes;
  }

  async dispose(): Promise<void> {
    await this.http.dispose();
  }

  /** Writes the questions, builds an exam from them, schedules it to be open now, and publishes it. */
  async publishExam(name: string, questions: QuestionSpec[]): Promise<PublishedExam> {
    const admin = await this.administrator();

    const questionIds: string[] = [];
    for (const question of questions) {
      const created = await this.send<{ id: string }>('POST', '/v1/questions', admin, {
        text: question.text,
        options: [
          { text: question.right, isCorrect: true },
          { text: question.wrong, isCorrect: false },
        ],
      });
      questionIds.push(created.id);
    }

    const exam = await this.send<{ id: string }>('POST', '/v1/exams', admin, { name });
    const section = await this.send<{ id: string }>('POST', `/v1/exams/${exam.id}/sections`, admin, { name: 'Section A' });
    for (const questionId of questionIds) {
      await this.send('POST', `/v1/exams/${exam.id}/sections/${section.id}/questions`, admin, { questionId });
    }

    const opens = new Date(Date.now() - 5 * 60_000);
    await this.send('PUT', `/v1/exams/${exam.id}/schedule`, admin, {
      scheduledStartTime: opens.toISOString(),
      scheduledEndTime: new Date(opens.getTime() + 3 * 3_600_000).toISOString(),
      durationMinutes: 60,
    });
    await this.send('POST', `/v1/exams/${exam.id}/publish`, admin);

    return { id: exam.id, name, questions };
  }

  /** Registers a new candidate (with the code the API logs) and has them accept an invitation to the exam. */
  async enrolNewCandidate(exam: PublishedExam): Promise<EnrolledCandidate> {
    const email = `candidate-${randomUUID().replaceAll('-', '')}@e2e.test`;
    const admin = await this.administrator();

    const mark = this.codes.mark();
    const registered = await this.send<RegisterCandidateResponse>('POST', '/v1/auth/register', null, {
      email,
      phoneNumber: null,
      dateOfBirth: '1990-01-01',
      displayName: 'Browser Test Candidate',
      otpChannel: 'Email',
    });
    const verified = await this.send<AuthResult>('POST', '/v1/auth/otp/verify', null, {
      otpChallengeId: registered.otpChallengeId,
      code: await this.codes.nextAfter(mark),
    });
    const accessToken = verified.accessToken ?? '';

    const invite = await this.send<{ inviteLink: string }>('POST', '/v1/invites', admin, { examId: exam.id, email });
    const code = decodeURIComponent(invite.inviteLink.slice(invite.inviteLink.indexOf('code=') + 'code='.length));
    await this.send('POST', '/v1/invites/accept', accessToken, { code });

    return { email, accessToken };
  }

  // The two reads below take the token the browser holds, not the one registration returned: an account has one session at a time, so
  // signing in on the page ends the session the registration started, and the API answers that token with 401 "signed in somewhere else".

  /** What the API holds for the candidate's exams: the attempt, whether it is submitted, and the score. */
  candidateExams(accessToken: string): Promise<MyExamDto[]> {
    return this.send<MyExamDto[]>('GET', '/v1/me/exams', accessToken);
  }

  /** What the API holds for one attempt, with the questions and the answers saved so far. */
  attempt(accessToken: string, attemptId: string): Promise<AttemptDto> {
    return this.send<AttemptDto>('GET', `/v1/me/attempts/${attemptId}`, accessToken);
  }

  private async administrator(): Promise<string> {
    if (this.adminToken === null) {
      const mark = this.codes.mark();
      const first = await this.send<AuthResult>('POST', '/v1/auth/login', null, {
        email: this.settings.adminEmail,
        password: this.settings.adminPassword,
      });
      if (!first.requiresTwoFactor) {
        this.adminToken = first.accessToken ?? '';
      } else {
        const second = await this.send<AuthResult>('POST', '/v1/auth/otp/verify', null, {
          otpChallengeId: first.otpChallengeId,
          code: await this.codes.nextAfter(mark),
        });
        this.adminToken = second.accessToken ?? '';
      }
    }
    return this.adminToken;
  }

  /** One JSON request to the API. Anything but a success is an error that says what was asked and what came back. */
  private async send<T = void>(method: 'GET' | 'POST' | 'PUT', path: string, token: string | null, data?: unknown): Promise<T> {
    const response = await this.http.fetch(path, {
      method,
      data,
      headers: token === null ? {} : { Authorization: `Bearer ${token}` },
    });
    if (!response.ok()) {
      throw new Error(`${method} ${path} answered ${response.status()}: ${await response.text()}`);
    }
    const body = await response.text();
    return (body === '' ? undefined : JSON.parse(body)) as T;
  }
}
