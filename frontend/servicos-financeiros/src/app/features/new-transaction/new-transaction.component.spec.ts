import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { TransactionResult } from '../../core/models/account.model';
import { isUuid } from '../../core/utils/uuid';
import { ACCOUNTS, provideTestEnvironment, textOf } from '../../../testing/test-providers';
import { NewTransactionComponent } from './new-transaction.component';

describe('NewTransactionComponent', () => {
  let fixture: ComponentFixture<NewTransactionComponent>;
  let component: NewTransactionComponent;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;

  const result: TransactionResult = {
    eventId: 'e',
    accountId: 'acc-1',
    type: 'CREDIT',
    amount: 250.5,
    balanceAfter: 1750.5,
    occurredAt: '2026-01-30T10:15:00Z',
    processedAt: '2026-01-30T10:15:01Z',
  };

  const flushAccounts = (): void => {
    http.expectOne('/api/accounts').flush(ACCOUNTS);
    fixture.detectChanges();
  };

  const submit = (): void => {
    fixture.debugElement.query(By.css('form')).triggerEventHandler('ngSubmit');
    fixture.detectChanges();
  };

  const fillValid = (type: 'CREDIT' | 'DEBIT' = 'CREDIT', amount = 250.5): void => {
    component.form.patchValue({ accountId: 'acc-1', type, amount });
    fixture.detectChanges();
  };

  const expectPost = (): TestRequest => http.expectOne('/api/transactions');

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [NewTransactionComponent], providers: provideTestEnvironment() });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(NewTransactionComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    flushAccounts();
  });

  afterEach(() => http.verify());

  describe('validação do formulário', () => {
    it('não envia nada e mostra os erros quando o formulário está vazio', () => {
      submit();

      http.expectNone('/api/transactions');
      expect(textOf(root())).toContain('Selecione uma conta.');
      expect(textOf(root())).toContain('Informe o valor.');
    });

    it('rejeita valor zero', () => {
      fillValid('CREDIT', 0);
      submit();

      http.expectNone('/api/transactions');
      expect(textOf(root())).toContain('O valor deve ser maior que zero.');
    });

    it('rejeita valor com mais de 2 casas decimais', () => {
      fillValid('CREDIT', 10.999);
      submit();

      http.expectNone('/api/transactions');
      expect(textOf(root())).toContain('Use no máximo 2 casas decimais.');
    });

    it('rejeita identificador de evento que não é UUID', () => {
      fillValid();
      component.form.controls.eventId.setValue('nao-e-uuid');
      submit();

      http.expectNone('/api/transactions');
      expect(textOf(root())).toContain('Informe um identificador UUID válido.');
    });

    it('já nasce com um UUID válido como identificador do evento', () => {
      expect(isUuid(component.form.controls.eventId.value)).toBeTrue();
    });

    it('pré-seleciona a conta recebida em ?conta=', () => {
      const other = TestBed.createComponent(NewTransactionComponent);
      other.componentRef.setInput('conta', 'acc-2');
      other.detectChanges();
      http.expectOne('/api/accounts').flush(ACCOUNTS);
      other.detectChanges();

      expect(other.componentInstance.form.controls.accountId.value).toBe('acc-2');
      expect(textOf(other.nativeElement)).toContain('Carlos Lima');
    });
  });

  describe('prévia do saldo', () => {
    it('mostra o saldo projetado para um crédito', () => {
      fillValid('CREDIT', 100);

      expect(textOf(root())).toContain('R$ 1.600,00');
    });

    it('avisa quando um débito excede o saldo, sem impedir o envio (o servidor decide)', () => {
      fillValid('DEBIT', 2000);

      expect(textOf(root())).toContain('Este débito excede o saldo.');
      expect(component.form.valid).toBeTrue();
    });
  });

  describe('envio', () => {
    it('envia o evento com o contrato da API e mostra o novo saldo', () => {
      fillValid('CREDIT', 250.5);
      const eventId = component.form.controls.eventId.value;

      submit();
      const request = expectPost();

      expect(request.request.body).toEqual(
        jasmine.objectContaining({ eventId, accountId: 'acc-1', type: 'CREDIT', amount: 250.5 }),
      );
      expect(request.request.body.occurredAt).toMatch(/^\d{4}-\d{2}-\d{2}T.*Z$/);
      expect(textOf(root())).toContain('Processando o lançamento');

      request.flush(result);
      fixture.detectChanges();

      expect(textOf(root())).toContain('Lançamento processado');
      expect(textOf(root())).toContain('Novo saldo: R$ 1.750,50');
      // Os saldos exibidos são recarregados da API, que é a fonte da verdade.
      http.expectOne('/api/accounts').flush(ACCOUNTS);
    });

    it('depois do sucesso, limpa o valor e gera um novo identificador de evento', () => {
      fillValid();
      const before = component.form.controls.eventId.value;

      submit();
      expectPost().flush(result);
      http.expectOne('/api/accounts').flush(ACCOUNTS);

      expect(component.form.controls.amount.value).toBeNull();
      expect(component.form.controls.eventId.value).not.toBe(before);
      expect(isUuid(component.form.controls.eventId.value)).toBeTrue();
    });

    it('não envia duas vezes enquanto o primeiro envio está em andamento', () => {
      fillValid();

      submit();
      submit();

      expectPost().flush(result);
      http.expectOne('/api/accounts').flush(ACCOUNTS);
    });
  });

  describe('respostas de erro da API', () => {
    it('evento duplicado (409): explica e mantém o identificador para nova tentativa', () => {
      fillValid();
      const eventId = component.form.controls.eventId.value;

      submit();
      expectPost().flush({}, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(textOf(root())).toContain('Evento duplicado');
      expect(textOf(root())).toContain('já foi processado');
      expect(component.form.controls.eventId.value).toBe(eventId);
    });

    it('saldo insuficiente (422): informa que o lançamento foi recusado', () => {
      fillValid('DEBIT', 2000);

      submit();
      expectPost().flush({}, { status: 422, statusText: 'Unprocessable Entity' });
      fixture.detectChanges();

      expect(textOf(root())).toContain('Saldo insuficiente');
      expect(textOf(root())).toContain('O lançamento foi recusado.');
    });

    it('falha de comunicação: mantém o identificador e diz que reenviar é seguro', () => {
      fillValid();
      const eventId = component.form.controls.eventId.value;

      submit();
      expectPost().error(new ProgressEvent('error'), { status: 0 });
      fixture.detectChanges();

      expect(textOf(root())).toContain('Sem conexão com o servidor');
      expect(textOf(root())).toContain('reenviar é seguro');
      expect(component.form.controls.eventId.value).toBe(eventId);
    });

    it('validação do servidor (400): mostra o erro no campo correspondente', () => {
      fillValid();

      submit();
      expectPost().flush(
        { errors: { Amount: ['O valor informado é inválido.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      fixture.detectChanges();

      expect(component.form.controls.amount.hasError('server')).toBeTrue();
      expect(textOf(root())).toContain('O valor informado é inválido.');
    });

    it('permite corrigir e reenviar depois de um erro', () => {
      fillValid('DEBIT', 2000);
      submit();
      expectPost().flush({}, { status: 422, statusText: 'Unprocessable Entity' });

      fillValid('DEBIT', 100);
      submit();
      expectPost().flush({ ...result, type: 'DEBIT', amount: 100, balanceAfter: 1400 });
      fixture.detectChanges();

      expect(textOf(root())).toContain('Lançamento processado');
      http.expectOne('/api/accounts').flush(ACCOUNTS);
    });
  });

  it('mostra erro ao carregar as contas e permite tentar de novo', () => {
    const other = TestBed.createComponent(NewTransactionComponent);
    other.detectChanges();
    http.expectOne('/api/accounts').error(new ProgressEvent('error'), { status: 0 });
    other.detectChanges();

    expect(textOf(other.nativeElement)).toContain('Não foi possível carregar as contas');
  });
});
