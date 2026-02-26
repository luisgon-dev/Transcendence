export type AdminOverview = {
  generatedAtUtc: string;
  databaseConnected: boolean;
  enqueued: number;
  processing: number;
  scheduled: number;
  failed: number;
  succeeded: number;
  recurring: number;
  queues: { name: string; length: number; fetched: number }[];
};

export type AdminRecurringJob = {
  id: string;
  queue: string;
  cron: string;
  nextExecution: string | null;
  lastExecution: string | null;
  lastJobId: string | null;
  lastJobState: string | null;
  error: string | null;
};

export type AdminFailedJob = {
  jobId: string;
  reason: string | null;
  exceptionType: string | null;
  exceptionMessage: string | null;
  failedAt: string | null;
};

export type AdminAuditEntry = {
  id: string;
  actorUserAccountId: string | null;
  actorEmail: string | null;
  action: string;
  targetType: string | null;
  targetId: string | null;
  requestId: string | null;
  isSuccess: boolean;
  metadataJson: string | null;
  createdAtUtc: string;
};

export type ApiKeyListItem = {
  id: string;
  name: string;
  prefix: string;
  isRevoked: boolean;
  createdAt: string;
  expiresAt: string | null;
  lastUsedAt: string | null;
};

export type ProSummoner = {
  id: string;
  puuid: string;
  platformRegion: string;
  gameName: string | null;
  tagLine: string | null;
  proName: string | null;
  teamName: string | null;
  isPro: boolean;
  isHighEloOtp: boolean;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
};
