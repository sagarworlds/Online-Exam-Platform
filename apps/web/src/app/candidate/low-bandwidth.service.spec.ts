import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, vi } from 'vitest';
import { LOW_BANDWIDTH_HEARTBEAT_MS, LowBandwidthService, NORMAL_HEARTBEAT_MS } from './low-bandwidth.service';

describe('LowBandwidthService (FR-53)', () => {
  // The test browser has no network information of its own, so it is put on the navigator for the test and taken off after it.
  const connection = (value: object | undefined) => Object.defineProperty(globalThis.navigator, 'connection', { value, configurable: true });

  const create = () => TestBed.inject(LowBandwidthService);

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    delete (globalThis.navigator as { connection?: unknown }).connection;
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('is off for a device that says nothing about its connection', () => {
    connection(undefined);

    const service = create();

    expect(service.suggested()).toBe(false);
    expect(service.enabled()).toBe(false);
    expect(service.heartbeatMs()).toBe(NORMAL_HEARTBEAT_MS);
  });

  it.each([{ saveData: true }, { effectiveType: '2g' }, { effectiveType: 'slow-2g' }])('follows a device that is saving data or on 2G (%o)', (info) => {
    connection(info);

    const service = create();

    expect(service.suggested()).toBe(true);
    expect(service.enabled()).toBe(true);
    expect(service.heartbeatMs()).toBe(LOW_BANDWIDTH_HEARTBEAT_MS);
  });

  it('is off for a device on a fast connection', () => {
    connection({ effectiveType: '4g', saveData: false });

    expect(create().enabled()).toBe(false);
  });

  it('turns on when the candidate turns it on, and remembers it on this device', () => {
    connection(undefined);
    const service = create();

    service.set(true);

    expect(service.enabled()).toBe(true);
    expect(service.heartbeatMs()).toBe(LOW_BANDWIDTH_HEARTBEAT_MS);
    expect(localStorage.getItem('exam.lowBandwidth')).toBe('on');
  });

  it('lets the candidate turn it off even when the device suggests it, and remembers that too', () => {
    connection({ saveData: true });
    const service = create();

    service.set(false);

    expect(service.suggested()).toBe(true);
    expect(service.enabled()).toBe(false);
    expect(localStorage.getItem('exam.lowBandwidth')).toBe('off');
  });

  it('starts from what was remembered, over what the device suggests', () => {
    connection({ saveData: true });
    localStorage.setItem('exam.lowBandwidth', 'off');
    TestBed.resetTestingModule();

    expect(create().enabled()).toBe(false);
  });

  it('works without a store that can be written to', () => {
    connection(undefined);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const service = create();

    service.set(true);

    expect(service.enabled()).toBe(true);
  });
});
