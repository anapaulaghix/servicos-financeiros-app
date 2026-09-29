import { KeyValuePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ValidationErrors } from '@angular/forms';

import { ErrorMessagePipe } from './pipe/error-message.pipe';

@Component({
  selector: 'app-input-error',
  standalone: true,
  imports: [KeyValuePipe, ErrorMessagePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-live': 'polite' },
  template: `
    @for (error of errors() | keyvalue; track error.key) {
      <p data-testingId="input-error" class="input-error">
        {{ error.key | errorMessage: error.value }}
      </p>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .input-error {
      margin: 0;
      color: var(--accent-ink);
      font-size: 13px;
      font-weight: 500;
      animation: input-error-in 120ms ease-out;
    }

    @keyframes input-error-in {
      from {
        opacity: 0;
        transform: translateY(-4px);
      }
    }
  `,
})
export class InputErrorComponent {
  readonly errors = input.required<ValidationErrors | null>();
}
