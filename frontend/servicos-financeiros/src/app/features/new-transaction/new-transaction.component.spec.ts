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

  /** Envio real (evento submit do DOM), para o FormGroupDirective marcar o formulário como enviado. */
  const submit = (): void => {
    (fixture.debugElement.query(By.css('form')).nativeElement as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );
    fixture.detectChanges();
  };

  /** Mensagem de validação exibida abaixo do campo (inserida pela DynamicValidatorMessageDirective). */
  const fieldError = (hostSelector: string): string | null => {
    const error = root().querySelector(`${hostSelector} + app-input-error`);
    return error ? textOf(error as HTMLElement) : null;
  };

  const fillValid = (type: 'CREDIT' | 'DEBIT' = 'CREDIT', amount = 250.5): void => {
    component.form.patchValue({ accountId: 'acc-1', type, amount });
    fixture.detectChanges();
  };

  const expectPost = (): TestRequest => http.expectOne('/api/transactions');

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [NewTransactionComponent],
      providers: provideTestEnvironment(),
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(NewTransactionComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    flushAccounts();
  });

  afterEach(() => http.verify());

  describe('validação do formulário', () => {
    it('não mostra erros num formulário recém-aberto', () => {
      expect(root().querySelector('app-input-error')).toBeNull();
    });

    it('não envia nada e mostra os erros abaixo de cada campo quando o formulário está vazio', () => {
      submit();

      http.expectNone('/api/transactions');
      expect(fieldError('p-select')).toBe('Campo obrigatório.');
      expect(fieldError('p-inputnumber')).toBe('Campo obrigatório.');
    });

    it('rejeita valor zero', () => {
      fillValid('CREDIT', 0);
      submit();

      http.expectNone('/api/transactions');
      expect(fieldError('p-inputnumber')).toBe('O valor mínimo é 0,01.');
    });

    it('rejeita valor com mais de 2 casas decimais', () => {
      fillValid('CREDIT', 10.999);
      submit();

      http.expectNone('/api/transactions');
      expect(fieldError('p-inputnumber')).toBe('Use no máximo 2 casas decimais.');
    });

    it('marca visualmente o campo inválido', () => {
      submit();

      expect(root().querySelector('p-inputnumber')!.classList).toContain('field-invalid');
    });

    it('não exibe o identificador do evento (chave de idempotência) na tela', () => {
      expect(root().querySelector('#eventId')).toBeNull();
      expect(textOf(root())).not.toContain('Identificador do evento');
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

    it('mostra a prévia mesmo quando o saldo projetado é exatamente zero', () => {
      fillValid('DEBIT', 1500); // conta com R$ 1.500,00

      const summary = textOf(root().querySelector('.summary') as HTMLElement);
      expect(summary).toContain('Saldo após o lançamento');
      expect(summary).toContain('R$ 0,00');
    });

    it('avisa quando um débito excede o saldo, sem impedir o envio (o servidor decide)', () => {
      fillValid('DEBIT', 2000);

      expect(textOf(root())).toContain('Este débito excede o saldo.');
      expect(component.form.valid).toBeTrue();
    });
  });

  describe('envio', () => {
    it('envia o evento com o contrato da API, com um eventId gerado internamente, e mostra o novo saldo', () => {
      fillValid('CREDIT', 250.5);

      submit();
      const request = expectPost();

      expect(request.request.body).toEqual(
        jasmine.objectContaining({ accountId: 'acc-1', type: 'CREDIT', amount: 250.5 }),
      );
      expect(isUuid(request.request.body.eventId)).toBeTrue();
      expect(request.request.body.occurredAt).toMatch(/^\d{4}-\d{2}-\d{2}T.*Z$/);
      expect(textOf(root())).toContain('Processando o lançamento');

      request.flush(result);
      fixture.detectChanges();

      expect(textOf(root())).toContain('Lançamento processado');
      expect(textOf(root())).toContain('Novo saldo: R$ 1.750,50');
      // Os saldos exibidos são recarregados da API, que é a fonte da verdade.
      http.expectOne('/api/accounts').flush(ACCOUNTS);
    });

    it('depois do sucesso, limpa o valor e um lançamento igual recebe outro eventId', () => {
      fillValid();
      submit();
      const first = expectPost();
      first.flush(result);
      http.expectOne('/api/accounts').flush(ACCOUNTS);

      expect(component.form.controls.amount.value).toBeNull();

      // Mesmos dados de novo: é um segundo lançamento legítimo, não um reenvio.
      component.form.patchValue({ amount: 250.5, occurredAt: new Date(first.request.body.occurredAt) });
      submit();
      const second = expectPost();

      expect(second.request.body.eventId).not.toBe(first.request.body.eventId);
      second.flush(result);
      http.expectOne('/api/accounts').flush(ACCOUNTS);
    });

    it('depois do sucesso, mantém conta e tipo e não acusa o valor vazio como erro', () => {
      fillValid('DEBIT', 10);

      submit();
      expectPost().flush({ ...result, type: 'DEBIT', amount: 10 });
      http.expectOne('/api/accounts').flush(ACCOUNTS);
      fixture.detectChanges();

      expect(component.form.controls.accountId.value).toBe('acc-1');
      expect(component.form.controls.type.value).toBe('DEBIT');
      expect(root().querySelector('app-input-error')).toBeNull();
    });

    it('não envia duas vezes enquanto o primeiro envio está em andamento', () => {
      fillValid();

      submit();
      submit();

      const requests = http.match('/api/transactions');
      expect(requests.length).toBe(1);
      requests[0].flush(result);
      http.expectOne('/api/accounts').flush(ACCOUNTS);
    });
  });

  describe('respostas de erro da API', () => {
    it('duplicado (409): informa que o lançamento já estava registrado e recarrega os saldos', () => {
      fillValid();
      submit();
      const first = expectPost();
      first.flush({}, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(textOf(root())).toContain('Lançamento já registrado');
      expect(textOf(root())).toContain('Nenhum valor foi lançado novamente.');
      http.expectOne('/api/accounts').flush(ACCOUNTS);

      // Confirmado pelo servidor: um novo envio, mesmo com dados iguais, é outro lançamento.
      submit();
      const second = expectPost();
      expect(second.request.body.eventId).not.toBe(first.request.body.eventId);
      second.flush(result);
      http.expectOne('/api/accounts').flush(ACCOUNTS);
    });

    it('saldo insuficiente (422): informa que o lançamento foi recusado', () => {
      fillValid('DEBIT', 2000);

      submit();
      expectPost().flush({}, { status: 422, statusText: 'Unprocessable Entity' });
      fixture.detectChanges();

      expect(textOf(root())).toContain('Saldo insuficiente');
      expect(textOf(root())).toContain('O lançamento foi recusado.');
    });

    it('falha de comunicação: avisa que tentar de novo é seguro e o reenvio usa o mesmo eventId', () => {
      fillValid();
      submit();
      const first = expectPost();
      first.error(new ProgressEvent('error'), { status: 0 });
      fixture.detectChanges();

      expect(textOf(root())).toContain('Sem conexão com o servidor');
      expect(textOf(root())).toContain('tentar de novo com segurança');

      // Se a primeira tentativa chegou ao servidor, a mesma chave faz o backend não lançar de novo.
      submit();
      const retry = expectPost();
      expect(retry.request.body.eventId).toBe(first.request.body.eventId);
      retry.flush(result);
      http.expectOne('/api/accounts').flush(ACCOUNTS);
    });

    it('falha de comunicação seguida de alteração nos dados: o novo envio usa outro eventId', () => {
      fillValid('CREDIT', 100);
      submit();
      const first = expectPost();
      first.error(new ProgressEvent('error'), { status: 0 });

      fillValid('CREDIT', 150);
      submit();
      const changed = expectPost();

      expect(changed.request.body.eventId).not.toBe(first.request.body.eventId);
      changed.flush(result);
      http.expectOne('/api/accounts').flush(ACCOUNTS);
    });

    it('limite de requisições (429): pede para aguardar e o reenvio dos mesmos dados usa o mesmo eventId', () => {
      fillValid();
      submit();
      const first = expectPost();
      first.flush({}, { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '3' } });
      fixture.detectChanges();

      expect(textOf(root())).toContain('Aguarde um instante');
      expect(textOf(root())).toContain('Aguarde 3 segundos');

      // Nada foi processado: a mesma chave é reaproveitada quando o usuário tenta de novo.
      submit();
      const retry = expectPost();
      expect(retry.request.body.eventId).toBe(first.request.body.eventId);
      retry.flush(result);
      http.expectOne('/api/accounts').flush(ACCOUNTS);
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
      expect(fieldError('p-inputnumber')).toBe('O valor informado é inválido.');
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
