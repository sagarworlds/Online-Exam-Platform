import { expect, test } from './support/fixtures';
import { choose, optionOf, startExam } from './support/journey';
import { text } from './support/messages';

const answersPath = /\/api\/v1\/me\/attempts\/[^/]+\/answers\/[^/]+$/;

test.describe('sitting an exam', () => {
  test('a candidate reads the instructions, answers questions and submits, and sees the score', async ({ page, api }) => {
    await startExam(page, api);

    // The page shows how long is left, and counts down from the server's deadline rather than showing zero.
    const timer = page.getByRole('timer');
    await expect(timer).toBeVisible();
    await expect(timer).not.toContainText('00:00');

    // Question 1: right answer. The save carries sequence number 1 (NFR-4).
    await choose(page, 'Answer 2');
    await expect.poll(() => api.requestsTo('PUT', answersPath)).toHaveLength(1);
    expect(api.requestsTo('PUT', answersPath)[0]?.body).toMatchObject({ optionId: 'question-1-option-1', sequence: 1 });

    // Changing the answer is a newer write, so its number is higher.
    await choose(page, 'Answer 1');
    await expect.poll(() => api.requestsTo('PUT', answersPath)).toHaveLength(2);
    expect(api.requestsTo('PUT', answersPath)[1]?.body).toMatchObject({ optionId: 'question-1-option-0', sequence: 2 });
    await choose(page, 'Answer 2');
    await expect.poll(() => api.sequenceOf('question-1')).toBe(3);

    // Save & Next moves on; question 2 is answered wrongly on purpose, question 3 left alone.
    await page.getByRole('button', { name: text('attempt.saveNext'), exact: false }).click();
    await expect(page.getByText(api.questions[1]!.text)).toBeVisible();
    await choose(page, 'Answer 3');
    await expect.poll(() => api.chosenOption('question-2')).toBe('question-2-option-0');

    // Submitting asks first, and "keep working" really keeps working.
    await page.getByRole('button', { name: text('attempt.submit.ask'), exact: true }).click();
    const confirmation = page.getByRole('alertdialog', { name: text('attempt.submit.aria') });
    await expect(confirmation).toBeVisible();
    await expect(confirmation).toContainText(text('attempt.submit.summary', { answered: 2, total: 3 }));
    await confirmation.getByRole('button', { name: text('attempt.submit.keep'), exact: true }).click();
    await expect(confirmation).toBeHidden();
    expect(api.requestsTo('POST', /\/submit$/)).toHaveLength(0);

    await page.getByRole('button', { name: text('attempt.submit.ask'), exact: true }).click();
    await page.getByRole('button', { name: text('attempt.submit.yes'), exact: true }).click();

    // One right answer out of three.
    await expect(page.getByText('1 / 3')).toBeVisible();
    await expect(page.getByRole('link', { name: text('common.backToMyExams'), exact: true })).toBeVisible();
    expect(api.requestsTo('POST', /\/submit$/)).toHaveLength(1);
  });

  test('reloading in the middle of an exam brings back the answers the server holds', async ({ page, api }) => {
    await startExam(page, api);
    await choose(page, 'Answer 2');
    await expect.poll(() => api.chosenOption('question-1')).toBe('question-1-option-1');

    await page.reload();

    await expect(page.getByText(api.questions[0]!.text)).toBeVisible();
    await expect(optionOf(page, 'Answer 2')).toBeChecked();

    // The next write numbers itself above the one the server holds, so the reload does not restart the count (NFR-4).
    await choose(page, 'Answer 1');
    await expect.poll(() => api.requestsTo('PUT', answersPath)).toHaveLength(2);
    expect(api.requestsTo('PUT', answersPath)[1]?.body).toMatchObject({ sequence: 2 });
  });

  test('the question palette and the Previous button move between questions', async ({ page, api }) => {
    await startExam(page, api);

    // The palette names each question's state for a screen reader; jump straight to question 3.
    await page.getByRole('button', { name: /^Question 3\b/ }).click();
    await expect(page.getByText(api.questions[2]!.text)).toBeVisible();

    await page.getByRole('button', { name: text('attempt.previous'), exact: false }).click();
    await expect(page.getByText(api.questions[1]!.text)).toBeVisible();
  });
});
