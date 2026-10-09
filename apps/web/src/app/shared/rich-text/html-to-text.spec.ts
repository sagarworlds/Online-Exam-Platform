import { htmlToPlainText } from './html-to-text';

describe('htmlToPlainText', () => {
  it('returns nothing for nothing', () => {
    expect(htmlToPlainText(null)).toBe('');
    expect(htmlToPlainText(undefined)).toBe('');
    expect(htmlToPlainText('')).toBe('');
  });

  it('removes the markup and keeps the words', () => {
    expect(htmlToPlainText('<p>Water is H<sub>2</sub>O and <strong>wet</strong>.</p>')).toBe('Water is H2O and wet.');
  });

  it('keeps paragraphs, list items and line breaks apart', () => {
    expect(htmlToPlainText('<p>One</p><p>Two</p><ul><li>Three</li><li>Four</li></ul>Five<br>Six')).toBe('One Two Three Four Five Six');
  });

  it('shows literal angle brackets as text', () => {
    expect(htmlToPlainText('<p>if a &lt; b &amp;&amp; b &gt; c</p>')).toBe('if a < b && b > c');
  });

  it('stands in for a picture, so a picture-only question is not blank', () => {
    expect(htmlToPlainText('<p><img src="x" alt="diagram"></p>')).toBe('[Image]');
    expect(htmlToPlainText('<p>Look: <img src="x"> then answer.</p>')).toBe('Look: [Image] then answer.');
  });

  it('drops script and style content instead of showing it', () => {
    expect(htmlToPlainText('<p>Hi</p><script>alert(1)</script><style>p{}</style>')).toBe('Hi');
  });

  it('runs nothing in the HTML it is given', () => {
    const w = window as unknown as { __ran?: boolean };
    htmlToPlainText('<img src="x" onerror="window.__ran = true"><script>window.__ran = true</script>');
    expect(w.__ran).toBeUndefined();
  });

  it('cuts to the length given, ending in an ellipsis', () => {
    expect(htmlToPlainText('<p>abcdefghij</p>', 5)).toBe('abcd…');
    expect(htmlToPlainText('<p>abc</p>', 5)).toBe('abc');
  });
});
