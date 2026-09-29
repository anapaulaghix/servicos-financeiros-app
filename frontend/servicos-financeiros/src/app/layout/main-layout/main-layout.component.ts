import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { SidebarComponent } from '../sidebar/sidebar.component';

/** Estrutura das telas autenticadas do produto: navegação lateral e área de conteúdo. */
@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [RouterOutlet, SidebarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="shell">
      <app-sidebar />
      <main class="shell__main" id="conteudo">
        <router-outlet />
      </main>
    </div>
  `,
  styleUrl: './main-layout.component.scss',
})
export class MainLayoutComponent {}
