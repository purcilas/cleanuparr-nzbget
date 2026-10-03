import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { form, required, min, max, validate, FormField } from '@angular/forms/signals';
import { PageHeaderComponent } from '@layout/page-header/page-header.component';
import {
  CardComponent, ButtonComponent, InputComponent, ToggleComponent, NumberInputComponent,
  SelectComponent, ModalComponent, EmptyStateComponent, BadgeComponent, LoadingStateComponent,
  type SelectOption,
} from '@ui';
import { DownloadClientApi } from '@core/api/download-client.api';
import { ApiError } from '@core/interceptors/error.interceptor';
import { ToastService } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import {
  ClientConfig, CreateDownloadClientDto, TestDownloadClientRequest, UsenetOptions, DEFAULT_USENET_OPTIONS, UsenetStatus,
} from '@shared/models/download-client-config.model';
import { DownloadClientType, DownloadClientTypeName } from '@shared/models/enums';
import { HasPendingChanges } from '@core/guards/pending-changes.guard';
import { createSettingsResource } from '@shared/utils/settings-resource.util';

const TYPE_OPTIONS: SelectOption[] = [
  { label: 'qBittorrent', value: DownloadClientTypeName.qBittorrent },
  { label: 'Deluge', value: DownloadClientTypeName.Deluge },
  { label: 'Transmission', value: DownloadClientTypeName.Transmission },
  { label: 'uTorrent', value: DownloadClientTypeName.uTorrent },
  { label: 'rTorrent', value: DownloadClientTypeName.rTorrent },
  { label: 'NZBGet (Usenet)', value: DownloadClientTypeName.NZBGet },
];

const AUTOFILL_URL_BASES: Partial<Record<DownloadClientTypeName, string>> = {
  [DownloadClientTypeName.Transmission]: 'transmission',
  [DownloadClientTypeName.rTorrent]: 'plugins/httprpc/action.php',
};

interface DownloadClientFormModel extends UsenetOptions {
  enabled: boolean;
  name: string;
  typeName: DownloadClientTypeName;
  host: string;
  username: string;
  password: string;
  urlBase: string;
  externalUrl: string;
  downloadDirectorySource: string;
  downloadDirectoryTarget: string;
}

