import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';

import { toApiError } from '../errors/api-error';
import { TransactionRequest, TransactionResult } from '../models/account.model';
import { API_BASE } from './api.config';

@Injectable({ providedIn: 'root' })
export class TransactionsApi {
  private readonly http = inject(HttpClient);

  submit(request: TransactionRequest): Observable<TransactionResult> {
    return this.http
      .post<TransactionResult>(`${API_BASE}/transactions`, request)
      .pipe(catchError((error) => throwError(() => toApiError(error))));
  }
}
