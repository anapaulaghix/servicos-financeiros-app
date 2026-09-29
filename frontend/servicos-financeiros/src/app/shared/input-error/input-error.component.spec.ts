import { Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { InputErrorComponent } from './input-error.component';
import { VALIDATION_ERROR_MESSAGES } from './validator/validation-error-messages.token';

@Component({
  standalone: true,
  imports: [InputErrorComponent, ReactiveFormsModule],
  template: `<app-input-error [errors]="form.controls.name.errors" />`,
})
class InputErrorTestHost {
  readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(4)]],
  });
}

describe('InputErrorComponent', () => {
  let fixture: ComponentFixture<InputErrorTestHost>;
  const messages = (): string[] =>
    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('[data-testingId="input-error"]')).map(
      (element) => element.textContent!.trim(),
    );

  beforeEach(() => {
    fixture = TestBed.createComponent(InputErrorTestHost);
    fixture.detectChanges();
  });

  it('mostra a mensagem do validador required quando o campo está vazio', () => {
    expect(messages()).toEqual(['Campo obrigatório.']);
  });

  it('usa o valor do erro para montar a mensagem (minlength)', () => {
    fixture.componentInstance.form.controls.name.setValue('Tes');
    fixture.detectChanges();

    expect(messages()).toEqual(['Informe no mínimo 4 caracteres.']);
  });

  it('não mostra nada quando o campo é válido', () => {
    fixture.componentInstance.form.controls.name.setValue('Valor válido');
    fixture.detectChanges();

    expect(messages()).toEqual([]);
  });

  it('usa a classe do tema para a cor do erro', () => {
    const error = (fixture.nativeElement as HTMLElement).querySelector('[data-testingId="input-error"]');

    expect(error?.className).toContain('input-error');
  });
});

describe('ErrorMessagePipe (via InputErrorComponent)', () => {
  it('permite trocar as mensagens pelo token VALIDATION_ERROR_MESSAGES', () => {
    TestBed.configureTestingModule({
      providers: [{ provide: VALIDATION_ERROR_MESSAGES, useValue: { required: () => 'Preencha.' } }],
    });
    const fixture = TestBed.createComponent(InputErrorTestHost);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Preencha.');
  });

  it('usa uma mensagem genérica quando o validador não tem mensagem cadastrada', () => {
    spyOn(console, 'warn');
    TestBed.configureTestingModule({
      providers: [{ provide: VALIDATION_ERROR_MESSAGES, useValue: {} }],
    });
    const fixture = TestBed.createComponent(InputErrorTestHost);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Valor inválido.');
    expect(console.warn).toHaveBeenCalledWith('Missing message for required validator');
  });
});
