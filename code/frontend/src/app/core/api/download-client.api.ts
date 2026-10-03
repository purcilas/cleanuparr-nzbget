import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  DownloadClientConfig,
  ClientConfig,
  CreateDownloadClientDto,
  TestDownloadClientRequest,
  TestConnectionResult, UsenetStatus,
} from '@shared/models/download-client-config.model';

@Injectable({ providedIn: 'root' })
export class DownloadClientApi {
  private http = inject(HttpClient);

  getConfig(): Observable<DownloadClientConfig> {
    return this.http.get<DownloadClientConfig>('/api/configuration/download_client');
  }

  create(client: CreateDownloadClientDto): Observable<ClientConfig> {
    return this.http.post<ClientConfig>('/api/configuration/download_client', client);
  }

  update(id: string, client: ClientConfig): Observable<ClientConfig> {
    return this.http.put<ClientConfig>(`/api/configuration/download_client/${id}`, client);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/configuration/download_client/${id}`);
  }

  usenetStatus(id: string): Observable<UsenetStatus[]> {
    return this.http.get<UsenetStatus[]>(`/api/configuration/download_client/${id}/usenet-status`);
  }

  reviewUsenetRecovery(id: string): Observable<{message: string}> {
    return this.http.post<{message: string}>(`/api/configuration/download_client/${id}/usenet-review`, {acknowledgeReview: true});
  }

  test(request: TestDownloadClientRequest): Observable<TestConnectionResult> {
    return this.http.post<TestConnectionResult>('/api/configuration/download_client/test', request);
  }
}
