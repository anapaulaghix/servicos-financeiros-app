import { HttpErrorResponse } from '@angular/common/http';

export type ApiErrorKind =
  'network' | 'validation' | 'duplicate' | 'insufficient-funds' | 'not-found' | 'rate-limited' | 'server';

/**
 * Erro da API já traduzido para a linguagem da interface. Os componentes tratam `kind`,
 * sem precisar conhecer códigos HTTP nem o formato do ProblemDetails.
 */
export class ApiError extends Error {
  constructor(
    readonly kind: ApiErrorKind,
    override readonly message: string,
    readonly status: number,
    /** Erros por campo (chaves em camelCase), quando o servidor os informa. */
    readonly fieldErrors: Readonly<Record<string, string[]>> = {},
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

interface ProblemDetailsBody {
  detail?: string;
  errors?: Record<string, string[]>;
}

const NETWORK_MESSAGE =
  'Não foi possível se comunicar com o servidor. Verifique sua conexão e tente novamente.';

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }

  if (!(error instanceof HttpErrorResponse)) {
    return new ApiError('server', 'Ocorreu um erro inesperado. Tente novamente.', 0);
  }

  if (error.status === 0) {
    return new ApiError('network', NETWORK_MESSAGE, 0);
  }

  const body = (error.error ?? {}) as ProblemDetailsBody;

  switch (error.status) {
    case 400:
      return new ApiError(
        'validation',
        body.detail ?? 'Alguns campos são inválidos. Revise o formulário e tente novamente.',
        400,
        normalizeFieldErrors(body.errors),
      );
    case 404:
      return new ApiError('not-found', 'Conta não encontrada.', 404);
    case 409:
      return new ApiError(
        'duplicate',
        'Este lançamento já havia sido processado. Nenhum valor foi lançado novamente.',
        409,
      );
    case 422:
      return new ApiError(
        'insufficient-funds',
        'Saldo insuficiente para este débito. O lançamento foi recusado.',
        422,
      );
    case 429:
      return new ApiError('rate-limited', rateLimitedMessage(error), 429);
    default:
      return new ApiError(
        'server',
        'O servidor encontrou um problema ao processar a solicitação. Tente novamente em instantes.',
        error.status,
      );
  }
}

/** Usa o Retry-After (em segundos) enviado pela API para dizer quanto tempo esperar. */
function rateLimitedMessage(error: HttpErrorResponse): string {
  const seconds = Number(error.headers?.get('Retry-After'));
  const wait =
    Number.isFinite(seconds) && seconds > 0
      ? `Aguarde ${seconds} ${seconds === 1 ? 'segundo' : 'segundos'}`
      : 'Aguarde alguns segundos';

  return `Muitos lançamentos em pouco tempo. ${wait} e tente novamente; nada foi lançado.`;
}

function normalizeFieldErrors(errors: Record<string, string[]> | undefined): Record<string, string[]> {
  const result: Record<string, string[]> = {};

  for (const [key, messages] of Object.entries(errors ?? {})) {
    const field = key.replace(/^\$\./, '');
    const camelCased = field.charAt(0).toLowerCase() + field.slice(1);
    result[camelCased] = messages;
  }

  return result;
}
