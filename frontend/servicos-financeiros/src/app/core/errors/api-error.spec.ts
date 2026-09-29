import { HttpErrorResponse } from '@angular/common/http';

import { ApiError, toApiError } from './api-error';

describe('toApiError', () => {
  const http = (status: number, error?: unknown) => new HttpErrorResponse({ status, error });

  it('classifica falha de comunicação (status 0) como network', () => {
    const error = toApiError(http(0));

    expect(error.kind).toBe('network');
    expect(error.message).toContain('comunicar com o servidor');
  });

  it('classifica 409 como evento duplicado', () => {
    expect(toApiError(http(409)).kind).toBe('duplicate');
  });

  it('classifica 422 como saldo insuficiente', () => {
    expect(toApiError(http(422)).kind).toBe('insufficient-funds');
  });

  it('classifica 404 como conta não encontrada', () => {
    const error = toApiError(http(404));

    expect(error.kind).toBe('not-found');
    expect(error.message).toBe('Conta não encontrada.');
  });

  it('classifica 5xx como erro de servidor', () => {
    const error = toApiError(http(503));

    expect(error.kind).toBe('server');
    expect(error.status).toBe(503);
  });

  it('em 400 usa o detalhe do ProblemDetails quando existir', () => {
    const error = toApiError(http(400, { detail: 'O valor da transação deve ser maior que zero.' }));

    expect(error.kind).toBe('validation');
    expect(error.message).toBe('O valor da transação deve ser maior que zero.');
  });

  it('em 400 converte as chaves dos erros por campo para camelCase e remove o prefixo "$."', () => {
    const error = toApiError(
      http(400, { errors: { Amount: ['inválido'], '$.type': ['tipo desconhecido'] } }),
    );

    expect(error.fieldErrors['amount']).toEqual(['inválido']);
    expect(error.fieldErrors['type']).toEqual(['tipo desconhecido']);
  });

  it('não reembrulha um ApiError que já foi traduzido', () => {
    const original = new ApiError('duplicate', 'x', 409);

    expect(toApiError(original)).toBe(original);
  });

  it('trata valores desconhecidos como erro de servidor', () => {
    expect(toApiError(new Error('boom')).kind).toBe('server');
  });
});
