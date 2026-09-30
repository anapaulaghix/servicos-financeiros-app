import { CurrencyPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  FormControl,
  FormGroupDirective,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { InputNumberModule } from 'primeng/inputnumber';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { SelectButtonModule } from 'primeng/selectbutton';
import { BehaviorSubject, switchMap } from 'rxjs';

import { AccountsApi } from '../../core/api/accounts-api.service';
import { TransactionsApi } from '../../core/api/transactions-api.service';
import { ApiError, ApiErrorKind } from '../../core/errors/api-error';
import { IdempotencyKeyTracker } from '../../core/idempotency/idempotency-key-tracker';
import {
  Account,
  TransactionRequest,
  TransactionResult,
  TransactionType,
} from '../../core/models/account.model';
import { LoadState, toLoadState } from '../../core/state/load-state';
import {
  CustomValidators,
  DynamicValidatorMessageDirective,
  ErrorStateMatcherService,
  OnTouchedErrorStateMatcherService,
} from '../../shared/input-error';
import { ErrorPanelComponent } from '../../shared/error-panel/error-panel.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { Submission, ERROR_TITLES } from '../../core/models/transaction.model';

@Component({
  selector: 'app-new-transaction',
  standalone: true,
  imports: [
    CurrencyPipe,
    ReactiveFormsModule,
    RouterLink,
    ButtonModule,
    DatePickerModule,
    InputNumberModule,
    MessageModule,
    SelectModule,
    SelectButtonModule,
    PageHeaderComponent,
    ErrorPanelComponent,
    DynamicValidatorMessageDirective,
  ],
  providers: [{ provide: ErrorStateMatcherService, useClass: OnTouchedErrorStateMatcherService }],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './new-transaction.component.html',
  styleUrl: './new-transaction.component.scss',
})
export class NewTransactionComponent {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly accountsApi = inject(AccountsApi);
  private readonly transactionsApi = inject(TransactionsApi);
  private readonly reloadAccounts$ = new BehaviorSubject<void>(undefined);
  private readonly formDirective = viewChild.required(FormGroupDirective);
  /** Chave de idempotência (eventId): gerada e controlada internamente, nunca exibida ao usuário. */
  private readonly idempotencyKeys = new IdempotencyKeyTracker<Omit<TransactionRequest, 'eventId'>>();

  readonly conta = input<string>();

  protected readonly typeOptions: { label: string; value: TransactionType }[] = [
    { label: 'Crédito', value: 'CREDIT' },
    { label: 'Débito', value: 'DEBIT' },
  ];

  readonly form = this.fb.group({
    accountId: this.fb.control('', Validators.required),
    type: this.fb.control<TransactionType>('CREDIT', Validators.required),
    amount: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(0.01),
      CustomValidators.maxDecimals(2),
    ]),
    occurredAt: new FormControl<Date | null>(new Date(), Validators.required),
  });

  protected readonly accountsState = toSignal(
    this.reloadAccounts$.pipe(switchMap(() => this.accountsApi.list().pipe(toLoadState()))),
    { initialValue: { status: 'loading' } as LoadState<Account[]> },
  );

  protected readonly submission = signal<Submission>({ status: 'idle' });

  protected readonly accounts = computed(() => {
    const state = this.accountsState();
    return state.status === 'ready' ? state.data : [];
  });

  private readonly selectedAccountId = toSignal(this.form.controls.accountId.valueChanges, {
    initialValue: this.form.controls.accountId.value,
  });
  private readonly selectedType = toSignal(this.form.controls.type.valueChanges, {
    initialValue: this.form.controls.type.value,
  });
  private readonly typedAmount = toSignal(this.form.controls.amount.valueChanges, {
    initialValue: this.form.controls.amount.value,
  });

  protected readonly selectedAccount = computed(() =>
    this.accounts().find((account) => account.id === this.selectedAccountId()),
  );

  protected readonly projectedBalance = computed(() => {
    const account = this.selectedAccount();
    const amount = this.typedAmount();
    if (!account || amount === null || amount <= 0) {
      return null;
    }
    const delta = this.selectedType() === 'CREDIT' ? amount : -amount;
    return Math.round((account.balance + delta) * 100) / 100;
  });

  protected readonly wouldOverdraw = computed(() => {
    const projected = this.projectedBalance();
    return projected !== null && projected < 0;
  });

  constructor() {
    // Pré-seleciona a conta recebida em `?conta=` (ex.: botão "Novo lançamento" do extrato).
    effect(() => {
      const conta = this.conta();
      if (conta) {
        this.form.controls.accountId.setValue(conta);
      }
    });
  }

  protected submit(): void {
    if (this.submission().status === 'processing') {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const transaction = {
      accountId: value.accountId,
      type: value.type,
      amount: value.amount as number,
      occurredAt: (value.occurredAt as Date).toISOString(),
    };
    // Mesmos dados de uma tentativa sem confirmação reutilizam a chave (retry seguro); dados novos, chave nova.
    const request: TransactionRequest = { eventId: this.idempotencyKeys.keyFor(transaction), ...transaction };

    this.submission.set({ status: 'processing' });
    this.transactionsApi.submit(request).subscribe({
      next: (result) => this.onSuccess(result),
      error: (error: ApiError) => this.onError(error),
    });
  }

  protected reloadAccounts(): void {
    this.reloadAccounts$.next();
  }

  protected errorTitle(kind: ApiErrorKind): string {
    return ERROR_TITLES[kind];
  }

  protected isRetrySafe(error: ApiError): boolean {
    return error.kind === 'network' || error.kind === 'server';
  }

  private onSuccess(result: TransactionResult): void {
    const holderName = this.selectedAccount()?.holderName ?? 'a conta';
    this.submission.set({ status: 'success', result, holderName });

    this.idempotencyKeys.complete();
    const { accountId, type } = this.form.getRawValue();
    this.formDirective().resetForm({ accountId, type, amount: null, occurredAt: new Date() });
    this.reloadAccounts$.next();
  }

  private onError(error: ApiError): void {
    this.submission.set({ status: 'error', error });

    // Duplicado significa que uma tentativa anterior (cuja resposta se perdeu) já foi processada:
    // o lançamento está confirmado, então a próxima operação usa outra chave e os saldos são recarregados.
    if (error.kind === 'duplicate') {
      this.idempotencyKeys.complete();
      this.reloadAccounts$.next();
      return;
    }

    for (const [field, messages] of Object.entries(error.fieldErrors)) {
      const control = this.form.get(field);
      if (control && messages.length > 0) {
        control.markAsTouched();
        control.setErrors({ server: messages[0] });
      }
    }
  }
}
