import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  FormControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { SelectButtonModule } from 'primeng/selectbutton';
import { BehaviorSubject, switchMap } from 'rxjs';

import { AccountsApi } from '../../core/api/accounts-api.service';
import { TransactionsApi } from '../../core/api/transactions-api.service';
import { ApiError, ApiErrorKind } from '../../core/errors/api-error';
import {
  Account,
  TransactionRequest,
  TransactionResult,
  TransactionType,
} from '../../core/models/account.model';
import { LoadState, toLoadState } from '../../core/state/load-state';
import { isUuid, newUuid } from '../../core/utils/uuid';
import { ErrorPanelComponent } from '../../shared/error-panel/error-panel.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

type Submission =
  | { status: 'idle' }
  | { status: 'processing' }
  | { status: 'success'; result: TransactionResult; holderName: string }
  | { status: 'error'; error: ApiError };

type FieldName = 'accountId' | 'amount' | 'occurredAt' | 'eventId';

/** Aceita no máximo 2 casas decimais, como a API (valores monetários). */
const maxTwoDecimals: ValidatorFn = (control): ValidationErrors | null => {
  const value = control.value as number | null;
  if (value === null || value === undefined) {
    return null;
  }
  return /^\d+(\.\d{1,2})?$/.test(String(value)) ? null : { maxDecimals: true };
};

const uuidValidator: ValidatorFn = (control): ValidationErrors | null =>
  isUuid(String(control.value ?? '').trim()) ? null : { uuid: true };

const ERROR_TITLES: Record<ApiErrorKind, string> = {
  duplicate: 'Evento duplicado',
  'insufficient-funds': 'Saldo insuficiente',
  network: 'Sem conexão com o servidor',
  validation: 'Dados inválidos',
  'not-found': 'Conta não encontrada',
  server: 'Falha no servidor',
};

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
    InputTextModule,
    MessageModule,
    SelectModule,
    SelectButtonModule,
    PageHeaderComponent,
    ErrorPanelComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './new-transaction.component.html',
  styleUrl: './new-transaction.component.scss',
})
export class NewTransactionComponent {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly accountsApi = inject(AccountsApi);
  private readonly transactionsApi = inject(TransactionsApi);
  private readonly reloadAccounts$ = new BehaviorSubject<void>(undefined);

  /** Conta pré-selecionada, vinda de `?conta=` (ex.: botão "Novo lançamento" do extrato). */
  readonly conta = input<string>();

  protected readonly typeOptions: { label: string; value: TransactionType }[] = [
    { label: 'Crédito', value: 'CREDIT' },
    { label: 'Débito', value: 'DEBIT' },
  ];

  readonly form = this.fb.group({
    accountId: this.fb.control('', Validators.required),
    type: this.fb.control<TransactionType>('CREDIT', Validators.required),
    amount: new FormControl<number | null>(null, [Validators.required, Validators.min(0.01), maxTwoDecimals]),
    occurredAt: new FormControl<Date | null>(new Date(), Validators.required),
    // O eventId é a chave de idempotência: só muda depois de um lançamento bem-sucedido.
    eventId: this.fb.control(newUuid(), [Validators.required, uuidValidator]),
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

  /** Pré-visualização do saldo após o lançamento. É só orientação: quem decide é o servidor. */
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
    const request: TransactionRequest = {
      eventId: value.eventId.trim(),
      accountId: value.accountId,
      type: value.type,
      amount: value.amount as number,
      occurredAt: (value.occurredAt as Date).toISOString(),
    };

    this.submission.set({ status: 'processing' });
    this.transactionsApi.submit(request).subscribe({
      next: (result) => this.onSuccess(result),
      error: (error: ApiError) => this.onError(error),
    });
  }

  protected regenerateEventId(): void {
    this.form.controls.eventId.setValue(newUuid());
  }

  protected reloadAccounts(): void {
    this.reloadAccounts$.next();
  }

  /** Mensagem do primeiro erro do campo, mostrada só depois que o usuário interagiu com ele. */
  protected fieldError(name: FieldName): string | null {
    const control = this.form.controls[name];
    if (!control.invalid || !(control.touched || control.dirty)) {
      return null;
    }

    const errors = control.errors ?? {};
    if (errors['server']) {
      return String(errors['server']);
    }

    switch (name) {
      case 'accountId':
        return 'Selecione uma conta.';
      case 'amount':
        if (errors['required']) return 'Informe o valor.';
        if (errors['maxDecimals']) return 'Use no máximo 2 casas decimais.';
        return 'O valor deve ser maior que zero.';
      case 'occurredAt':
        return 'Informe a data e a hora da ocorrência.';
      case 'eventId':
        return 'Informe um identificador UUID válido.';
    }
  }

  protected errorTitle(kind: ApiErrorKind): string {
    return ERROR_TITLES[kind];
  }

  /** Falhas de comunicação não dizem se o evento chegou ao servidor; reenviar é seguro por causa da idempotência. */
  protected isRetrySafe(error: ApiError): boolean {
    return error.kind === 'network' || error.kind === 'server';
  }

  private onSuccess(result: TransactionResult): void {
    const holderName = this.selectedAccount()?.holderName ?? 'a conta';
    this.submission.set({ status: 'success', result, holderName });

    this.form.controls.amount.reset(null);
    this.form.controls.occurredAt.setValue(new Date());
    this.form.controls.eventId.setValue(newUuid());
    this.reloadAccounts$.next();
  }

  private onError(error: ApiError): void {
    this.submission.set({ status: 'error', error });

    for (const [field, messages] of Object.entries(error.fieldErrors)) {
      const control = this.form.get(field);
      if (control && messages.length > 0) {
        control.setErrors({ server: messages[0] });
        control.markAsTouched();
      }
    }
  }
}
