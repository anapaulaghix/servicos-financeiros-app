import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { mapApiError } from '../errors/api-error';
import { Account, Page, StatementEntry } from '../models/account.model';
import { API_BASE } from './api.config';

@Injectable({ providedIn: 'root' })
export class AccountsApi {
  private readonly http = inject(HttpClient);

  list(): Observable<Account[]> {
    return this.http.get<Account[]>(`${API_BASE}/accounts`).pipe(mapApiError());
  }

  get(accountId: string): Observable<Account> {
    return this.http.get<Account>(`${API_BASE}/accounts/${accountId}`).pipe(mapApiError());
  }

  /** Extrato paginado. `page` começa em 1, como na API. */
  statement(accountId: string, page: number, pageSize: number): Observable<Page<StatementEntry>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);

    return this.http
      .get<Page<StatementEntry>>(`${API_BASE}/accounts/${accountId}/transactions`, { params })
      .pipe(mapApiError());
  }
}
