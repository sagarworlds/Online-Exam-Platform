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
