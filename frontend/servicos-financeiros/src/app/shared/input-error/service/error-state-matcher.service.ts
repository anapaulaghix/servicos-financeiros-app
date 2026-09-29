import { Injectable } from '@angular/core';
import { AbstractControl, FormGroupDirective, NgForm } from '@angular/forms';

/** Decide quando o erro de um campo deve aparecer na tela. */
export interface ErrorStateMatcher {
  isErrorVisible(control: AbstractControl | null, form: NgForm | FormGroupDirective | null): boolean;
}

/** Padrão: mostra depois que o usuário altera o campo ou tenta enviar o formulário. */
@Injectable({ providedIn: 'root' })
export class ErrorStateMatcherService implements ErrorStateMatcher {
  isErrorVisible(control: AbstractControl | null, form: NgForm | FormGroupDirective | null): boolean {
    return Boolean(control && control.invalid && (control.dirty || form?.submitted));
  }
}

/**
 * Alternativa: mostra também quando o usuário sai do campo sem preenchê-lo.
 * Uso: `providers: [{ provide: ErrorStateMatcherService, useClass: OnTouchedErrorStateMatcherService }]`.
 */
@Injectable()
export class OnTouchedErrorStateMatcherService implements ErrorStateMatcher {
  isErrorVisible(control: AbstractControl | null, form: NgForm | FormGroupDirective | null): boolean {
    return Boolean(control && control.invalid && (control.dirty || control.touched || form?.submitted));
  }
}
