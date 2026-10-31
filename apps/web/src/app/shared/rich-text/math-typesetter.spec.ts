import { typesetMath } from './math-typesetter';

describe('typesetMath', () => {
  const run = (html: string): HTMLElement => {
    const root = document.createElement('div');
    root.innerHTML = html;
    typesetMath(root);
    return root;
  };

  it('renders an inline formula and keeps the words around it', () => {
    const root = run('<p>Solve $x^2 + 1$ now</p>');

    expect(root.querySelector('p .katex')).not.toBeNull();
    expect(root.textContent).toContain('Solve ');
    expect(root.textContent).toContain(' now');
    expect(root.textContent).not.toContain('$');
  });

  it('renders the LaTeX brackets too: \\( ... \\) inline and \\[ ... \\] as a block', () => {
    const inline = run('<p>Solve \\(x^2 + 1\\) now</p>');
    const block = run('<p>\\[\\frac{1}{2}\\]</p>');

    expect(inline.querySelector('p .katex')).not.toBeNull();
    expect(inline.querySelector('.katex-display')).toBeNull();
    expect(inline.textContent).not.toContain('\\(');
    expect(block.querySelector('div .katex-display')).not.toBeNull();
  });

  it('renders several formulas of mixed kinds in one paragraph', () => {
    const root = run('<p>Let $a$ and \\(b\\) give \\[c\\] and $$d$$.</p>');

    expect(root.querySelectorAll('.katex').length).toBe(4);
  });

  it('leaves a bracket that is not a formula alone', () => {
    const root = run('<p>Path C:\\Users and a \\(lone bracket</p>');

    expect(root.querySelector('.katex')).toBeNull();
    expect(root.textContent).toBe('Path C:\\Users and a \\(lone bracket');
  });

  it('renders a display formula as a block', () => {
    const root = run('<p>$$\\frac{1}{2}$$</p>');

    expect(root.querySelector('div .katex-display')).not.toBeNull();
  });

  it('leaves text with a formula that does not parse exactly as typed', () => {
    const root = run('<p>Cost is $\\frac{1$ rupees</p>');

    expect(root.querySelector('.katex')).toBeNull();
    expect(root.textContent).toBe('Cost is $\\frac{1$ rupees');
  });

  it('does not treat dollar amounts as a formula', () => {
    const root = run('<p>It costs $5 or $ 6 today</p>');

    expect(root.querySelector('.katex')).toBeNull();
  });

  it('leaves code exactly as written', () => {
    const root = run('<pre><code>echo $HOME and $PATH</code></pre>');

    expect(root.querySelector('.katex')).toBeNull();
    expect(root.textContent).toBe('echo $HOME and $PATH');
  });

  it('cannot be made to run markup through a formula', () => {
    const root = run('<p>$\\href{javascript:alert(1)}{x}$ $<img src=x onerror=alert(1)>$</p>');

    expect(root.querySelector('a')).toBeNull();
  });
});
