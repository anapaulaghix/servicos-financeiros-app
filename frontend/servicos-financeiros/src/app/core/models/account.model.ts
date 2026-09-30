export type TransactionType = 'CREDIT' | 'DEBIT';

export interface Account {
  id: string;
  holderName: string;
  balance: number;
  createdAt: string;
}

export interface StatementEntry {
  eventId: string;
  type: TransactionType;
  amount: number;
  signedAmount: number;
  balanceAfter: number;
  occurredAt: string;
  processedAt: string;
}

export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

export interface TransactionRequest {
  eventId: string;
  accountId: string;
  type: TransactionType;
  amount: number;
  occurredAt: string;
}

/** Resultado do POST /api/transactions. */
export interface TransactionOutcome {
  transaction: TransactionResult;
  /**
   * O evento já tinha sido processado (reenvio com os mesmos dados): a API devolveu o lançamento
   * original e nada foi lançado de novo.
   */
  replayed: boolean;
}

export interface TransactionResult {
  eventId: string;
  accountId: string;
  type: TransactionType;
  amount: number;
  balanceAfter: number;
  occurredAt: string;
  processedAt: string;
}
