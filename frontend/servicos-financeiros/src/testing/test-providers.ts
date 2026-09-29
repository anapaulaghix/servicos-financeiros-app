import { registerLocaleData } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import localePt from '@angular/common/locales/pt';
import { EnvironmentProviders, LOCALE_ID, Provider } from '@angular/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';

import { Account, StatementEntry } from '../app/core/models/account.model';

registerLocaleData(localePt);

/** Providers comuns dos testes de componente: HTTP simulado, rotas vazias, pt-BR e sem animações. */
export function provideTestEnvironment(): (Provider | EnvironmentProviders)[] {
  return [
    provideHttpClient(),
    provideHttpClientTesting(),
    provideRouter([]),
    provideNoopAnimations(),
    { provide: LOCALE_ID, useValue: 'pt-BR' },
  ];
}

export const ACCOUNTS: Account[] = [
  { id: 'acc-1', holderName: 'Ana Paula Ghis', balance: 1500, createdAt: '2026-01-01T10:00:00Z' },
  { id: 'acc-2', holderName: 'Carlos Lima', balance: 320.5, createdAt: '2026-01-02T10:00:00Z' },
];

export function entry(partial: Partial<StatementEntry> & Pick<StatementEntry, 'eventId'>): StatementEntry {
  return {
    type: 'CREDIT',
    amount: 100,
    signedAmount: 100,
    balanceAfter: 100,
    occurredAt: '2026-01-30T10:15:00Z',
    processedAt: '2026-01-30T10:15:01Z',
    ...partial,
  };
}

/** Texto visível do elemento, com espaços (inclusive o não separável do BRL) normalizados. */
export function textOf(element: HTMLElement): string {
  return (element.textContent ?? '').replace(/\s+/g, ' ').trim();
}
