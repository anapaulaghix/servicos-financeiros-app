import { ApiError, ApiErrorKind } from '../errors/api-error';
import { TransactionResult } from './account.model';

export type Submission =
  | { status: 'idle' }
  | { status: 'processing' }
  | { status: 'success'; result: TransactionResult; holderName: string; replayed: boolean }
  | { status: 'error'; error: ApiError };

export const ERROR_TITLES: Record<ApiErrorKind, string> = {
  duplicate: 'Identificador já utilizado',
  'insufficient-funds': 'Saldo insuficiente',
  network: 'Sem conexão com o servidor',
  validation: 'Dados inválidos',
  'not-found': 'Conta não encontrada',
  'rate-limited': 'Aguarde um instante',
  server: 'Falha no servidor',
};
