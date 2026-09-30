import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ApiError } from '../errors/api-error';
import { TransactionOutcome, TransactionRequest, TransactionResult } from '../models/account.model';
import { AccountsApi } from './accounts-api.service';
import { TransactionsApi } from './transactions-api.service';

describe('API services', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('AccountsApi', () => {
    it('lista contas em GET /api/accounts', () => {
      let result: unknown;
      TestBed.inject(AccountsApi)
        .list()
        .subscribe((accounts) => (result = accounts));

      const request = http.expectOne('/api/accounts');
      expect(request.request.method).toBe('GET');
      request.flush([{ id: 'a' }]);

      expect(result).toEqual([{ id: 'a' }]);
    });

    it('busca o extrato enviando page e pageSize como query params', () => {
      TestBed.inject(AccountsApi).statement('acc-1', 2, 5).subscribe();

      const request = http.expectOne((r) => r.url === '/api/accounts/acc-1/transactions');
      expect(request.request.params.get('page')).toBe('2');
      expect(request.request.params.get('pageSize')).toBe('5');
      request.flush({ items: [], page: 2, pageSize: 5, totalItems: 0, totalPages: 0 });
    });

    it('traduz erros HTTP para ApiError', () => {
      let error: ApiError | undefined;
      TestBed.inject(AccountsApi)
        .get('missing')
        .subscribe({ error: (e: ApiError) => (error = e) });

      http.expectOne('/api/accounts/missing').flush(null, { status: 404, statusText: 'Not Found' });

      expect(error).toBeInstanceOf(ApiError);
      expect(error?.kind).toBe('not-found');
    });
  });

  describe('TransactionsApi', () => {
    const payload: TransactionRequest = {
      eventId: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
      accountId: '7b895f64-5717-4562-b3fc-2c963f66afa7',
      type: 'CREDIT',
      amount: 150.75,
      occurredAt: '2026-01-30T10:15:00.000Z',
    };

    const created: TransactionResult = {
      ...payload,
      balanceAfter: 150.75,
      processedAt: '2026-01-30T10:15:01Z',
    };

    it('envia o evento em POST /api/transactions com o contrato da API', () => {
      let outcome: TransactionOutcome | undefined;
      TestBed.inject(TransactionsApi)
        .submit(payload)
        .subscribe((value) => (outcome = value));

      const request = http.expectOne('/api/transactions');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual(payload);
      request.flush(created, { status: 201, statusText: 'Created' });

      expect(outcome).toEqual({ transaction: created, replayed: false });
    });

    it('identifica o reenvio de um evento já processado pelo cabeçalho Idempotent-Replayed', () => {
      let outcome: TransactionOutcome | undefined;
      TestBed.inject(TransactionsApi)
        .submit(payload)
        .subscribe((value) => (outcome = value));

      http.expectOne('/api/transactions').flush(created, { headers: { 'Idempotent-Replayed': 'true' } });

      expect(outcome).toEqual({ transaction: created, replayed: true });
    });

    it('propaga eventId reutilizado com outros dados (409) como ApiError do tipo duplicate', () => {
      let error: ApiError | undefined;
      TestBed.inject(TransactionsApi)
        .submit(payload)
        .subscribe({ error: (e: ApiError) => (error = e) });

      http.expectOne('/api/transactions').flush({}, { status: 409, statusText: 'Conflict' });

      expect(error?.kind).toBe('duplicate');
    });
  });
});
