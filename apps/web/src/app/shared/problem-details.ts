import { HttpErrorResponse } from '@angular/common/http';

/** Shape of the API's error responses (see DomainExceptionHandler on the backend). */
export interface ProblemDetails {
  status?: number;
  title?: string;
  detail?: string;
  instance?: string;
}

/** Extracts a human-readable message from an HTTP error, preferring the API's ProblemDetails 'detail'. */
export function extractErrorMessage(error: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (error instanceof HttpErrorResponse) {
    const problem = error.error as ProblemDetails | null;
    if (problem && typeof problem.detail === 'string' && problem.detail.length > 0) {
      return problem.detail;
    }
  }

  return fallback;
}

/**
 * The API's message for an error from a request that asked for a file. Such a request reads its response as a Blob, so its error body is a
 * Blob too and is parsed here. A failure with no body, or a body that is not ProblemDetails, gives the fallback.
 */
export async function extractBlobErrorMessage(error: unknown, fallback: string): Promise<string> {
  if (error instanceof HttpErrorResponse && error.error instanceof Blob) {
    try {
      const problem = JSON.parse(await error.error.text()) as ProblemDetails | null;
      if (problem && typeof problem.detail === 'string' && problem.detail.length > 0) {
        return problem.detail;
      }
    } catch {
      // The body was not JSON, so there is no message to show; the fallback says what went wrong.
    }
  }

  return fallback;
}

/**
 * The API's machine-readable error code (the ProblemDetails `title`, such as `question_locked`), for the few places that must
 * react to a particular failure and not only show its message. Undefined when the error is not one the API explained.
 */
export function extractProblemCode(error: unknown): string | undefined {
  if (error instanceof HttpErrorResponse) {
    const problem = error.error as ProblemDetails | null;
    if (problem && typeof problem.title === 'string' && problem.title.length > 0) {
      return problem.title;
    }
  }

  return undefined;
}
