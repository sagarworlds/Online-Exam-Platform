import { expect, test } from './support/fixtures';
import { startExam } from './support/journey';

// Formulas are kept in a question's text as `$...$` and `$$...$$` and typeset in the candidate's browser by KaTeX (FR-5). Unit tests run
// that code in a simulated page; this runs it in each real engine, with the stylesheet and the fonts the build ships.

test.describe('maths in a question', () => {
  test('a formula is typeset, and a broken one and a dollar amount are left as written', async ({ page, api }) => {
    api.questions[0]!.text =
      'Add $\\frac{1}{2} + \\frac{1}{3}$ and say what $$x^2 + y^2$$ is, for a price of $5, but leave $\\notacommand$ as it is.';

    const failures: string[] = [];
    const fontResponses: { url: string; status: number }[] = [];
    page.on('pageerror', (error) => failures.push(error.message));
    page.on('response', (response) => {
      if (response.url().includes('KaTeX_')) {
        fontResponses.push({ url: response.url(), status: response.status() });
      }
    });

    await startExam(page, api);
    const question = page.locator('.question__text');

    // The inline and the display formula are each typeset, with the MathML a screen reader reads next to the drawn maths.
    await expect(question.locator('.katex')).toHaveCount(2);
    await expect(question.locator('.katex-display')).toHaveCount(1);
    await expect(question.locator('.katex-mathml')).toHaveCount(2);
    await expect(question.locator('.katex .mfrac').first()).toBeVisible();

    // A dollar amount is not a formula, and a formula KaTeX cannot parse stays as the author typed it.
    await expect(question).toContainText('for a price of $5');
    await expect(question).toContainText('$\\notacommand$');

    // The fonts the typeset maths is drawn with came from the build: none was missing.
    await expect.poll(() => fontResponses.length, { message: 'the KaTeX fonts were requested' }).toBeGreaterThan(0);
    expect(fontResponses.filter((font) => font.status !== 200)).toEqual([]);
    expect(failures).toEqual([]);
  });
});
