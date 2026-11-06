import type { Page, Route } from '@playwright/test';
import type { AuthResult } from '../../src/app/auth/auth.models';
import type { BrandingDto } from '../../src/app/branding/branding.models';
import type {
  AttemptDto,
  AttemptQuestionDto,
  AttemptStatusDto,
  MyExamDto,
} from '../../src/app/candidate/candidate.models';

/** One request the app made to the fake API, for a test to read back. */
export interface RecordedRequest {
  method: string;
  /** The path without the query string, e.g. `/api/v1/me/exams`. */
  path: string;
  body: unknown;
}

/** What a stubbed endpoint answers with: a status, and a JSON body unless it is empty. */
interface Reply {
  status: number;
  body?: unknown;
}

/** What an endpoint handler is given: the request body and the values captured from its path. */
interface Call {
  body: unknown;
  params: readonly string[];
}

interface Endpoint {
  method: string;
  path: RegExp;
  reply: (call: Call) => Reply;
}

/** A question the fake API holds, with the option that would be marked right. The app never sees which one that is. */
interface FakeQuestion {
  id: string;
  text: string;
  options: { id: string; text: string }[];
  correctOptionId: string;
}

const NO_BRANDING: BrandingDto = { instituteName: null, primaryColour: null, hasLogo: false, updatedAtUtc: null };

