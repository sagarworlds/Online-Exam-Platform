import { Injectable } from '@angular/core';
import { AttemptDto } from '../candidate.models';

/**
 * How long a started attempt may wait for the exam page to open. Past this the page asks the server again: the reading of the server's
 * clock that came with the attempt is the start of the countdown, and an old one would put it out.
 */
export const HANDOFF_MAX_AGE_MS = 30_000;

/** An attempt handed from the page that started it, with the moment it arrived. */
export interface HandedAttempt {
  attempt: AttemptDto;
  /** The candidate's clock when the attempt arrived, which is what the server's reading of its own clock is to be compared with. */
  receivedAt: number;
}

/**
 * Carries the attempt the server returned when it was started to the exam page that shows it, so the page does not ask for the same
 * questions again: the answer to starting an attempt is the attempt, with every picture in it, and on a slow connection fetching it twice
 * is most of the wait before the first question. It is held in memory for one use, so a reload, or any later visit to the page, asks the
 * server as it always did, and the server stays the source of what the candidate sees.
 */
@Injectable({ providedIn: 'root' })
export class AttemptHandoff {
  private held: (HandedAttempt & { lowBandwidth: boolean }) | null = null;

  /**
   * Holds a started attempt for the exam page that is about to open.
   * @param attempt What starting the attempt returned.
   * @param lowBandwidth Whether it was asked for without its pictures; the page only takes it when its own mode is the same.
   * @param receivedAt When it arrived, by the candidate's clock.
   */
  put(attempt: AttemptDto, lowBandwidth: boolean, receivedAt: number = Date.now()): void {
    this.held = { attempt, lowBandwidth, receivedAt };
  }

  /**
   * Gives the held attempt to the page that opens it, once. Whatever is held is dropped either way, so it can never be shown later than the
   * first page that asks. Nothing is given for another attempt, for one that is not open, for one asked for in the other bandwidth mode, or
   * for one that has waited too long: the page then reads it from the server.
   * @param attemptId The attempt the page is opening.
   * @param lowBandwidth The page's own mode.
   * @param now The candidate's clock now.
   */
  take(attemptId: string, lowBandwidth: boolean, now: number = Date.now()): HandedAttempt | null {
    const held = this.held;
    this.held = null;

    if (held === null || held.attempt.id !== attemptId || held.attempt.status !== 'InProgress' || held.lowBandwidth !== lowBandwidth) {
      return null;
    }

    const age = now - held.receivedAt;
    return age < 0 || age > HANDOFF_MAX_AGE_MS ? null : { attempt: held.attempt, receivedAt: held.receivedAt };
  }
}
