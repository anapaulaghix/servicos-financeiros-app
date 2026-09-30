import { OperatorFunction, catchError, map, of, startWith } from 'rxjs';

import { ApiError, toApiError } from '../errors/api-error';

/** Os três estados de qualquer carga de dados exibida na tela. */
export type LoadState<T> =
  { status: 'loading' } | { status: 'ready'; data: T } | { status: 'error'; error: ApiError };

/**
 * Converte um Observable de dados em um fluxo de estados de tela. Usado dentro de um `switchMap`,
 * cada nova chamada volta a emitir `loading` primeiro, e uma falha vira estado em vez de quebrar o fluxo.
 */
export function toLoadState<T>(): OperatorFunction<T, LoadState<T>> {
  return (source) =>
    source.pipe(
      map((data): LoadState<T> => ({ status: 'ready', data })),
      catchError((error) => of<LoadState<T>>({ status: 'error', error: toApiError(error) })),
      startWith<LoadState<T>>({ status: 'loading' }),
    );
}
