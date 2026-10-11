import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  DEFAULT_HOLD_SECONDS,
  DEFAULT_RAMP_SECONDS,
  accountEmail,
  arrivalRateScenario,
  assertPoolCovers,
  maxVUsFor,
  pickAnswer,
  planFromEnv,
  positiveInt,
  readRunConfig,
  singleChoiceAnswers,
} from '../lib/plan.js';

const GUID = '6f1c2a0e-4b7d-4e8a-9c3f-0d2b1a9e8f7c';

function runEnv(overrides = {}) {
  return {
    LOAD_BASE_URL: 'https://staging-api.example',
    LOAD_EXAM_ID: GUID,
    LOAD_TEST_EMAIL_PATTERN: 'candidate-{n}@load.example',
    LOAD_TEST_PASSWORD: 'correct horse battery',
    LOAD_TEST_ACCOUNT_COUNT: '1000',
    ...overrides,
  };
}

test('the default plan is 2x of 10,000 concurrent candidates, at 1 save per 20 s and a 120 s entry window', () => {
  const plan = planFromEnv({});
  assert.equal(plan.cohort, 20000);
  assert.equal(plan.saveRate, 1000);
  assert.equal(plan.questionRate, 167);
  assert.equal(plan.rampSeconds, DEFAULT_RAMP_SECONDS);
  assert.equal(plan.holdSeconds, DEFAULT_HOLD_SECONDS);
});

test('the peak inputs change the derived rates', () => {
  const plan = planFromEnv({
    LOAD_PEAK_CONCURRENT_CANDIDATES: '5000',
    LOAD_PEAK_MULTIPLIER: '1',
    LOAD_SAVE_INTERVAL_SECONDS: '10',
    LOAD_ENTRY_WINDOW_SECONDS: '100',
  });
  assert.equal(plan.cohort, 5000);
  assert.equal(plan.saveRate, 500);
  assert.equal(plan.questionRate, 50);
});

test('explicit rates override the derivation, which keeps a smoke run small', () => {
  const plan = planFromEnv({ LOAD_SAVE_RPS: '3', LOAD_QUESTION_RPS: '2' });
  assert.equal(plan.saveRate, 3);
  assert.equal(plan.questionRate, 2);
});

test('a blank setting takes the default, and a malformed one stops the run', () => {
  assert.equal(positiveInt('X', '', 7), 7);
  assert.equal(positiveInt('X', '   ', 7), 7);
  assert.equal(positiveInt('X', undefined, 7), 7);
  assert.equal(positiveInt('X', '42', 7), 42);
  assert.throws(() => positiveInt('LOAD_SAVE_RPS', '0', 7), /LOAD_SAVE_RPS must be a positive whole number/);
  assert.throws(() => positiveInt('LOAD_SAVE_RPS', '-5', 7), /positive whole number/);
  assert.throws(() => positiveInt('LOAD_SAVE_RPS', '12.5', 7), /positive whole number/);
  assert.throws(() => positiveInt('LOAD_SAVE_RPS', 'fast', 7), /positive whole number/);
});

test('the arrival-rate scenario ramps to the target and holds it, with a VU ceiling of one second of latency per arrival', () => {
  const scenario = arrivalRateScenario(1000, 120, 600);
  assert.equal(scenario.executor, 'ramping-arrival-rate');
  assert.equal(scenario.timeUnit, '1s');
  assert.equal(scenario.maxVUs, maxVUsFor(1000));
  assert.equal(scenario.maxVUs, 1000);
  assert.ok(scenario.preAllocatedVUs >= 1 && scenario.preAllocatedVUs <= scenario.maxVUs);
  assert.deepEqual(scenario.stages, [
    { target: 1000, duration: '120s' },
    { target: 1000, duration: '600s' },
  ]);
});

test('the pool must cover the VU ceiling, one account per VU', () => {
  assert.doesNotThrow(() => assertPoolCovers(1000, 1000));
  assert.doesNotThrow(() => assertPoolCovers(1500, 1000));
  assert.throws(() => assertPoolCovers(999, 1000), /must be at least the VU ceiling \(1000\)/);
});

test('an email pattern gives each account its own address, numbered from 1', () => {
  assert.equal(accountEmail('candidate-{n}@load.example', 0), 'candidate-1@load.example');
  assert.equal(accountEmail('candidate-{n}@load.example', 999), 'candidate-1000@load.example');
  assert.equal(accountEmail('{n}-{n}@x.example', 2), '3-3@x.example');
});

test('readRunConfig accepts a complete staging setting and strips a trailing slash from the origin', () => {
  const config = readRunConfig(runEnv({ LOAD_BASE_URL: 'https://staging-api.example/' }));
  assert.deepEqual(config, {
    baseUrl: 'https://staging-api.example',
    examId: GUID,
    emailPattern: 'candidate-{n}@load.example',
    password: 'correct horse battery',
    accountCount: 1000,
  });
});

