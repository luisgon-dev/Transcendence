export type AdminServerSnapshot = {
  name: string;
  workersCount: number;
  startedAtUtc: string;
  heartbeatUtc: string | null;
  queues: string[];
};

export type AdminOverview = {
  generatedAtUtc: string;
  databaseConnected: boolean;
  enqueued: number;
  processing: number;
  scheduled: number;
  failed: number;
  succeeded: number;
  recurring: number;
  deleted: number;
  effectiveConcurrency: number;
  servers: AdminServerSnapshot[];
  queues: { name: string; length: number; fetched: number | null }[];
};

export type AdminAnalysisSummary = {
  summoners: number;
  matches: number;
  currentPatchSuccessfulMatches: number;
  currentPatchRankedMatches: number;
  timelineCoverageRatio: number;
  activeRefreshLocks: number;
  expiredRefreshLocks: number;
  trackedProSummoners: number;
};

export type AdminAnalysisRegionMetrics = {
  region: string;
  enabled: boolean;
  summoners: number;
  currentPatchTotalMatches: number;
  currentPatchSuccessfulMatches: number;
  currentPatchTemporaryFailures: number;
  currentPatchUnfetchedMatches: number;
  currentPatchPermanentlyUnfetchableMatches: number;
  currentPatchOutsideRetentionMatches: number;
  rankedCurrentPatchMatches: number;
  latestSuccessfulFetchUtc: string | null;
  timelineSuccessfulMatches: number;
  timelinePendingMatches: number;
  timelineTemporaryFailures: number;
  timelinePermanentFailures: number;
  trackedProSummoners: number;
  enqueuedJobs: number;
  processingJobs: number;
  scheduledJobs: number;
  health: string;
};

export type AdminAnalysisMetricsResponse = {
  generatedAtUtc: string;
  activePatchVersion: string | null;
  activePatchReleasedAtUtc: string | null;
  summary: AdminAnalysisSummary;
  regions: AdminAnalysisRegionMetrics[];
};

export type AdminBuildLabGeneration = {
  id: string;
  status: string;
  isActive: boolean;
  patch: string;
  rankScope: string;
  datasetVersion: string;
  modelVersion: string;
  codeRevision: string;
  sourceCutoffUtc: string;
  matchCount: number;
  actionEstimateCount: number;
  publishableActionCount: number;
  artifactUri: string | null;
  validationMetricsJson: string;
  failureReason: string | null;
  leaseOwner: string | null;
  leaseExpiresAtUtc: string | null;
  heartbeatAtUtc: string | null;
  promotionHistoryJson: string;
  createdAtUtc: string;
  completedAtUtc: string | null;
  promotedAtUtc: string | null;
};

/** One entry of AdminBuildLabGeneration.promotionHistoryJson. */
export type AdminBuildLabPromotionEntry = {
  action: string;
  atUtc: string;
  actor: string | null;
  reason: string | null;
};

export type AdminBuildLabGenerationResponse = {
  generations: AdminBuildLabGeneration[];
  activeChampionRoleScopes: number;
  activeMatchupScopes: number;
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
  isPresentInStorage: boolean;
  isEnabledByConfiguration: boolean;
  isPaused: boolean;
  isPausable: boolean;
};

export type AdminJobGroup = {
  state: string;
  queue: string;
  jobType: string | null;
  jobMethod: string | null;
  region: string | null;
  count: number;
  oldestSeenAtUtc: string | null;
  newestSeenAtUtc: string | null;
};

export type AdminQueueSummaryResponse = {
  generatedAtUtc: string;
  truncated: boolean;
  scanLimit: number;
  queues: { name: string; length: number; fetched: number | null }[];
  topGroups: AdminJobGroup[];
};

export type AdminJobListItem = {
  jobId: string;
  state: string;
  queue: string;
  jobType: string | null;
  jobMethod: string | null;
  region: string | null;
  createdAtUtc: string | null;
  stateChangedAtUtc: string | null;
  startedAtUtc: string | null;
  serverId: string | null;
  reason: string | null;
  exceptionType: string | null;
  exceptionMessage: string | null;
  arguments: string[];
};

export type AdminJobListResponse = {
  generatedAtUtc: string;
  from: number;
  count: number;
  totalMatched: number;
  truncated: boolean;
  scanLimit: number;
  items: AdminJobListItem[];
};

export type AdminJobStateTransition = {
  stateName: string;
  createdAtUtc: string;
  reason: string | null;
  data: Record<string, string>;
};

export type AdminJobDetail = {
  jobId: string;
  currentState: string | null;
  queue: string;
  jobType: string | null;
  jobMethod: string | null;
  arguments: string[];
  region: string | null;
  createdAtUtc: string | null;
  enqueuedAtUtc: string | null;
  scheduledAtUtc: string | null;
  startedAtUtc: string | null;
  stateChangedAtUtc: string | null;
  failedAtUtc: string | null;
  deletedAtUtc: string | null;
  serverId: string | null;
  reason: string | null;
  exceptionType: string | null;
  exceptionMessage: string | null;
  exceptionDetails: string | null;
  failedCount: number;
  states: AdminJobStateTransition[];
  properties: Record<string, string>;
};

export type AdminDeleteJobResult = {
  jobId: string;
  deleted: boolean;
  expectedState: string | null;
  currentState: string | null;
  message: string;
};

export type AdminBulkDeleteJobsResult = {
  dryRun: boolean;
  truncated: boolean;
  matched: number;
  deleted: number;
  failed: number;
  sampleJobIds: string[];
};

export type AdminFailedJob = {
  jobId: string;
  reason: string | null;
  exceptionType: string | null;
  exceptionMessage: string | null;
  failedAt: string | null;
};

export type AdminFailedJobDetail = AdminJobDetail;

export type AdminServiceLog = {
  timestampUtc: string;
  service: string;
  level: string;
  category: string;
  eventId: number;
  message: string | null;
  exception: string | null;
};

export type AdminLogSource = {
  service: string;
  available: boolean;
  filesScanned: number;
  latestTimestampUtc: string | null;
  truncated: boolean;
};

export type AdminServiceLogsResponse = {
  source: AdminLogSource;
  items: AdminServiceLog[];
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
  source: string;
  sourceExternalId: string | null;
  lastVerifiedAtUtc: string | null;
  otpChampionId: number | null;
  otpGames: number | null;
  otpSampleSize: number | null;
  otpEvaluatedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type ProPlayerDiscoveryCandidate = {
  id: string;
  source: string;
  externalId: string;
  proName: string;
  teamName: string | null;
  role: string | null;
  soloQueueIds: string | null;
  status: string;
  approvedTrackedProSummonerId: string | null;
  firstSeenAtUtc: string;
  lastSeenAtUtc: string;
  reviewedAtUtc: string | null;
};
