/** A student account as the staff list shows it. The date of birth is left out; open the student for that. */
export interface StudentSummaryDto {
  id: string;
  displayName: string;
  email: string | null;
  phoneNumber: string | null;
  status: string;
}

export type AgeBand = 'Minor' | 'Adult';

/** One student's profile, with the date of birth staff may correct (`yyyy-MM-dd`). */
export interface StudentDto {
  id: string;
  displayName: string;
  email: string | null;
  phoneNumber: string | null;
  dateOfBirth: string;
  ageBand: AgeBand;
  status: string;
}

/** The profile as it should be: every field is sent, and a blank e-mail address or number means the account has none. */
export interface UpdateStudentRequest {
  displayName: string;
  email: string | null;
  phoneNumber: string | null;
  dateOfBirth: string;
}

/** The fields a change can touch, as the API names them. */
export type StudentField = 'name' | 'email' | 'phoneNumber' | 'dateOfBirth';

/** What changing a student's profile did. */
export interface UpdateStudentResult {
  student: StudentDto;
  /** Empty when nothing was different. */
  changed: StudentField[];
  invitationsMoved: number;
  /** Whether the student's sessions were ended, which happens when their e-mail address or mobile number changed. */
  signedOut: boolean;
}

/** The body of POST /v1/admin/students/lookup. */
export interface FindStudentsRequest {
  emails: string[];
}
