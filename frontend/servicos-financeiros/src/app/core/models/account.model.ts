export type TransactionType = 'CREDIT' | 'DEBIT';

export interface Account {
  id: string;
  holderName: string;
  balance: number;
  createdAt: string;
}

/** Linha do extrato: o lançamento e o efeito dele no saldo. */
export interface StatementEntry {
  eventId: string;
  type: TransactionType;
  amount: number;
  /** Positivo para crédito, negativo para débito. */
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

/** Evento financeiro enviado à API. */
export interface TransactionRequest {
  eventId: string;
  accountId: string;
  type: TransactionType;
  amount: number;
  occurredAt: string;
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
