import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'contas' },
  {
    path: 'contas',
    title: 'Contas · Serviços Financeiros',
    loadComponent: () =>
      import('./features/accounts/accounts-list.component').then((m) => m.AccountsListComponent),
  },
  {
    path: 'contas/:id',
    title: 'Extrato · Serviços Financeiros',
    loadComponent: () =>
      import('./features/statement/statement.component').then((m) => m.StatementComponent),
  },
  {
    path: 'lancamento',
    title: 'Novo lançamento · Serviços Financeiros',
    loadComponent: () =>
      import('./features/new-transaction/new-transaction.component').then(
        (m) => m.NewTransactionComponent,
      ),
  },
  { path: '**', redirectTo: 'contas' },
];
