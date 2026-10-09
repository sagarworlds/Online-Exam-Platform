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
