import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withFetch } from '@angular/common/http';
import localePt from '@angular/common/locales/pt';
import { ApplicationConfig, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { providePrimeNG } from 'primeng/config';

import { routes } from './app.routes';
import { PT_BR_TRANSLATION } from './theme/pt-br-translation';
import { AppPreset } from './theme/app-preset';

registerLocaleData(localePt);

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    // withComponentInputBinding: parâmetros de rota e query string chegam como inputs dos componentes.
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch()),
    provideAnimationsAsync(),
    providePrimeNG({
      theme: { preset: AppPreset, options: { darkModeSelector: 'none' } },
      translation: PT_BR_TRANSLATION,
    }),
    { provide: LOCALE_ID, useValue: 'pt-BR' },
  ],
};
