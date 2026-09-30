import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { AppComponent } from '../app.component';
import { MainLayoutComponent } from './main-layout/main-layout.component';

@Component({ standalone: true, template: '<p id="page">conteúdo da tela</p>' })
class FakePageComponent {}

describe('Layout', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: '',
            component: MainLayoutComponent,
            children: [{ path: 'contas', component: FakePageComponent }],
          },
        ]),
      ],
    });
  });

  const renderAt = async (url: string): Promise<HTMLElement> => {
    const harness = await RouterTestingHarness.create(url);
    return harness.fixture.nativeElement as HTMLElement;
  };

  it('AppComponent só hospeda o roteador, sem markup próprio', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.children.length).toBe(1);
    expect(root.firstElementChild?.tagName).toBe('ROUTER-OUTLET');
  });

  it('as telas são renderizadas dentro do layout, ao lado da navegação', async () => {
    const root = await renderAt('/contas');

    expect(root.querySelector('app-main-layout app-sidebar')).not.toBeNull();
    expect(root.querySelector('app-main-layout main #page')).not.toBeNull();
  });

  it('a navegação lista as telas e marca a ativa', async () => {
    const root = await renderAt('/contas');
    const links = Array.from(root.querySelectorAll<HTMLAnchorElement>('app-sidebar .nav__link'));

    expect(links.map((link) => link.getAttribute('href'))).toEqual(['/contas', '/lancamento']);
    expect(links[0].classList).toContain('is-active');
    expect(links[1].classList).not.toContain('is-active');
  });
});
