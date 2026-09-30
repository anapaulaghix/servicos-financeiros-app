import {
  AfterViewInit,
  ComponentRef,
  DestroyRef,
  Directive,
  ElementRef,
  Renderer2,
  ViewContainerRef,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  ControlContainer,
  FormGroupDirective,
  FormResetEvent,
  FormSubmittedEvent,
  NgControl,
  NgForm,
  NgModel,
} from '@angular/forms';
import { EMPTY, filter, merge, skip, startWith } from 'rxjs';

import { InputErrorComponent } from '../input-error.component';
import { ErrorStateMatcherService } from '../service/error-state-matcher.service';

const INVALID_CLASS = 'field-invalid';
const VALID_CLASS = 'field-valid';

/**
 * Exibe automaticamente as mensagens de validação de qualquer campo de formulário: basta importar
 * a diretiva no componente, sem escrever nenhum markup de erro no template. Para desligar em um
 * campo específico, use o atributo `withoutFormValidation`.
 */
@Directive({
  // Exceção intencional ao prefixo "app": a diretiva precisa se anexar sozinha aos controles de
  // formulário do Angular, que é justamente o que dispensa markup de erro nos templates.
  // eslint-disable-next-line @angular-eslint/directive-selector
  selector: `
    [ngModel]:not([withoutFormValidation]),
    [formControl]:not([withoutFormValidation]),
    [formControlName]:not([withoutFormValidation]),
    [formGroupName]:not([withoutFormValidation]),
    [ngModelGroup]:not([withoutFormValidation])
  `,
  standalone: true,
})
export class DynamicValidatorMessageDirective implements AfterViewInit {
  private readonly ngControl =
    inject(NgControl, { self: true, optional: true }) ?? inject(ControlContainer, { self: true });
  private readonly parentContainer = inject(ControlContainer, { optional: true });
  private readonly errorStateMatcher = inject(ErrorStateMatcherService);
  private readonly elementRef = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly renderer = inject(Renderer2);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly destroyRef = inject(DestroyRef);

  private componentRef: ComponentRef<InputErrorComponent> | null = null;

  private get form(): NgForm | FormGroupDirective | null {
    return (this.parentContainer?.formDirective as NgForm | FormGroupDirective | undefined) ?? null;
  }

  /**
   * O FormControlName só associa o controle no próprio ngOnChanges. No AfterViewInit ele já
   * existe, sem depender da ordem das diretivas no elemento nem de microtasks.
   */
  ngAfterViewInit(): void {
    const control = this.ngControl.control;
    if (!control) {
      throw new Error(`No control model for ${this.ngControl.name} control`);
    }

    // `events` cobre valor, status, touched e pristine (inclusive markAllAsTouched e setErrors).
    // Do formulário raiz só interessam envio e reset, que mudam o estado `submitted`.
    const formLifecycle$ = this.form
      ? this.form.form.events.pipe(
          filter((event) => event instanceof FormSubmittedEvent || event instanceof FormResetEvent),
        )
      : EMPTY;

    merge(control.events, formLifecycle$)
      .pipe(
        startWith(null),
        skip(this.ngControl instanceof NgModel ? 1 : 0),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.render());

    this.destroyRef.onDestroy(() => this.hideErrors());
  }

  private render(): void {
    const control = this.ngControl.control;
    if (!control) {
      return;
    }

    if (control.pristine) {
      this.removeAllClasses();
    }

    if (this.errorStateMatcher.isErrorVisible(control, this.form) && control.errors) {
      this.showErrors();
      this.setClass(INVALID_CLASS, VALID_CLASS);
    } else {
      this.hideErrors();
      if (control.errors === null && control.dirty) {
        this.setClass(VALID_CLASS, INVALID_CLASS);
      }
    }
  }

  private showErrors(): void {
    this.componentRef ??= this.viewContainerRef.createComponent(InputErrorComponent);
    this.componentRef.setInput('errors', this.ngControl.errors);
    // Renderiza já: o evento que disparou a mudança pode não passar pelo ciclo de um componente OnPush.
    this.componentRef.changeDetectorRef.detectChanges();
  }

  private hideErrors(): void {
    this.componentRef?.destroy();
    this.componentRef = null;
  }

  private setClass(add: string, remove: string): void {
    this.renderer.removeClass(this.elementRef.nativeElement, remove);
    this.renderer.addClass(this.elementRef.nativeElement, add);
  }

  private removeAllClasses(): void {
    this.renderer.removeClass(this.elementRef.nativeElement, INVALID_CLASS);
    this.renderer.removeClass(this.elementRef.nativeElement, VALID_CLASS);
  }
}
