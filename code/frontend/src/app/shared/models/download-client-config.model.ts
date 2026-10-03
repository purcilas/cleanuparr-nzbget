import { DownloadClientType, DownloadClientTypeName } from './enums';

export interface ClientConfig {
  usenetOptions?: UsenetOptions;
  enabled: boolean;
  id: string;
  name: string;
  type: DownloadClientType;
  typeName: DownloadClientTypeName;
  host: string;
  username: string;
  password?: string;
  urlBase: string;
  externalUrl?: string;
  downloadDirectorySource?: string | null;
  downloadDirectoryTarget?: string | null;
}

export interface DownloadClientConfig {
  clients: ClientConfig[];
}

export interface CreateDownloadClientDto {
  usenetOptions?: UsenetOptions;
  enabled: boolean;
  name: string;
  type: DownloadClientType;
  typeName: DownloadClientTypeName;
  host?: string;
  username?: string;
  password?: string;
  urlBase?: string;
  externalUrl?: string;
  downloadDirectorySource?: string | null;
  downloadDirectoryTarget?: string | null;
}

export interface TestDownloadClientRequest {
  typeName: DownloadClientTypeName;
  type: DownloadClientType;
  host?: string;
  username?: string;
  password?: string;
  urlBase?: string;
  clientId?: string;
}

export interface TestConnectionResult {
  message: string;
  responseTime?: number;
}

export interface UsenetOptions {
  liveCleanupEnabled: boolean;
  liveValidationConfirmed: boolean;
  stallMinutes: number;
  processingStallMinutes: number;
  requiredObservations: number;
  maxObservationGapMinutes: number;
  maxActionsPerRun: number;
  maxActionsPerHour: number;
  replacementCooldownMinutes: number;
  recoveryTimeoutMinutes: number;
  minimumFreeDiskMiB: number;
}
export const DEFAULT_USENET_OPTIONS: UsenetOptions = {
  liveCleanupEnabled: false, liveValidationConfirmed: false,
  stallMinutes: 60, processingStallMinutes: 240, requiredObservations: 3,
  maxObservationGapMinutes: 10, maxActionsPerRun: 1, maxActionsPerHour: 1,
  replacementCooldownMinutes: 120, recoveryTimeoutMinutes: 30, minimumFreeDiskMiB: 1024,
};
export interface UsenetStatus {
  id: string; ownerId: string; lastSeenTicks: number; samples: number;
  incident: string; actionState: string; title?: string;
}
