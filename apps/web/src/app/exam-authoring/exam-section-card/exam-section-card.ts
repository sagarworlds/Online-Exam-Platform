import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { QUESTION_DIFFICULTIES, QuestionDifficulty, QuestionDto } from '../../question-bank/question.models';
import { PlainTextPipe } from '../../shared/rich-text/plain-text.pipe';
import { htmlToPlainText } from '../../shared/rich-text/html-to-text';
import { DrawQuestionsRequest, DrawRuleDto, ExamQuestionDto, ExamScopeType, ExamSectionDto, MAX_DRAW_COUNT } from '../exam.models';

/**
 * One section of an exam in the editor (FR-11): its name, its questions in order, and, while the exam is a draft, what the
 * author may do with them: rename the section, remove it, take a question out, add one from the bank. It only shows and
 * asks; the editor page makes the requests, so a card never needs to know how the exam is stored or reloaded.
 */
@Component({
  selector: 'app-exam-section-card',
  imports: [PlainTextPipe, TranslatePipe],
  templateUrl: './exam-section-card.html',
})
export class ExamSectionCard {
  private readonly i18n = inject(I18nService);

  readonly section = input.required<ExamSectionDto>();
  /** Whether the section can be changed, which is only while the exam is a draft; a published exam is shown read-only. */
  readonly editable = input(false);
  /** True while a request about the exam is running, so its buttons cannot be pressed twice. */
  readonly busy = input(false);
  /** The bank questions that may go into this exam (not already in it, and inside its scope). */
  readonly choices = input<readonly QuestionDto[]>([]);
  /** What the exam's questions may come from, which decides what the hint under the picker says. */
  readonly scope = input<ExamScopeType>('Independent');

  /** Topics in use in the bank, offered in the draw's topic picker. */
  readonly topics = input<readonly string[]>([]);

  /** The author asked for a rule that draws random questions for each candidate when they start. */
  readonly drawRuleRequested = output<{ sectionId: string; request: DrawQuestionsRequest }>();
  /** The author asked to take a draw rule out. */
  readonly drawRuleRemoveRequested = output<{ sectionId: string; ruleId: string }>();

  /** The author asked for random questions to be drawn into this section. */
  readonly drawRequested = output<{ sectionId: string; request: DrawQuestionsRequest }>();

  /** The author saved a new name for the section. */
  readonly renameRequested = output<{ sectionId: string; name: string }>();
  /** The author confirmed removing the section; carries its id. */
  readonly removeConfirmed = output<string>();
  /** The author asked to add a question; `questionId` is empty when none was chosen, which the page says out loud. */
  readonly addRequested = output<{ sectionId: string; questionId: string }>();
  /** The author asked to take a question out; `questionId` is its id in the question bank. */
  readonly takeOutRequested = output<{ sectionId: string; questionId: string }>();

  protected readonly renaming = signal(false);
  protected readonly nameDraft = signal('');
  protected readonly confirmingRemove = signal(false);

  /** The draw row: closed until asked for, since most sections are filled by choosing questions one at a time. */
  protected readonly drawing = signal(false);
  protected readonly drawCount = signal(5);
  protected readonly drawDifficulty = signal<QuestionDifficulty | ''>('');
  protected readonly drawTopic = signal('');
  /** Whether the draw becomes a rule, drawn again for every candidate, instead of picking the questions now. */
  protected readonly drawPerCandidate = signal(false);
  protected readonly difficulties = QUESTION_DIFFICULTIES;
  protected readonly maxDrawCount = MAX_DRAW_COUNT;
  protected readonly drawCountValid = computed(() => Number.isInteger(this.drawCount()) && this.drawCount() >= 1 && this.drawCount() <= MAX_DRAW_COUNT);

  /** The name the section had when the rename row was last reset; undefined before the first look. */
  private shownName: string | undefined;

  constructor() {
    // Once the section carries its new name the rename row has done its job; close it. A refusal leaves the name as it
    // was, so the row stays open with what the author typed.
    effect(() => {
      const name = this.section().name;
      untracked(() => {
        if (name !== this.shownName) {
          this.shownName = name;
          this.renaming.set(false);
        }
      });
    });
  }

  protected startRenaming(): void {
    this.nameDraft.set(this.section().name);
    this.renaming.set(true);
  }

  protected saveName(event: Event): void {
    event.preventDefault();
    const name = this.nameDraft().trim();
    if (name !== '' && name !== this.section().name) {
      this.renameRequested.emit({ sectionId: this.section().id, name });
    }
  }

  protected draw(): void {
    if (!this.drawCountValid()) {
      return;
    }

    const target = {
      sectionId: this.section().id,
      request: { count: this.drawCount(), difficulty: this.drawDifficulty() || null, topic: this.drawTopic() || null },
    };
    (this.drawPerCandidate() ? this.drawRuleRequested : this.drawRequested).emit(target);
  }

  /** A short description of what a rule draws, e.g. "5 easy questions on algebra". */
  protected describeRule(rule: DrawRuleDto): string {
    const level = rule.difficulty ? `${rule.difficulty} ` : '';
    const topic = rule.topic ? ` ${this.i18n.t('exams.section.onTopic', { topic: rule.topic })}` : '';
    return this.i18n.t('exams.section.ruleLine', {
      count: rule.count,
      level,
      noun: this.i18n.plural('exams.section.questionNoun', rule.count),
      topic,
    });
  }

  /** The question asked before a section is removed; it says how many questions go with it. */
  protected removeConfirmText(): string {
    const count = this.section().questions.length;
    const andItsQuestions = count > 0 ? ` ${this.i18n.plural('exams.section.andItsQuestions', count)}` : '';
    return this.i18n.t('exams.section.removeConfirm', { andItsQuestions });
  }

  /** The draw's one-line explanation of where its questions come from: the bank, or this exam's book or chapters. */
  protected drawScopeText(): string {
    const inside =
      this.scope() === 'Independent' ? '' : this.i18n.t(this.scope() === 'Book' ? 'exams.section.insideBook' : 'exams.section.insideChapters');
    return this.i18n.t('exams.section.drawPicks', { inside });
  }

  protected confirmRemove(): void {
    this.confirmingRemove.set(false);
    this.removeConfirmed.emit(this.section().id);
  }

  /** What a screen reader says for a question's Remove button; the visible label is just "Remove". */
  protected removeLabel(question: ExamQuestionDto): string {
    const text = question.text ? htmlToPlainText(question.text, 60) : this.i18n.t('exams.section.questionGoneLong');
    return this.i18n.t('exams.section.removeQuestionLabel', { question: text });
  }
}
