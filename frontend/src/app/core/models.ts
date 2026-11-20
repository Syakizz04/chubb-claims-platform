// Types mirror the backend's OpenAPI contract (backend/src/Claims.Api/Features/Dtos.cs and
// Insights.Api/Dashboard/DashboardCalculator.cs). The backend owns the contract; these follow it.

export type Role = 'Claimant' | 'Handler' | 'Manager';
export type ClaimType = 'Motor' | 'Property';
export type ClaimStatus = 'Submitted' | 'UnderReview' | 'AwaitingInfo' | 'Approved' | 'Rejected' | 'Settled';
export type ClaimEventType =
  | 'Submitted' | 'PickedUp' | 'InformationRequested' | 'InformationProvided'
  | 'ReserveAdjusted' | 'Approved' | 'Rejected' | 'Settled' | 'NoteAdded';
export type StaffAction = 'PickUp' | 'RequestInfo' | 'AdjustReserve' | 'Approve' | 'Reject' | 'Settle' | 'AddNote';

export interface User { id: string; displayName: string; role: Role; }
export interface Market { code: string; name: string; currencyCode: string; minorUnits: number; timeZoneId: string; }

export interface TimelineEntry {
  sequence: number;
  type: ClaimEventType;
  fromStatus: ClaimStatus | null;
  toStatus: ClaimStatus | null;
  actorName: string;
  note: string | null;
  amount: number | null;
  previousAmount: number | null;
  isInternal: boolean;
  occurredAt: string;
}

export interface InformationRequest {
  id: string;
  question: string;
  requestedAt: string;
  response: string | null;
  respondedAt: string | null;
}

interface ClaimBase {
  id: string;
  reference: string;
  marketCode: string;
  currencyCode: string;
  type: ClaimType;
  status: ClaimStatus;
  claimedAmount: number;
  submittedAt: string;
  updatedAt: string;
}

export interface ClaimantClaimSummary extends ClaimBase { actionRequired: boolean; }

interface ClaimDetailBase extends ClaimBase {
  minorUnits: number;
  policyNumber: string;
  incidentDate: string;
  incidentLocation: string;
  description: string;
  vehicleRegistration: string | null;
  propertyAddress: string | null;
  approvedAmount: number | null;
  settledAmount: number | null;
  rejectionReason: string | null;
  decidedAt: string | null;
  closedAt: string | null;
  informationRequests: InformationRequest[];
  timeline: TimelineEntry[];
}

export interface ClaimantClaimDetail extends ClaimDetailBase {
  openInformationRequest: InformationRequest | null;
}

export interface StaffClaimSummary extends ClaimBase {
  claimantName: string;
  assignedHandlerId: string | null;
  assignedHandlerName: string | null;
  reserveAmount: number;
  ageDays: number;
}

export interface StaffClaimDetail extends ClaimDetailBase {
  claimantId: string;
  claimantName: string;
  assignedHandlerId: string | null;
  assignedHandlerName: string | null;
  reserveAmount: number;
  ageDays: number;
  availableActions: StaffAction[];
}

export interface SubmitClaimRequest {
  marketCode: string;
  type: ClaimType;
  policyNumber: string;
  incidentDate: string;
  incidentLocation: string;
  description: string;
  claimedAmount: number;
  vehicleRegistration?: string | null;
  propertyAddress?: string | null;
}

// ---------------------------------------------------------------- insights

export interface TypeExposure { claimType: string; amountUsd: number | null; openClaims: number; amount?: number; currencyCode?: string; }
export interface MarketExposure {
  marketCode: string; currencyCode: string; amount: number; amountUsd: number | null; openClaims: number; byType: TypeExposure[];
}
export interface ExposureReport {
  totalUsd: number; openClaims: number; ratesAsOf: string; ratesNote: string;
  markets: MarketExposure[]; byType: TypeExposure[]; currenciesWithoutRate: string[];
}
export interface HandlerWorkload {
  handlerId: string; handlerName: string; openClaims: number; awaitingInfo: number; oldestOpenDays: number | null; reserveUsd: number;
}
export interface WorkloadReport { unassignedCount: number; oldestUnassignedDays: number | null; handlers: HandlerWorkload[]; }
export interface AgeingBucket { label: string; minDays: number; maxDays: number | null; count: number; reserveUsd: number; }
export interface HandlerPerformance {
  handlerId: string; handlerName: string; decided30d: number; approved30d: number; rejected30d: number; avgDaysToDecision: number | null;
}
export interface DashboardReport {
  generatedAt: string;
  lastProcessedAt: string | null;
  exposure: ExposureReport;
  workload: WorkloadReport;
  ageing: AgeingBucket[];
  performance: HandlerPerformance[];
}
export interface ClaimantNotification { id: string; claimId: string; reference: string; message: string; createdAt: string; }

// ---------------------------------------------------------------- errors (RFC 7807)

export interface Problem {
  status?: number;
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
  currentStatus?: ClaimStatus;
}
