import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { Page, StatementEntry } from '../../core/models/account.model';
import { ACCOUNTS, entry, provideTestEnvironment, textOf } from '../../../testing/test-providers';
import { StatementComponent } from './statement.component';

describe('StatementComponent', () => {
  let fixture: ComponentFixture<StatementComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;

  const statementRequest = (): TestRequest =>
    http.expectOne((r) => r.url === '/api/accounts/acc-1/transactions');

  const page = (
    items: StatementEntry[],
    overrides: Partial<Page<StatementEntry>> = {},
  ): Page<StatementEntry> => ({
    items,
    page: 1,
    pageSize: 10,
    totalItems: items.length,
    totalPages: 1,
    ...overrides,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [StatementComponent], providers: provideTestEnvironment() });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StatementComponent);
    fixture.componentRef.setInput('id', 'acc-1');
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('busca a conta e a primeira página do extrato', () => {
    http.expectOne('/api/accounts/acc-1').flush(ACCOUNTS[0]);
    const request = statementRequest();

    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('10');
    request.flush(page([]));
  });

  it('mostra o carregamento antes dos dados chegarem', () => {
    expect(root().querySelector('app-loading')).not.toBeNull();

    http.expectOne('/api/accounts/acc-1').flush(ACCOUNTS[0]);
    statementRequest().flush(page([]));
  });

  it('exibe o saldo atual e o impacto de cada lançamento no saldo', () => {
    http.expectOne('/api/accounts/acc-1').flush(ACCOUNTS[0]);
    statementRequest().flush(
      page([
        entry({
          eventId: 'aaaaaaaa-0000-0000-0000-000000000002',
          type: 'DEBIT',
          amount: 40,
          signedAmount: -40,
          balanceAfter: 60,
        }),
        entry({
          eventId: 'aaaaaaaa-0000-0000-0000-000000000001',
          type: 'CREDIT',
          amount: 100,
          signedAmount: 100,
          balanceAfter: 100,
        }),
      ]),
    );
    fixture.detectChanges();

    expect(textOf(root().querySelector('.balance') as HTMLElement)).toContain('R$ 1.500,00');

    const rows = root().querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(textOf(rows[0] as HTMLElement)).toContain('Débito');
    expect(textOf(rows[0] as HTMLElement)).toContain('− R$ 40,00');
    expect(textOf(rows[0] as HTMLElement)).toContain('R$ 60,00');
    expect(textOf(rows[1] as HTMLElement)).toContain('Crédito');
    expect(textOf(rows[1] as HTMLElement)).toContain('+ R$ 100,00');
  });

  it('informa quando a conta não tem lançamentos', () => {
    http.expectOne('/api/accounts/acc-1').flush(ACCOUNTS[0]);
    statementRequest().flush(page([]));
    fixture.detectChanges();

    expect(textOf(root())).toContain('Esta conta ainda não tem lançamentos.');
    expect(root().querySelector('p-table')).toBeNull();
  });

  it('ao trocar de página, pede a página seguinte à API (que começa em 1)', () => {
    http.expectOne('/api/accounts/acc-1').flush(ACCOUNTS[0]);
    statementRequest().flush(
      page([entry({ eventId: 'aaaaaaaa-0000-0000-0000-000000000001' })], {
        pageSize: 5,
        totalItems: 12,
        totalPages: 3,
      }),
    );
    fixture.detectChanges();

    fixture.debugElement.query(By.css('p-table')).triggerEventHandler('onPage', { first: 5, rows: 5 });

    const next = statementRequest();
    expect(next.request.params.get('page')).toBe('2');
    expect(next.request.params.get('pageSize')).toBe('5');
    next.flush(
      page([entry({ eventId: 'aaaaaaaa-0000-0000-0000-000000000002' })], {
        page: 2,
        pageSize: 5,
        totalItems: 12,
        totalPages: 3,
      }),
    );
  });

  it('mantém a tabela visível enquanto a próxima página carrega', () => {
    http.expectOne('/api/accounts/acc-1').flush(ACCOUNTS[0]);
    statementRequest().flush(
      page([entry({ eventId: 'aaaaaaaa-0000-0000-0000-000000000001' })], {
        pageSize: 5,
        totalItems: 12,
        totalPages: 3,
      }),
    );
    fixture.detectChanges();

    fixture.debugElement.query(By.css('p-table')).triggerEventHandler('onPage', { first: 5, rows: 5 });
    fixture.detectChanges();

    expect(root().querySelector('p-table')).not.toBeNull();
    expect(root().querySelector('app-loading')).toBeNull();

    statementRequest().flush(page([], { page: 2, pageSize: 5, totalItems: 12, totalPages: 3 }));
  });

  it('ao trocar de conta, volta à primeira página e não mostra os lançamentos da conta anterior', () => {
    http.expectOne('/api/accounts/acc-1').flush(ACCOUNTS[0]);
    statementRequest().flush(
      page([entry({ eventId: 'aaaaaaaa-0000-0000-0000-000000000001' })], { pageSize: 5, totalItems: 12 }),
    );
    fixture.detectChanges();
    fixture.debugElement.query(By.css('p-table')).triggerEventHandler('onPage', { first: 5, rows: 5 });
    statementRequest().flush(
      page([entry({ eventId: 'aaaaaaaa-0000-0000-0000-000000000002' })], {
        page: 2,
        pageSize: 5,
        totalItems: 12,
      }),
    );
    fixture.detectChanges();

    // Mesma instância do componente com outra conta (ex.: navegação de /contas/acc-1 para /contas/acc-2).
    fixture.componentRef.setInput('id', 'acc-2');
    fixture.detectChanges();

    http.expectOne('/api/accounts/acc-2').flush(ACCOUNTS[1]);
    const next = http.expectOne((r) => r.url === '/api/accounts/acc-2/transactions');
    expect(next.request.params.get('page')).toBe('1');
    expect(next.request.params.get('pageSize')).toBe('5');
    expect(root().querySelector('tbody tr')).toBeNull();
    expect(root().querySelector('app-loading')).not.toBeNull();
    next.flush(page([]));
  });

  it('mostra "Conta não encontrada" sem oferecer nova tentativa (404)', () => {
    http.expectOne('/api/accounts/acc-1').flush(null, { status: 404, statusText: 'Not Found' });
    statementRequest().flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(textOf(root())).toContain('Conta não encontrada.');
    expect(root().querySelector('.p-button')).toBeNull();
  });

  it('mostra erro de comunicação no extrato com opção de tentar novamente', () => {
    http.expectOne('/api/accounts/acc-1').flush(ACCOUNTS[0]);
    statementRequest().error(new ProgressEvent('error'), { status: 0 });
    fixture.detectChanges();

    expect(textOf(root())).toContain('Não foi possível carregar o extrato');
    expect(root().querySelector('.p-button')).not.toBeNull();

    (root().querySelector('.p-button') as HTMLButtonElement).click();
    http.expectOne('/api/accounts/acc-1').flush(ACCOUNTS[0]);
    statementRequest().flush(page([]));
  });
});
