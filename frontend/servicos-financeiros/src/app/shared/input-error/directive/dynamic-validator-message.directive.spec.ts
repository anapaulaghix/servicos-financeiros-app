import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormBuilder, FormGroupDirective, ReactiveFormsModule, Validators } from '@angular/forms';

import {
  ErrorStateMatcherService,
  OnTouchedErrorStateMatcherService,
} from '../service/error-state-matcher.service';
import { CustomValidators } from '../validator/custom-validators';
import { DynamicValidatorMessageDirective } from './dynamic-validator-message.directive';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, DynamicValidatorMessageDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form">
      <input id="name" formControlName="name" />
      <input id="amount" type="number" formControlName="amount" />
      <input id="free" formControlName="free" withoutFormValidation />
    </form>
  `,
})
class FormHost {
  readonly formDirective = viewChild.required(FormGroupDirective);
  readonly form = inject(FormBuilder).group({
    name: ['', Validators.required],
    amount: [null as number | null, [Validators.min(0.01), CustomValidators.maxDecimals(2)]],
    free: ['', Validators.required],
  });
}

describe('DynamicValidatorMessageDirective', () => {
  let fixture: ComponentFixture<FormHost>;
  let host: FormHost;
  const root = (): HTMLElement => fixture.nativeElement;
  const errorsText = (): string => root().querySelector('form')!.textContent!.replace(/\s+/g, ' ').trim();
  const input = (id: string): HTMLInputElement => root().querySelector(`#${id}`)!;
  const submitForm = (): void => {
    root().querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  };

  const create = (): void => {
    fixture = TestBed.createComponent(FormHost);
    host = fixture.componentInstance;
    fixture.detectChanges();
  };

  describe('com o critério padrão (alterado ou enviado)', () => {
    beforeEach(create);

    it('não mostra erro em um formulário recém-aberto', () => {
      expect(root().querySelector('app-input-error')).toBeNull();
    });

    it('mostra o erro logo abaixo do campo quando o usuário o altera', () => {
      host.form.controls.amount.setValue(0);
      host.form.controls.amount.markAsDirty();
      fixture.detectChanges();

      expect(input('amount').nextElementSibling?.tagName).toBe('APP-INPUT-ERROR');
      expect(errorsText()).toContain('O valor mínimo é 0,01.');
      expect(input('amount').classList).toContain('field-invalid');
    });

    it('troca a mensagem conforme o erro muda e some quando o campo fica válido', () => {
      host.form.controls.amount.markAsDirty();
      host.form.controls.amount.setValue(1.999);
      fixture.detectChanges();
      expect(errorsText()).toContain('Use no máximo 2 casas decimais.');

      host.form.controls.amount.setValue(1.99);
      fixture.detectChanges();

      expect(root().querySelector('#amount + app-input-error')).toBeNull();
      expect(input('amount').classList).toContain('field-valid');
      expect(input('amount').classList).not.toContain('field-invalid');
    });

    it('mostra todos os campos inválidos ao tentar enviar o formulário', () => {
      submitForm();

      expect(errorsText()).toContain('Campo obrigatório.');
      expect(input('name').classList).toContain('field-invalid');
    });

    it('ignora campos marcados com withoutFormValidation', () => {
      submitForm();

      expect(input('free').nextElementSibling).toBeNull();
      expect(input('free').classList).not.toContain('field-invalid');
    });

    it('limpa erros e classes quando o formulário é reiniciado', () => {
      submitForm();
      host.formDirective().resetForm();
      fixture.detectChanges();

      expect(root().querySelector('app-input-error')).toBeNull();
      expect(input('name').classList).not.toContain('field-invalid');
    });

    it('mostra erros vindos do servidor (setErrors) com a mensagem recebida', () => {
      host.form.controls.name.setValue('Ana');
      host.form.controls.name.markAsDirty();
      host.form.controls.name.setErrors({ server: 'Nome já cadastrado.' });
      fixture.detectChanges();

      expect(errorsText()).toContain('Nome já cadastrado.');
    });
  });

  describe('com OnTouchedErrorStateMatcherService', () => {
    beforeEach(() => {
      TestBed.configureTestingModule({
        providers: [{ provide: ErrorStateMatcherService, useClass: OnTouchedErrorStateMatcherService }],
      });
      create();
    });

    it('mostra o erro quando o usuário sai do campo sem preencher', () => {
      input('name').dispatchEvent(new Event('blur'));
      fixture.detectChanges();

      expect(errorsText()).toContain('Campo obrigatório.');
    });
  });
});
