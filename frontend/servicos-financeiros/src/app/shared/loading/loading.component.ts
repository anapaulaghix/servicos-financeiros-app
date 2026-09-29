import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ProgressBarModule } from 'primeng/progressbar';

@Component({
  selector: 'app-loading',
  standalone: true,
  imports: [ProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="loading" role="status" aria-live="polite">
      <p-progressbar mode="indeterminate" [showValue]="false" [attr.aria-label]="label()" />
      <p class="eyebrow muted">{{ label() }}</p>
    </div>
  `,
  styles: `
    .loading {
      display: grid;
      gap: 16px;
      padding: 48px 0;
    }
  `,
})
export class LoadingComponent {
  readonly label = input('Carregando');
}
