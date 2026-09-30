import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';

import { ApiError, toApiError } from './api-error';

describe('toApiError', () => {
  const http = (status: number, error?: unknown) => new HttpErrorResponse({ status, error });

  it('classifica falha de comunicação (status 0) como network', () => {
    const error = toApiError(http(0));

    expect(error.kind).toBe('network');
    expect(error.message).toContain('comunicar com o servidor');
  });

  it('classifica 409 (eventId reutilizado com outros dados) como duplicate', () => {
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

  it('classifica 429 como limite de requisições e usa o Retry-After na mensagem', () => {
    const error = toApiError(
      new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '7' }) }),
    );

    expect(error.kind).toBe('rate-limited');
    expect(error.message).toContain('Aguarde 7 segundos');
    expect(error.message).toContain('nada foi lançado');
  });

  it('em 429 sem Retry-After, pede para aguardar alguns segundos', () => {
    expect(toApiError(http(429)).message).toContain('Aguarde alguns segundos');
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