@Component({
  selector: 'app-download-clients',
  standalone: true,
  imports: [
    PageHeaderComponent, CardComponent, ButtonComponent, InputComponent, NumberInputComponent,
    ToggleComponent, SelectComponent, ModalComponent, EmptyStateComponent,
    BadgeComponent, LoadingStateComponent, FormField,
  ],
  templateUrl: './download-clients.component.html',
  styleUrl: './download-clients.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DownloadClientsComponent implements HasPendingChanges {
  private readonly api = inject(DownloadClientApi);
  private readonly toast = inject(ToastService);
  private readonly confirmService = inject(ConfirmService);

  private readonly settings = createSettingsResource({
    load: () => this.api.getConfig(),
    errorMessage: 'Failed to load download clients',
  });
  private readonly clientsResource = this.settings.resource;

  readonly typeOptions = TYPE_OPTIONS;
  readonly loader = this.settings.loader;
  readonly loadError = this.settings.loadError;
  readonly saving = signal(false);
  readonly clients = computed(() =>
    this.clientsResource.hasValue() ? (this.clientsResource.value().clients ?? []) : [],
  );

  // Modal
  readonly modalVisible = signal(false);
  readonly editingClient = signal<ClientConfig | null>(null);
  readonly testing = signal(false);

  readonly clientModel = signal<DownloadClientFormModel>({
    ...DEFAULT_USENET_OPTIONS, enabled: true, name: '', typeName: DownloadClientTypeName.qBittorrent,
    host: '', username: '', password: '', urlBase: '', externalUrl: '',
    downloadDirectorySource: '', downloadDirectoryTarget: '',
  });
  readonly clientForm = form(this.clientModel, (p) => {
    required(p.name, { message: 'Name is required' });
    required(p.host, { message: 'Host is required' });
    min(p.stallMinutes, 5, { message: 'Minimum is 5' });
    max(p.stallMinutes, 10080, { message: 'Maximum is 10080' });
    min(p.processingStallMinutes, 60, { message: 'Minimum is 60' });
    max(p.processingStallMinutes, 10080, { message: 'Maximum is 10080' });
    min(p.requiredObservations, 2, { message: 'Minimum is 2' });
    max(p.requiredObservations, 100, { message: 'Maximum is 100' });
    min(p.maxObservationGapMinutes, 1, { message: 'Minimum is 1' });
    max(p.maxObservationGapMinutes, 60, { message: 'Maximum is 60' });
    min(p.maxActionsPerRun, 1, { message: 'Minimum is 1' });
    max(p.maxActionsPerRun, 5, { message: 'Maximum is 5' });
    min(p.maxActionsPerHour, 1, { message: 'Minimum is 1' });
    max(p.maxActionsPerHour, 10, { message: 'Maximum is 10' });
    min(p.replacementCooldownMinutes, 30, { message: 'Minimum is 30' });
    max(p.replacementCooldownMinutes, 10080, { message: 'Maximum is 10080' });
    min(p.recoveryTimeoutMinutes, 5, { message: 'Minimum is 5' });
    max(p.recoveryTimeoutMinutes, 1440, { message: 'Maximum is 1440' });
    min(p.minimumFreeDiskMiB, 256, { message: 'Minimum is 256' });
    max(p.minimumFreeDiskMiB, 1048576, { message: 'Maximum is 1048576' });
    validate(p.liveCleanupEnabled, ({ value, valueOf }) => value() && !valueOf(p.liveValidationConfirmed) ? { kind: 'validation', message: 'Complete live validation first' } : undefined);

  });

  readonly hasModalErrors = computed(() => this.clientForm().invalid());

  /** JSON snapshot of the model as loaded when the modal opened, for dirty tracking. */
  private readonly openSnapshot = signal('');
  private readonly modalDirty = computed(() =>
    this.modalVisible() && JSON.stringify(this.clientModel()) !== this.openSnapshot());

  readonly showUsernameField = computed(() => {
    return this.clientModel().typeName !== DownloadClientTypeName.Deluge;
  });

  readonly isUsenet = computed(() => this.clientModel().typeName === DownloadClientTypeName.NZBGet);
  readonly reviewingId = signal<string | null>(null);
  readonly statuses = signal<Record<string, UsenetStatus[]>>({});

  loadUsenetStatus(id: string): void {
    this.api.usenetStatus(id).subscribe({
      next: rows => this.statuses.update(all => ({ ...all, [id]: rows })),
      error: (err: ApiError) => this.toast.error(err.message),
    });
  }

  async reviewUsenetRecovery(id: string): Promise<void> {
    if (!await this.confirmService.confirm({
      title: 'Review recovery hold',
      message: 'Confirm that you inspected NZBGet and the owning arr and resolved the incident. Monitoring will wait for renewed progress before allowing recovery. Previously attempted jobs will never be retried automatically.',
      confirmLabel: 'I reviewed the queues',
    })) return;
    this.reviewingId.set(id);
    this.api.reviewUsenetRecovery(id).subscribe({
      next: result => { this.reviewingId.set(null); this.toast.success(result.message); this.loadUsenetStatus(id); },
      error: (err: ApiError) => { this.reviewingId.set(null); this.toast.error(err.message); },
    });
  }

  private usenetOptions(m: DownloadClientFormModel): UsenetOptions {
    return Object.fromEntries(Object.keys(DEFAULT_USENET_OPTIONS).map(key => [key, m[key as keyof UsenetOptions]])) as unknown as UsenetOptions;
  }

  readonly showPasswordField = computed(() => true);

  readonly usernameHint = computed(() => {
    if (this.clientModel().typeName === DownloadClientTypeName.rTorrent) {
      return 'Username for HTTP Basic Auth';
    }
    return 'Username for authentication';
  });

  readonly passwordHint = computed(() => {
    if (this.clientModel().typeName === DownloadClientTypeName.rTorrent) {
      return 'Password for HTTP Basic Auth';
    }
    return 'Password for authentication';
  });

  readonly urlBaseHint = computed(() => {
    if (this.clientModel().typeName === DownloadClientTypeName.rTorrent) {
      return 'Path to the XMLRPC endpoint. Usually RPC2 for rTorrent or plugins/httprpc/action.php for ruTorrent.';
    }
    if (this.isUsenet()) return 'Reverse-proxy path only, such as /nzbget. The /jsonrpc endpoint is added automatically.';
    return 'Optional URL base path, leave blank for default';
  });

  // typeName is owned by [formField]; here we only apply type-specific defaults,
  // guarded so they never clobber values already loaded when editing a client.
  onClientTypeChange(value: unknown): void {
    const newType = value as DownloadClientTypeName;
    const m = this.clientModel();
    const patch: Partial<DownloadClientFormModel> = {};
    if (newType === DownloadClientTypeName.Deluge && m.username !== '') {
      patch.username = '';
    }
    const autofill = AUTOFILL_URL_BASES[newType];
    const replaceable = m.urlBase === '' || Object.values(AUTOFILL_URL_BASES).includes(m.urlBase);
    if (replaceable && (autofill ?? '') !== m.urlBase) {
      patch.urlBase = autofill ?? '';
    }
    if (Object.keys(patch).length > 0) {
      this.clientModel.update((mm) => ({ ...mm, ...patch }));
    }
  }

  retry(): void {
    this.settings.retry();
  }

  openAddModal(): void {
    this.editingClient.set(null);
    this.clientModel.set({
      ...DEFAULT_USENET_OPTIONS, enabled: true, name: '', typeName: DownloadClientTypeName.qBittorrent,
      host: '', username: '', password: '', urlBase: '', externalUrl: '',
      downloadDirectorySource: '', downloadDirectoryTarget: '',
    });
    this.openSnapshot.set(JSON.stringify(this.clientModel()));
    this.modalVisible.set(true);
  }

  openEditModal(client: ClientConfig): void {
    this.editingClient.set(client);
    this.clientModel.set({
      ...DEFAULT_USENET_OPTIONS, ...client.usenetOptions,
      enabled: client.enabled,
      name: client.name,
      typeName: client.typeName,
      host: client.host,
      username: client.username,
      password: client.password ?? '',
      urlBase: client.urlBase,
      externalUrl: client.externalUrl ?? '',
      downloadDirectorySource: client.downloadDirectorySource ?? '',
      downloadDirectoryTarget: client.downloadDirectoryTarget ?? '',
    });
    this.openSnapshot.set(JSON.stringify(this.clientModel()));
    this.modalVisible.set(true);
  }

  testConnection(): void {
    const m = this.clientModel();
    const request: TestDownloadClientRequest = {
      typeName: m.typeName,
      type: m.typeName === DownloadClientTypeName.NZBGet ? DownloadClientType.Usenet : DownloadClientType.Torrent,
      host: m.host,
      username: m.username,
      password: m.password,
      urlBase: m.urlBase,
      clientId: this.editingClient()?.id,
    };
    this.testing.set(true);
    this.api.test(request).subscribe({
      next: (result) => {
        this.toast.success(result.message || 'Connection successful');
        this.testing.set(false);
      },
      error: (err: ApiError) => {
        this.toast.error(err.message);
        this.testing.set(false);
      },
    });
  }

  saveClient(): void {
    if (this.clientForm().invalid()) {
      return;
    }
    const editing = this.editingClient();
    const m = this.clientModel();
    this.saving.set(true);

    if (editing) {
      const client: ClientConfig = {
        ...editing,
        enabled: m.enabled,
        name: m.name,
        type: m.typeName === DownloadClientTypeName.NZBGet ? DownloadClientType.Usenet : DownloadClientType.Torrent,
        ...(m.typeName === DownloadClientTypeName.NZBGet ? { usenetOptions: this.usenetOptions(m) } : {}),
        typeName: m.typeName,
        host: m.host,
        username: m.username,
        password: m.password || undefined,
        urlBase: m.urlBase,
        externalUrl: m.externalUrl || undefined,
        downloadDirectorySource: m.downloadDirectorySource || null,
        downloadDirectoryTarget: m.downloadDirectoryTarget || null,
      };
      this.api.update(editing.id, client).subscribe({
        next: () => {
          this.toast.success('Client updated');
          this.modalVisible.set(false);
          this.saving.set(false);
          this.clientsResource.reload();
        },
        error: (err: ApiError) => {
          this.toast.error(err.message);
          this.saving.set(false);
        },
      });
    } else {
      const dto: CreateDownloadClientDto = {
        enabled: m.enabled,
        name: m.name,
        type: m.typeName === DownloadClientTypeName.NZBGet ? DownloadClientType.Usenet : DownloadClientType.Torrent,
        ...(m.typeName === DownloadClientTypeName.NZBGet ? { usenetOptions: this.usenetOptions(m) } : {}),
        typeName: m.typeName,
        host: m.host,
        username: m.username,
        password: m.password,
        urlBase: m.urlBase,
        externalUrl: m.externalUrl || undefined,
        downloadDirectorySource: m.downloadDirectorySource || null,
        downloadDirectoryTarget: m.downloadDirectoryTarget || null,
      };
      this.api.create(dto).subscribe({
        next: () => {
          this.toast.success('Client added');
          this.modalVisible.set(false);
          this.saving.set(false);
          this.clientsResource.reload();
        },
        error: (err: ApiError) => {
          this.toast.error(err.message);
          this.saving.set(false);
        },
      });
    }
  }

  async deleteClient(client: ClientConfig): Promise<void> {
    const confirmed = await this.confirmService.confirm({
      title: 'Delete Client',
      message: `Are you sure you want to delete "${client.name}"? This action cannot be undone.`,
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!confirmed) {
      return;
    }

    this.api.delete(client.id).subscribe({
      next: () => {
        this.toast.success('Client deleted');
        this.clientsResource.reload();
      },
      error: (err: ApiError) => this.toast.error(err.message),
    });
  }

  hasPendingChanges(): boolean {
    return this.modalDirty();
  }
}
