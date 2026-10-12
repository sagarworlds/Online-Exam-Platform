import { HttpErrorResponse } from '@angular/common/http';
import { extractBlobErrorMessage, extractErrorMessage, extractProblemCode } from './problem-details';

const problem = (body: unknown, status = 409) => new HttpErrorResponse({ status, error: body });

describe('problem details', () => {
  it('reads the API’s detail from a file request’s error body, which arrives as a blob', async () => {
    const refused = new Blob(['{"title":"forbidden","detail":"You may not export this exam."}'], { type: 'application/problem+json' });

    expect(await extractBlobErrorMessage(problem(refused, 403), 'Fallback')).toBe('You may not export this exam.');
  });

  it('gives the fallback for a blob that is not a problem, and for a failure with no body', async () => {
    expect(await extractBlobErrorMessage(problem(new Blob(['<html>'])), 'Fallback')).toBe('Fallback');
    expect(await extractBlobErrorMessage(problem(new Blob(['{"title":"x"}'])), 'Fallback')).toBe('Fallback');
    expect(await extractBlobErrorMessage(new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') }), 'Fallback')).toBe('Fallback');
    expect(await extractBlobErrorMessage(new Error('boom'), 'Fallback')).toBe('Fallback');
  });

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
