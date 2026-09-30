import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { mapApiError } from '../errors/api-error';
import { TransactionOutcome, TransactionRequest, TransactionResult } from '../models/account.model';
import { API_BASE } from './api.config';

/** Enviado pela API (com status 200) quando devolve o lançamento original de um evento reenviado. */
export const IDEMPOTENT_REPLAYED_HEADER = 'Idempotent-Replayed';

@Injectable({ providedIn: 'root' })
export class TransactionsApi {
  private readonly http = inject(HttpClient);

  submit(request: TransactionRequest): Observable<TransactionOutcome> {
    return this.http
      .post<TransactionResult>(`${API_BASE}/transactions`, request, { observe: 'response' })
      .pipe(
        map((response) => ({
          transaction: response.body as TransactionResult,
          replayed: response.headers.get(IDEMPOTENT_REPLAYED_HEADER) === 'true',
        })),
        mapApiError(),
      );
  }
}
