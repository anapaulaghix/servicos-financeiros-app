import { Pipe, PipeTransform, inject } from '@angular/core';

import { VALIDATION_ERROR_MESSAGES } from '../validator/validation-error-messages.token';

const FALLBACK_MESSAGE = 'Valor inválido.';

@Pipe({ name: 'errorMessage', standalone: true })
export class ErrorMessagePipe implements PipeTransform {
  private readonly errorMessages = inject(VALIDATION_ERROR_MESSAGES);

  transform(key: string, errorValue: unknown): string {
    const message = this.errorMessages[key];

    // Um validador sem mensagem cadastrada não pode quebrar a tela: avisa no console e usa um texto genérico.
    if (!message) {
      console.warn(`Missing message for ${key} validator`);
      return FALLBACK_MESSAGE;
    }

    return message(errorValue);
  }
}
