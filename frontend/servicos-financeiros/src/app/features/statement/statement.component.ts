import { AsyncPipe, CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TableModule, TablePageEvent } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { BehaviorSubject, combineLatest, scan, switchMap } from 'rxjs';
import { AccountsApi } from '../../core/api/accounts-api.service';
import { toLoadState } from '../../core/state/load-state';
import { ErrorPanelComponent } from '../../shared/error-panel/error-panel.component';
import { LoadingComponent } from '../../shared/loading/loading.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { SignedMoneyPipe } from '../../shared/pipes/signed-money.pipe';
import { Paging, StatementView } from '../../core/models/statement.models';

@Component({
  selector: 'app-statement',
  standalone: true,
  imports: [
    AsyncPipe,
    CurrencyPipe,
    DatePipe,
    RouterLink,
    TableModule,
    TagModule,
    PageHeaderComponent,
    LoadingComponent,
    ErrorPanelComponent,
    SignedMoneyPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './statement.component.html',
  styleUrl: './statement.component.scss',
})
export class StatementComponent {
  private readonly api = inject(AccountsApi);
  readonly id = input.required<string>();

  protected readonly pageSizeOptions = [5, 10, 25, 50];

  private readonly id$ = toObservable(this.id);
  private readonly paging$ = new BehaviorSubject<Paging>({ pageIndex: 0, pageSize: 10 });
  private readonly reload$ = new BehaviorSubject<void>(undefined);

  protected readonly account$ = combineLatest([this.id$, this.reload$]).pipe(
    switchMap(([id]) => this.api.get(id).pipe(toLoadState())),
  );

  protected readonly view$ = combineLatest([this.id$, this.paging$, this.reload$]).pipe(
    switchMap(([id, { pageIndex, pageSize }]) =>
      this.api.statement(id, pageIndex + 1, pageSize).pipe(toLoadState()),
    ),
    scan(
      (previous: StatementView, state): StatementView => {
        switch (state.status) {
          case 'loading':
            return { data: previous.data, loading: true };
          case 'ready':
            return { data: state.data, loading: false };
          case 'error':
            return { data: previous.data, loading: false, error: state.error };
        }
      },
      { loading: true },
    ),
  );

  protected onPage(event: TablePageEvent): void {
    this.paging$.next({ pageIndex: Math.floor(event.first / event.rows), pageSize: event.rows });
  }

  protected reload(): void {
    this.reload$.next();
  }
}
