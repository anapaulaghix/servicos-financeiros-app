import { InjectionToken } from '@angular/core';

/**
 * Recebe o valor do erro de validação (ex.: `{ min: 0.01, actual: 0 }`) e devolve o texto exibido.
 * O valor chega como `unknown`: cada mensagem declara o formato que espera, sem `any`.
 */
export type ValidationMessageFn = (errorValue: unknown) => string;

const formatNumber = (value: number): string => new Intl.NumberFormat('pt-BR').format(value);

/** Tipa o valor do erro de um validador específico. */
const message =
  <T>(render: (errorValue: T) => string): ValidationMessageFn =>
  (errorValue) =>
    render(errorValue as T);

/**
 * Mensagem de cada validador, indexada pela chave do erro (`required`, `min`...).
 * O valor do erro chega como argumento, para mensagens que dependem do limite configurado.
 */
export const ERROR_MESSAGES: Readonly<Record<string, ValidationMessageFn>> = {
  required: () => 'Campo obrigatório.',
  min: message<{ min: number }>((error) => `O valor mínimo é ${formatNumber(error.min)}.`),
  max: message<{ max: number }>((error) => `O valor máximo é ${formatNumber(error.max)}.`),
  minlength: message<{ requiredLength: number }>(
    (error) => `Informe no mínimo ${error.requiredLength} caracteres.`,
  ),
  maxlength: message<{ requiredLength: number }>(
    (error) => `Informe no máximo ${error.requiredLength} caracteres.`,
  ),
  maxDecimals: message<{ max: number }>((error) => `Use no máximo ${error.max} casas decimais.`),
  uuid: () => 'Informe um identificador UUID válido.',
  // Erro vindo da API (ProblemDetails): a mensagem já é o próprio valor do erro.
  server: message<string>((serverMessage) => serverMessage),
};

export const VALIDATION_ERROR_MESSAGES = new InjectionToken<Readonly<Record<string, ValidationMessageFn>>>(
  'Validation Messages',
  { providedIn: 'root', factory: () => ERROR_MESSAGES },
);
