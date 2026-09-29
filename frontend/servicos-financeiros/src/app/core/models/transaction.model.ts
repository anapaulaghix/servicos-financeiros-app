import { ApiError, ApiErrorKind } from "../errors/api-error";
import { TransactionResult } from "./account.model";

export type Submission =
  | { status: 'idle' }
  | { status: 'processing' }
  | { status: 'success'; result: TransactionResult; holderName: string }
  | { status: 'error'; error: ApiError };

export const ERROR_TITLES: Record<ApiErrorKind, string> = {
  duplicate: 'Lançamento já registrado',
  'insufficient-funds': 'Saldo insuficiente',
  network: 'Sem conexão com o servidor',
  validation: 'Dados inválidos',
  'not-found': 'Conta não encontrada',
  server: 'Falha no servidor',
};
