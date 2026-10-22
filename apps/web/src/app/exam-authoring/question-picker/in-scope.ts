import { QuestionDto } from '../../question-bank/question.models';
import { ExamScopeDto } from '../exam.models';

/** Whether a question may go into an exam with this scope; mirrors the rule the API enforces. */
export function isInScope(question: QuestionDto, scope: ExamScopeDto | undefined): boolean {
  switch (scope?.type) {
    case 'Book':
      return question.bookId !== null && question.bookId === scope.bookId;
    case 'Chapters':
      return question.chapterId !== null && scope.chapters.some((chapter) => chapter.id === question.chapterId);
    default:
      return true;
  }
}