/** Unsigned, which is all the app needs: it reads the claims for the interface and never checks the signature. */
function encodeSegment(value: object): string {
  return btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** An access token for a signed-in candidate, valid for an hour, in the shape the API issues. */
export function candidateAccessToken(userId: string, sessionId: string): string {
  const payload = {
    sub: userId,
    sid: sessionId,
    exp: Math.floor(Date.now() / 1000) + 3600,
    'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'Candidate',
    perm: [],
  };
  return `${encodeSegment({ alg: 'none', typ: 'JWT' })}.${encodeSegment(payload)}.signature`;
}

/**
 * The API, replaced inside the browser: every request the app makes to `/api` is answered here and recorded, so a test needs no server
 * or database and runs the same on every browser engine. It holds one exam and keeps the rules that matter to the page: an answer is
 * applied only if its sequence number is higher than the one held (NFR-4), as the real API does.
 *
 * A request the fake API does not know is answered with 501 and listed in {@link unexpected}; the test fixture fails the test if the
 * list is not empty, so the app cannot start calling something new without a test hearing about it.
 */
export class FakeApi {
  /** Every request the app made, oldest first. */
  readonly requests: RecordedRequest[] = [];

  /** The requests no endpoint here answered, as `METHOD path`. */
  readonly unexpected: string[] = [];

  /** The password the sign-in accepts. Any other is refused the way the API refuses a wrong one. */
  readonly password = 'correct-horse-battery';

  readonly examId = 'exam-0001';
  readonly attemptId = 'attempt-0001';
  readonly examName = 'Browser compatibility check';

  readonly questions: FakeQuestion[] = [1, 2, 3].map((n) => ({
    id: `question-${n}`,
    text: `What is ${n} + ${n}?`,
    options: [0, 1, 2, 3].map((i) => ({ id: `question-${n}-option-${i}`, text: `Answer ${n * 2 + i - 1}` })),
    correctOptionId: `question-${n}-option-1`,
  }));

  private attemptStarted = false;
  private submittedAtUtc: string | null = null;
  private readonly chosen = new Map<string, string>();
  private readonly sequences = new Map<string, number>();

  private readonly endpoints: Endpoint[] = [
    { method: 'GET', path: /^\/api\/v1\/health$/, reply: () => ({ status: 200, body: { status: 'Healthy' } }) },
    { method: 'GET', path: /^\/api\/v1\/branding$/, reply: () => ({ status: 200, body: NO_BRANDING }) },
    // The header's unread count, read in the background once someone is signed in.
    { method: 'GET', path: /^\/api\/v1\/me\/notifications\/unread-count$/, reply: () => ({ status: 200, body: { unreadCount: 0 } }) },
    { method: 'POST', path: /^\/api\/v1\/auth\/login$/, reply: ({ body }) => this.signIn(body) },
    { method: 'GET', path: /^\/api\/v1\/me\/exams$/, reply: () => ({ status: 200, body: [this.myExam()] }) },
    { method: 'POST', path: /^\/api\/v1\/me\/exams\/([^/]+)\/attempts$/, reply: () => this.startAttempt() },
    { method: 'GET', path: /^\/api\/v1\/me\/attempts\/([^/]+)$/, reply: () => ({ status: 200, body: this.attempt() }) },
    // No exam in these journeys takes webcam photos (FR-24), so the page is told to leave the camera alone.
    { method: 'GET', path: /^\/api\/v1\/me\/attempts\/([^/]+)\/webcam-policy$/, reply: () => ({ status: 200, body: { enabled: false, intervalSeconds: 0, screenEnabled: false, screenIntervalSeconds: 0 } }) },
    { method: 'GET', path: /^\/api\/v1\/me\/attempts\/([^/]+)\/status$/, reply: () => ({ status: 200, body: this.status() }) },
    { method: 'PUT', path: /^\/api\/v1\/me\/attempts\/([^/]+)\/answers\/([^/]+)$/, reply: (call) => this.saveAnswer(call) },
    { method: 'PUT', path: /^\/api\/v1\/me\/attempts\/([^/]+)\/questions\/([^/]+)\/time$/, reply: () => ({ status: 204 }) },
    { method: 'POST', path: /^\/api\/v1\/me\/attempts\/([^/]+)\/submit$/, reply: () => this.submit() },
    // The consent the exam asks for before it starts: already given, so the journey is not stopped at the box.
    { method: 'GET', path: /^\/api\/v1\/consent\/status$/, reply: () => ({ status: 200, body: { subjectId: 'u1', purpose: 'ProctoringDataProcessing', isActive: true, consentRecordId: 'c1', currentNoticeVersionId: 'n1' } }) },
  ];

  /** Starts answering the page's `/api` requests. Call once, before the page loads. */
  async install(page: Page): Promise<void> {
    await page.route('**/api/**', (route) => this.handle(route));
  }

  /** The requests made so far to one endpoint. */
  requestsTo(method: string, path: RegExp): RecordedRequest[] {
    return this.requests.filter((request) => request.method === method && path.test(request.path));
  }

  /** The option the fake API holds as the answer to a question, or undefined when none is saved. */
  chosenOption(questionId: string): string | undefined {
    return this.chosen.get(questionId);
  }

  /** The highest sequence number applied to a question's answer, or undefined when nothing was written. */
  sequenceOf(questionId: string): number | undefined {
    return this.sequences.get(questionId);
  }

  private async handle(route: Route): Promise<void> {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();
    this.requests.push({ method, path, body: request.postData() === null ? null : (request.postDataJSON() as unknown) });

    for (const endpoint of this.endpoints) {
      const match = endpoint.method === method ? endpoint.path.exec(path) : null;
      if (match !== null) {
        const reply = endpoint.reply({ body: this.requests.at(-1)?.body ?? null, params: match.slice(1) });
        await route.fulfill({
          status: reply.status,
          contentType: 'application/json',
          body: reply.body === undefined ? undefined : JSON.stringify(reply.body),
        });
        return;
      }
    }

    this.unexpected.push(`${method} ${path}`);
    await route.fulfill({ status: 501, contentType: 'application/json', body: JSON.stringify({ detail: 'Not stubbed' }) });
  }

  private signIn(body: unknown): Reply {
    const { password } = body as { email: string; password: string };
    if (password !== this.password) {
      // The same shape the API's DomainExceptionHandler writes; the page shows `detail`.
      return { status: 401, body: { status: 401, title: 'invalid_credentials', detail: 'The email or password is incorrect.' } };
    }

    const result: AuthResult = {
      requiresTwoFactor: false,
      accessToken: candidateAccessToken('candidate-0001', 'session-0001'),
      sessionId: 'session-0001',
      otpChallengeId: null,
    };
    return { status: 200, body: result };
  }

  private myExam(): MyExamDto {
    const inProgress = this.attemptStarted && this.submittedAtUtc === null;
    const now = Date.now();
    return {
      examId: this.examId,
      name: this.examName,
      description: 'Three short questions that check the exam page in this browser.',
      startUtc: new Date(now - 3_600_000).toISOString(),
      endUtc: new Date(now + 86_400_000).toISOString(),
      lateEntryDeadlineUtc: null,
      durationSeconds: 1800,
      questionCount: this.questions.length,
      state: 'Open',
      attemptId: this.attemptStarted ? this.attemptId : null,
      attemptStatus: !this.attemptStarted ? null : this.submittedAtUtc === null ? 'InProgress' : 'Submitted',
      score: this.submittedAtUtc === null ? null : this.score(),
      maxScore: this.submittedAtUtc === null ? null : this.questions.length,
      attemptsAllowed: 1,
      attemptsUsed: this.attemptStarted ? 1 : 0,
      canStartAttempt: !this.attemptStarted,
      attempts: this.attemptStarted
        ? [
            {
              id: this.attemptId,
              number: 1,
              status: inProgress ? 'InProgress' : 'Submitted',
              startedAtUtc: new Date(now).toISOString(),
              submittedAtUtc: this.submittedAtUtc,
              autoSubmitted: false,
              score: inProgress ? null : this.score(),
              maxScore: inProgress ? null : this.questions.length,
            },
          ]
        : [],
      canRequestAttempt: false,
      attemptRequest: null,
      rules: {
        correctMarks: 1,
        incorrectMarks: 0,
        unattemptedMarks: 0,
        partialCredit: false,
        sectionLock: false,
        sectionCount: 1,
        contentProtection: false,
        focusViolationLimit: 0,
        instructions: null,
      },
    };
  }

  private startAttempt(): Reply {
    this.attemptStarted = true;
    return { status: 200, body: this.attempt() };
  }

  private attempt(): AttemptDto {
    const now = new Date();
    const submitted = this.submittedAtUtc !== null;
    const questions: AttemptQuestionDto[] = this.questions.map((question) => ({
      id: question.id,
      text: question.text,
      options: question.options,
      selectedOptionId: this.chosen.get(question.id) ?? null,
      markedForReview: false,
      answerSequence: this.sequences.get(question.id) ?? null,
    }));

    return {
      id: this.attemptId,
      examId: this.examId,
      examName: this.examName,
      status: submitted ? 'Submitted' : 'InProgress',
      startedAtUtc: now.toISOString(),
      deadlineUtc: new Date(now.getTime() + 1_800_000).toISOString(),
      submittedAtUtc: this.submittedAtUtc,
      autoSubmitted: false,
      score: submitted ? this.score() : null,
      maxScore: submitted ? this.questions.length : null,
      serverTimeUtc: now.toISOString(),
      sections: submitted ? [] : [{ id: 'section-1', name: 'Section A', questions }],
      review: submitted ? { available: false, mode: 'Manual', availableFromUtc: null } : null,
      number: 1,
      contentProtection: false,
      focusViolationLimit: 0,
    };
  }

  private status(): AttemptStatusDto {
    const now = new Date();
    return {
      status: this.submittedAtUtc === null ? 'InProgress' : 'Submitted',
      pausedAtUtc: null,
      deadlineUtc: new Date(now.getTime() + 1_800_000).toISOString(),
      serverTimeUtc: now.toISOString(),
      warnings: [],
    };
  }

  /** Applies an answer if its sequence number is higher than the one held, as the real API does (NFR-4, ADR 0004). */
  private saveAnswer({ body, params }: Call): Reply {
    const questionId = params[1] ?? '';
    const { optionId, sequence } = body as { optionId: string; sequence: number | null };
    const held = this.sequences.get(questionId) ?? 0;
    if (sequence !== null && sequence <= held) {
      return { status: 409, body: { status: 409, title: 'answer_superseded', currentSequence: held, detail: 'A newer answer is held.' } };
    }

    this.chosen.set(questionId, optionId);
    this.sequences.set(questionId, sequence ?? held + 1);
    return { status: 204 };
  }

  private submit(): Reply {
    this.submittedAtUtc = new Date().toISOString();
    return { status: 200, body: this.attempt() };
  }

  /** One mark for each question whose chosen option is the right one. */
  private score(): number {
    return this.questions.filter((question) => this.chosen.get(question.id) === question.correctOptionId).length;
  }
}