test('readRunConfig names the first missing or malformed setting', () => {
  assert.throws(() => readRunConfig(runEnv({ LOAD_BASE_URL: undefined })), /LOAD_BASE_URL is required/);
  assert.throws(() => readRunConfig(runEnv({ LOAD_BASE_URL: 'staging-api.example' })), /must be an http/);
  assert.throws(() => readRunConfig(runEnv({ LOAD_EXAM_ID: 'not-a-guid' })), /LOAD_EXAM_ID must be the exam GUID/);
  assert.throws(() => readRunConfig(runEnv({ LOAD_TEST_PASSWORD: '' })), /LOAD_TEST_PASSWORD is required/);
  assert.throws(() => readRunConfig(runEnv({ LOAD_TEST_ACCOUNT_COUNT: undefined })), /LOAD_TEST_ACCOUNT_COUNT is required/);
  assert.throws(() => readRunConfig(runEnv({ LOAD_TEST_ACCOUNT_COUNT: '0' })), /positive whole number/);
});

test('a pool of several accounts needs {n} in the email pattern, or every VU would share one login', () => {
  assert.throws(
    () => readRunConfig(runEnv({ LOAD_TEST_EMAIL_PATTERN: 'candidate@load.example' })),
    /must contain \{n\}/,
  );
  assert.doesNotThrow(() =>
    readRunConfig(runEnv({ LOAD_TEST_EMAIL_PATTERN: 'candidate@load.example', LOAD_TEST_ACCOUNT_COUNT: '1' })),
  );
});

test('config errors never echo the candidate password', () => {
  const password = 'do-not-print-this-password';
  try {
    readRunConfig(runEnv({ LOAD_TEST_PASSWORD: password, LOAD_EXAM_ID: 'bad' }));
    assert.fail('expected readRunConfig to throw');
  } catch (error) {
    assert.ok(!String(error.message).includes(password));
  }
});

function attempt(overrides = {}) {
  return {
    id: 'a1',
    sectionLockEnabled: false,
    activeSectionId: null,
    sections: [
      {
        id: 's1',
        name: 'Section 1',
        questions: [
          { id: 'q1', options: [{ id: 'o1' }, { id: 'o2' }] },
          { id: 'q2', options: [{ id: 'o3' }, { id: 'o4' }], allowsMultiple: true },
          { id: 'q3', options: [], isTextAnswer: true },
          { id: 'q4', options: [{ id: 'o5' }] },
        ],
      },
    ],
    ...overrides,
  };
}

test('singleChoiceAnswers keeps single-choice questions and drops multiple-answer and typed ones', () => {
  assert.deepEqual(singleChoiceAnswers(attempt()), [
    { questionId: 'q1', optionIds: ['o1', 'o2'] },
    { questionId: 'q4', optionIds: ['o5'] },
  ]);
});

test('singleChoiceAnswers offers only the active section while sections are locked', () => {
  const locked = attempt({
    sectionLockEnabled: true,
    activeSectionId: 's2',
    sections: [
      { id: 's1', questions: [{ id: 'q1', options: [{ id: 'o1' }] }] },
      { id: 's2', questions: [{ id: 'q9', options: [{ id: 'o9' }] }] },
    ],
  });
  assert.deepEqual(singleChoiceAnswers(locked), [{ questionId: 'q9', optionIds: ['o9'] }]);
  assert.deepEqual(
    singleChoiceAnswers(attempt({ sectionLockEnabled: true, activeSectionId: null })),
    [],
  );
});

test('singleChoiceAnswers tolerates an attempt with no sections or no questions', () => {
  assert.deepEqual(singleChoiceAnswers({ sections: [] }), []);
  assert.deepEqual(singleChoiceAnswers({}), []);
});

test('pickAnswer rotates questions, then changes the option on each pass, so each save is a real change', () => {
  const answers = [
    { questionId: 'qa', optionIds: ['a1', 'a2'] },
    { questionId: 'qb', optionIds: ['b1'] },
  ];
  const picks = [0, 1, 2, 3, 4].map((step) => pickAnswer(answers, step));
  assert.deepEqual(picks, [
    { questionId: 'qa', optionId: 'a1' },
    { questionId: 'qb', optionId: 'b1' },
    { questionId: 'qa', optionId: 'a2' },
    { questionId: 'qb', optionId: 'b1' },
    { questionId: 'qa', optionId: 'a1' },
  ]);
});

test('pickAnswer refuses to run with nothing to answer', () => {
  assert.throws(() => pickAnswer([], 0), /no single-choice question/);
});
