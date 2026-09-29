import { LOCALE_ID, Pipe, PipeTransform, inject } from '@angular/core';
import { formatCurrency } from '@angular/common';

/** Valor em reais com sinal explícito (+ crédito, − débito), para evidenciar o impacto no saldo. */
@Pipe({ name: 'signedMoney', standalone: true })
export class SignedMoneyPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(value: number): string {
    const formatted = formatCurrency(Math.abs(value), this.locale, 'R$', 'BRL', '1.2-2');
    return `${value < 0 ? '−' : '+'} ${formatted}`;
  }
}
