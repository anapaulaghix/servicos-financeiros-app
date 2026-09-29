import { InjectionToken } from '@angular/core';

export type ValidationMessageFn = (errorValue?: any) => string;

const formatNumber = (value: number): string => new Intl.NumberFormat('pt-BR').format(value);

/**
 * Mensagem de cada validador, indexada pela chave do erro (`required`, `min`...).
 * O valor do erro chega como argumento, para mensagens que dependem do limite configurado.
 */
export const ERROR_MESSAGES: Readonly<Record<string, ValidationMessageFn>> = {
  required: () => 'Campo obrigatório.',
  min: (error: { min: number }) => `O valor mínimo é ${formatNumber(error.min)}.`,
  max: (error: { max: number }) => `O valor máximo é ${formatNumber(error.max)}.`,
  minlength: (error: { requiredLength: number }) => `Informe no mínimo ${error.requiredLength} caracteres.`,
  maxlength: (error: { requiredLength: number }) => `Informe no máximo ${error.requiredLength} caracteres.`,
  maxDecimals: (error: { max: number }) => `Use no máximo ${error.max} casas decimais.`,
  uuid: () => 'Informe um identificador UUID válido.',
  // Erro vindo da API (ProblemDetails): a mensagem já é o próprio valor do erro.
  server: (message: string) => message,
};

export const VALIDATION_ERROR_MESSAGES = new InjectionToken<Readonly<Record<string, ValidationMessageFn>>>(
  'Validation Messages',
  { providedIn: 'root', factory: () => ERROR_MESSAGES },
);
