import { HttpErrorResponse } from '@angular/common/http';
import { extractErrorMessage, extractProblemCode } from './problem-details';

const problem = (body: unknown, status = 409) => new HttpErrorResponse({ status, error: body });

describe('problem details', () => {
  it('uses the API’s detail as the message, and a fallback when there is none', () => {
    expect(extractErrorMessage(problem({ title: 'question_locked', detail: 'Only wording can change.' }))).toBe('Only wording can change.');
    expect(extractErrorMessage(problem(null, 403))).toBe('Something went wrong. Please try again.');
    expect(extractErrorMessage(new Error('boom'), 'Custom')).toBe('Custom');
  });

  it('reads the error code from the title', () => {
    expect(extractProblemCode(problem({ title: 'question_locked', detail: 'x' }))).toBe('question_locked');
  });

  it('has no code for an error the API did not explain', () => {
    expect(extractProblemCode(problem(null, 403))).toBeUndefined();
    expect(extractProblemCode(problem({ title: '' }))).toBeUndefined();
    expect(extractProblemCode(new Error('boom'))).toBeUndefined();
  });
});
