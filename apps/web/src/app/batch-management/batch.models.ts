export type BatchStatus = 'Draft' | 'Active' | 'Closed' | 'Archived';
export type MemberRegistrationStatus = 'Pending' | 'Registered' | 'Verified' | 'Inactive';

export interface BatchDto {
  id: string;
  examId: string;
  name: string;
  description?: string;
  status: BatchStatus;
  maxMembers: number;
  activeMemberCount: number;
  createdBy: string;
  createdAt: Date;
  updatedAt: Date;
}

export interface BatchMemberDto {
  id: string;
  email: string;
  phone?: string;
  status: MemberRegistrationStatus;
}

export interface CreateBatchRequest {
  examId: string;
  name: string;
  description?: string;
  maxMembers: number;
}

export interface AddBatchMemberRequest {
  email: string;
  phone?: string;
}
