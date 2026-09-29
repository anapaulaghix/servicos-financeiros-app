import { ApiError } from '../errors/api-error';
import { Page, StatementEntry } from './account.model';

export interface Paging {
  pageIndex: number;
  pageSize: number;
}

export interface StatementView {
  data?: Page<StatementEntry>;
  loading: boolean;
  error?: ApiError;
}
