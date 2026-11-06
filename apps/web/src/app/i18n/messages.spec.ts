import { EN, MessageKey } from './messages.en';
import { HI } from './messages.hi';
import { MR } from './messages.mr';
import { PENDING_TRANSLATION } from './pending-translations';

const placeholders = (message: string) => [...message.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

describe('the messages of every language (FR-51)', () => {
  const keys = Object.keys(EN) as MessageKey[];

  describe.each([
    ['Hindi', HI],
    ['Marathi', MR],
  ])('%s', (_name, messages) => {
    it('has exactly the keys English has, so no screen shows a gap', () => {
      expect(Object.keys(messages).sort()).toEqual([...keys].sort());
    });

    it('has no empty message', () => {
      expect(keys.filter((key) => messages[key].trim() === '')).toEqual([]);
    });

    it('uses the same {parameters} as English in each message, so a value is never dropped or left as a placeholder', () => {
      const differing = keys.filter((key) => placeholders(messages[key]).join() !== placeholders(EN[key]).join());

      expect(differing).toEqual([]);
    });

    it('is translated: the messages are not English left as they were', () => {
      // A few words are the same in any language (OK, SMS), but a language that matches English in many messages was not translated.
      const untranslated = keys.filter((key) => messages[key] === EN[key] && !PENDING_TRANSLATION.includes(key));

      expect(untranslated.length).toBeLessThan(5);
    });

    it('lists as pending only messages that are still English, so the list shrinks as they are translated', () => {
      expect(PENDING_TRANSLATION.filter((key) => messages[key] !== EN[key])).toEqual([]);
    });
  });

  it('has a `.other` form for every `.one` form, so a count always finds its wording', () => {
    const ones = keys.filter((key) => key.endsWith('.one'));

    expect(ones.length).toBeGreaterThan(0);
    expect(ones.filter((key) => !keys.includes(key.replace(/\.one$/, '.other') as MessageKey))).toEqual([]);
  });
});
