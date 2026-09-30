import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';

import { ApiError } from '../../core/errors/api-error';

@Component({
  selector: 'app-error-panel',
  standalone: true,
  imports: [MessageModule, ButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-message severity="error" class="panel" role="alert">
      <div class="panel__content">
        <p class="panel__title">{{ title() }}</p>
        <p>{{ error().message }}</p>
        @if (retryable()) {
          <p-button
            label="Tentar novamente"
            severity="secondary"
            [outlined]="true"
            (onClick)="retry.emit()"
          />
        }
      </div>
    </p-message>
  `,
  styles: `
    :host {
      display: block;
      margin-top: 32px;
    }

    .panel__content {
      display: grid;
      gap: 8px;
      justify-items: start;
    }

    .panel__title {
      font-size: 18px;
      font-weight: 800;
    }
  `,
})
export class ErrorPanelComponent {
  readonly error = input.required<ApiError>();
  readonly title = input('Não foi possível carregar');
  readonly retryable = input(true);
  readonly retry = output<void>();
}
