import { ValidatorFn } from '@angular/forms';

import { isUuid } from '../../../core/utils/uuid';

export class CustomValidators {
  /** Limita as casas decimais (valores monetários: 2, como a API). Erro: `{ maxDecimals: { max } }`. */
  static maxDecimals(max: number): ValidatorFn {
    const pattern = new RegExp(`^-?\\d+(\\.\\d{1,${max}})?$`);

    return (control) => {
      const value = control.value as number | null | undefined;
      if (value === null || value === undefined || value === ('' as unknown)) {
        return null;
      }
      return pattern.test(String(value)) ? null : { maxDecimals: { max } };
    };
  }

  /** Aceita apenas UUID (formato do eventId). Erro: `{ uuid: true }`. */
  static uuid(): ValidatorFn {
    return (control) => {
      const value = String(control.value ?? '').trim();
      if (value === '') {
        return null; // campo vazio é responsabilidade do `required`
      }
      return isUuid(value) ? null : { uuid: true };
    };
  }
}
