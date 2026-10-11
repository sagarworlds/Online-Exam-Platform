/** An instruction template as the API returns it (FR-41). */
export interface InstructionTemplateDto {
  id: string;
  title: string;
  body: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

/** What the template form sends (FR-41). */
export interface InstructionTemplateRequest {
  title: string;
  body: string;
}

/** The longest title a template may have. The same limit as the API's `InstructionTemplate.MaxTitleLength`. */
export const MAX_TEMPLATE_TITLE_LENGTH = 120;

/** The longest instructions text an exam or a template may hold. The same limit as the API's `Exam.MaxInstructionsLength`. */
export const MAX_INSTRUCTIONS_LENGTH = 4000;
