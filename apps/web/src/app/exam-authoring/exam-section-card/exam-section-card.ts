import { Component, ElementRef, Injector, afterNextRender, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { QUESTION_DIFFICULTIES, QuestionDifficulty } from '../../question-bank/question.models';
import { Disclosure } from '../../shared/disclosure/disclosure';
import { Icon } from '../../shared/icon/icon';
import { PlainTextPipe } from '../../shared/rich-text/plain-text.pipe';
import { Tooltip } from '../../shared/tooltip/tooltip';
import { htmlToPlainText } from '../../shared/rich-text/html-to-text';
import { ConfirmRemove } from '../confirm-remove/confirm-remove';
import { DrawQuestionsRequest, DrawRuleDto, ExamQuestionDto, ExamScopeDto, ExamSectionDto, MAX_DRAW_COUNT } from '../exam.models';
import { QuestionPicker } from '../question-picker/question-picker';
import { MoveDirection } from '../reorder';
import { MathTextDirective } from '../../shared/rich-text/math-text.directive';

/**
 * One section of an exam in the editor (FR-11): its name, its questions in order, and, while the exam is a draft, what the
 * author may do with them: rename the section, remove it, take a question out, add one from the bank. It only shows and
 * asks; the editor page makes the requests, so a card never needs to know how the exam is stored or reloaded.
 */
@Component({
  selector: 'app-exam-section-card',
  imports: [ConfirmRemove, Disclosure, Icon, PlainTextPipe, QuestionPicker, Tooltip, TranslatePipe, MathTextDirective],
  templateUrl: './exam-section-card.html',
  styleUrl: './exam-section-card.css',
})
export class ExamSectionCard {
  private readonly i18n = inject(I18nService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly section = input.required<ExamSectionDto>();
  /** This section's place among the exam's sections, counting from 0, and how many there are: the first cannot move up, nor the last down. */
  readonly sectionIndex = input(0);
  readonly sectionCount = input(1);
  /** Whether the section can be changed, which is only while the exam is a draft; a published exam is shown read-only. */
  readonly editable = input(false);
  /** True while a request about the exam is running, so its buttons cannot be pressed twice. */
  readonly busy = input(false);
  /** What the exam's questions may come from: the question picker lists only what the exam will accept, and the draw says where it picks from. */
  readonly examScope = input<ExamScopeDto | undefined>(undefined);
  /** The bank ids of the questions already in the exam, which the picker leaves out: a question goes into an exam once. */
  readonly excluded = input<ReadonlySet<string>>(new Set());

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
  /** The author chose a question from the bank to add; `questionId` is its id there. */
  readonly addRequested = output<{ sectionId: string; questionId: string }>();
  /** The author chose several questions from the bank to add at once; `questionIds` are their ids there, in the order they are listed. */
  readonly addManyRequested = output<{ sectionId: string; questionIds: readonly string[] }>();
  /** The author asked to move this section a place up or down among the exam's sections. */
  readonly moveSectionRequested = output<{ sectionId: string; direction: MoveDirection }>();
  /** The author asked to move a question a place up or down in this section; `questionId` is its id in the question bank. */
  readonly moveQuestionRequested = output<{ sectionId: string; questionId: string; direction: MoveDirection }>();
  /** The author asked to take a question out; `questionId` is its id in the question bank. */
  readonly takeOutRequested = output<{ sectionId: string; questionId: string }>();

  protected readonly scope = computed(() => this.examScope()?.type ?? 'Independent');
  protected readonly directions: readonly MoveDirection[] = ['up', 'down'];

  /** Whether the picker is open. It is closed until asked for, and the questions are not read from the bank until then. */
  protected readonly picking = signal(false);

  protected readonly renaming = signal(false);
  protected readonly nameDraft = signal('');

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

  /** The control that had focus when something was moved, so focus can be put back once the page has read the exam again. */
  private focusAfterMove: { selector: string; other: string } | null = null;

  /** Each question's text as one line, by its id in the bank, worked out when the section changes and not at every look at the page. */
  private readonly plainTexts = computed(
    () => new Map(this.section().questions.map((question) => [question.questionId, question.text ? htmlToPlainText(question.text, 60) : ''])),
  );

  constructor() {
    // A move takes the place of the control that had focus (the item has moved, and the list is drawn again); once the page is done with it
    // the focus goes back to the same arrow, or to the other one when this one has nowhere left to go.
    effect(() => {
      if (!this.busy()) {
        untracked(() => this.restoreMoveFocus());
      }
    });

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

  /** Whether a section can go in this direction: not off either end of the exam's sections, and not while the page is busy. */
  protected canMoveSection(direction: MoveDirection): boolean {
    return !this.busy() && (direction === 'up' ? this.sectionIndex() > 0 : this.sectionIndex() < this.sectionCount() - 1);
  }

  protected moveSection(direction: MoveDirection): void {
    if (this.canMoveSection(direction)) {
      this.focusAfterMove = { selector: `[data-section-move="${direction}"]`, other: `[data-section-move="${direction === 'up' ? 'down' : 'up'}"]` };
      this.moveSectionRequested.emit({ sectionId: this.section().id, direction });
    }
  }

  /** Whether a question can go in this direction: not off either end of the section, and not while the page is busy. */
  protected canMoveQuestion(index: number, direction: MoveDirection): boolean {
    return !this.busy() && (direction === 'up' ? index > 0 : index < this.section().questions.length - 1);
  }

  protected moveQuestion(question: ExamQuestionDto, index: number, direction: MoveDirection): void {
    if (this.canMoveQuestion(index, direction)) {
      const mine = (which: string) => `[data-question="${question.questionId}"][data-move="${which}"]`;
      this.focusAfterMove = { selector: mine(direction), other: mine(direction === 'up' ? 'down' : 'up') };
      this.moveQuestionRequested.emit({ sectionId: this.section().id, questionId: question.questionId, direction });
    }
  }

  /** What a screen reader says for a question's move buttons, which have no words of their own. */
  protected moveQuestionLabel(question: ExamQuestionDto, direction: MoveDirection): string {
    const text = this.plainTexts().get(question.questionId) || this.i18n.t('exams.section.questionGoneLong');
    return this.i18n.t(direction === 'up' ? 'exams.section.moveQuestionUp' : 'exams.section.moveQuestionDown', { question: text });
  }

  protected moveSectionLabel(direction: MoveDirection): string {
    return this.i18n.t(direction === 'up' ? 'exams.section.moveSectionUp' : 'exams.section.moveSectionDown', { name: this.section().name });
  }

  /** Why a move button does nothing, said in its tooltip, so an arrow that is dimmed is not a mystery. */
  protected whyNot(direction: MoveDirection): string {
    return this.i18n.t(direction === 'up' ? 'exams.section.alreadyFirst' : 'exams.section.alreadyLast');
  }

  private restoreMoveFocus(): void {
    const wanted = this.focusAfterMove;
    if (wanted === null) {
      return;
    }
    this.focusAfterMove = null;
    afterNextRender(
      () => {
        const root = this.host.nativeElement;
        const usable = (selector: string) => {
          const found = root.querySelector<HTMLElement>(selector);
          return found !== null && found.getAttribute('aria-disabled') !== 'true' ? found : null;
        };
        (usable(wanted.selector) ?? usable(wanted.other))?.focus();
      },
      { injector: this.injector },
    );
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

  /** What a screen reader says for a question's trash icon, which has no words of its own. */
  protected removeLabel(question: ExamQuestionDto): string {
    const text = this.plainTexts().get(question.questionId) || this.i18n.t('exams.section.questionGoneLong');
    return this.i18n.t('exams.section.removeQuestionLabel', { question: text });
  }
}
