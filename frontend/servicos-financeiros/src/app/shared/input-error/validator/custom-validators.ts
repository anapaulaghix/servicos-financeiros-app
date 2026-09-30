import { ValidatorFn } from '@angular/forms';

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
}
