import { AsyncPipe, CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BehaviorSubject, switchMap } from 'rxjs';

import { AccountsApi } from '../../core/api/accounts-api.service';
import { Account } from '../../core/models/account.model';
import { toLoadState } from '../../core/state/load-state';
import { ErrorPanelComponent } from '../../shared/error-panel/error-panel.component';
import { LoadingComponent } from '../../shared/loading/loading.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-accounts-list',
  standalone: true,
  imports: [
    AsyncPipe,
    CurrencyPipe,
    DatePipe,
    RouterLink,
    PageHeaderComponent,
    LoadingComponent,
    ErrorPanelComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './accounts-list.component.html',
  styleUrl: './accounts-list.component.scss',
})
export class AccountsListComponent {
  private readonly api = inject(AccountsApi);
  private readonly reload$ = new BehaviorSubject<void>(undefined);

  /** Cada `reload` volta a emitir `loading` antes de buscar de novo. */
  protected readonly state$ = this.reload$.pipe(
    switchMap(() => this.api.list().pipe(toLoadState())),
  );

  protected reload(): void {
    this.reload$.next();
  }

  protected total(accounts: Account[]): number {
    const sum = accounts.reduce((acc, account) => acc + account.balance, 0);
    return Math.round(sum * 100) / 100;
  }
}
