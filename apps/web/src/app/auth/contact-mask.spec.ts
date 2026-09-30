import { maskContact } from './contact-mask';

describe('maskContact', () => {
  it('keeps the first character and the domain of an email address', () => {
    expect(maskContact('jane.doe@example.com')).toBe('j***@example.com');
  });

  it('hides a one-character email local part entirely', () => {
    expect(maskContact('j@example.com')).toBe('***@example.com');
  });

  it('keeps only the last two digits of a phone number', () => {
    expect(maskContact('9876543210')).toBe('********10');
    expect(maskContact('+919876543210')).toBe('***********10');
  });

  it('masks a very short phone number entirely', () => {
    expect(maskContact('1234')).toBe('****');
  });

  it('ignores surrounding whitespace', () => {
    expect(maskContact('  jane@example.com ')).toBe('j***@example.com');
  });
});
