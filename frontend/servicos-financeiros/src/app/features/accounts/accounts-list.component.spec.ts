import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ACCOUNTS, provideTestEnvironment, textOf } from '../../../testing/test-providers';
import { AccountsListComponent } from './accounts-list.component';

describe('AccountsListComponent', () => {
  let fixture: ComponentFixture<AccountsListComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [AccountsListComponent], providers: provideTestEnvironment() });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AccountsListComponent);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('mostra o estado de carregamento enquanto a API não responde', () => {
    expect(root().querySelector('app-loading')).not.toBeNull();
    expect(root().querySelector('.card')).toBeNull();

    http.expectOne('/api/accounts').flush([]);
  });

  it('lista as contas com os saldos consolidados', () => {
    http.expectOne('/api/accounts').flush(ACCOUNTS);
    fixture.detectChanges();

    const cards = root().querySelectorAll('.card');
    expect(cards.length).toBe(2);
    expect(textOf(cards[0] as HTMLElement)).toContain('Ana Paula Ghis');
    expect(textOf(cards[0] as HTMLElement)).toContain('R$ 1.500,00');
    expect(textOf(cards[1] as HTMLElement)).toContain('R$ 320,50');
    expect(root().querySelector('app-loading')).toBeNull();
  });

  it('soma o saldo consolidado de todas as contas', () => {
    http.expectOne('/api/accounts').flush(ACCOUNTS);
    fixture.detectChanges();

    expect(textOf(root().querySelector('.summary') as HTMLElement)).toContain('R$ 1.820,50');
  });

  it('cada conta leva ao extrato dela', () => {
    http.expectOne('/api/accounts').flush(ACCOUNTS);
    fixture.detectChanges();

    const link = root().querySelector('.card') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/contas/acc-1');
  });

  it('informa quando não há contas', () => {
    http.expectOne('/api/accounts').flush([]);
    fixture.detectChanges();

    expect(textOf(root())).toContain('Nenhuma conta cadastrada.');
  });

  it('mostra o erro de comunicação e permite tentar novamente', () => {
    http.expectOne('/api/accounts').error(new ProgressEvent('error'), { status: 0 });
    fixture.detectChanges();

    expect(root().querySelector('[role="alert"]')).not.toBeNull();
    expect(textOf(root())).toContain('Não foi possível se comunicar com o servidor');

    (root().querySelector('.p-button') as HTMLButtonElement).click();
    fixture.detectChanges();

    // A nova tentativa volta ao estado de carregamento e refaz a chamada.
    expect(root().querySelector('app-loading')).not.toBeNull();
    http.expectOne('/api/accounts').flush(ACCOUNTS);
    fixture.detectChanges();

    expect(root().querySelectorAll('.card').length).toBe(2);
    expect(root().querySelector('[role="alert"]')).toBeNull();
  });
});
