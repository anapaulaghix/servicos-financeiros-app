import { ApiError } from '../errors/api-error';
import { Page, StatementEntry } from './account.model';

export interface Paging {
  /** Conta em que a página foi escolhida; em outra conta, o extrato volta à primeira página. */
  accountId?: string;
  pageIndex: number;
  pageSize: number;
}

export interface StatementView {
  /** Conta a que `data` pertence: dados de outra conta nunca ficam na tela enquanto a nova carrega. */
  accountId?: string;
  data?: Page<StatementEntry>;
  loading: boolean;
  error?: ApiError;
}
