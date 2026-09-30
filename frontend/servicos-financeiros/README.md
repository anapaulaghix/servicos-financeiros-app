# Serviços Financeiros — Frontend

Aplicação web em **Angular 19** (standalone, `OnPush`, TypeScript `strict`) com **PrimeNG 19** e **RxJS**. Lista as contas com o saldo consolidado, mostra o extrato paginado de cada conta e permite lançar créditos e débitos.

A visão geral da solução (arquitetura de ponta a ponta, regras de negócio, trade-offs) está no [README da raiz](../../README.md).

## Sumário

- [Como rodar](#como-rodar)
- [Estrutura de pastas](#estrutura-de-pastas)
- [Fluxo de dados](#fluxo-de-dados)
- [Validação de formulários: `shared/input-error`](#validação-de-formulários-sharedinput-error)
- [Tema e estilos](#tema-e-estilos)
- [Testes](#testes)

## Como rodar

**Pré-requisitos:** Node.js 20+, npm e, para os testes, o Google Chrome instalado (o Karma usa o Chrome em modo headless).

```bash
npm install
npm start          # http://localhost:4200
```

O `npm start` roda o `ng serve` com o `proxy.conf.json`, que encaminha `/api` para `http://localhost:8080`. **A API precisa estar no ar**; o jeito mais simples é subir só o backend pelo Docker, a partir da raiz do repositório:

```bash
docker compose up -d db redis api
```

| Comando | O que faz |
|---|---|
| `npm start` | Servidor de desenvolvimento com recarga automática e proxy para a API |
| `npm run build` | Build de produção em `dist/servicos-financeiros/browser` |
| `npm test` | Testes em modo observação (abre o Chrome) |
| `npm run test:ci` | Testes uma vez, em Chrome headless (o que a CI usa) |
| `npm run lint` | ESLint (regras do Angular, sem `any`, `OnPush` obrigatório) |
| `npm run format` / `format:check` | Prettier: formata / só verifica |

Sem Node ou Chrome instalados, os testes rodam pelo Docker, na raiz do repositório: `docker compose --profile test run --rm test-frontend`.

No Docker, o `Dockerfile` compila o app e o serve com **nginx sem root**; o `nginx.conf` faz o fallback de rotas do Angular e o proxy de `/api` para a API.

## Estrutura de pastas

```
servicos-financeiros/
├── src/
│   ├── app/
│   │   ├── app.component.ts     raiz: só o <router-outlet />
│   │   ├── app.config.ts        providers globais (roteador, HTTP, PrimeNG, locale pt-BR)
│   │   ├── app.routes.ts        rotas: layout como rota pai + telas com lazy loading
│   │   ├── core/                regras de acesso a dados, sem UI
│   │   ├── features/            uma pasta por tela
│   │   ├── layout/              estrutura visual comum às telas
│   │   ├── shared/              blocos reutilizáveis de UI
│   │   └── theme/               tema do PrimeNG e traduções
│   ├── styles/                  tokens de design e estilos globais (SCSS)
│   ├── testing/                 utilitários compartilhados pelos testes
│   ├── index.html
│   └── main.ts
├── Dockerfile / nginx.conf      imagem de produção (build Node → nginx) e estágio de testes (Chromium)
├── security-headers.conf        cabeçalhos de segurança incluídos em cada rota do nginx
├── karma.conf.js                launcher ChromeHeadlessCI (sem sandbox) para CI e container
├── eslint.config.js / .prettierrc.json   lint e formatação
├── proxy.conf.json              proxy de /api no ng serve
└── angular.json / tsconfig*.json
```

### `app/` (raiz)

| Arquivo | Responsabilidade |
|---|---|
| `app.component.ts` | Hospeda só o `<router-outlet />`. Ele não tem layout, estilos nem lógica: numa aplicação real, a raiz acumular responsabilidades dificulta ter áreas com layouts diferentes (ex.: login sem barra lateral). |
| `app.config.ts` | Composição dos providers: `provideRouter` com `withComponentInputBinding` (parâmetros de rota e query string chegam como `input()`), `provideHttpClient`, `providePrimeNG` com o tema e as traduções, e `LOCALE_ID` pt-BR (moeda e datas). |
| `app.routes.ts` | `MainLayoutComponent` como rota pai sem caminho; as telas são filhas e carregadas sob demanda (`loadComponent`). Cada rota define o título da aba. |

### `core/`: acesso a dados e regras da interface

Nada aqui conhece componentes ou templates, então tudo é testável isoladamente.

| Pasta / arquivo | Responsabilidade |
|---|---|
| `api/api.config.ts` | `API_BASE = '/api'`. A URL é relativa de propósito: o proxy (dev) ou o nginx (Docker) encaminha para a API, então não há CORS nem URL por ambiente. |
| `api/accounts-api.service.ts` | `list()`, `get(id)` e `statement(id, page, pageSize)`. Retornam `Observable` tipado e convertem qualquer falha HTTP em `ApiError`. |
| `api/transactions-api.service.ts` | `submit(request)`: `POST /api/transactions`. |
| `errors/api-error.ts` | `toApiError()` traduz `HttpErrorResponse`/`ProblemDetails` em `ApiError` com um `kind`: `network`, `validation`, `duplicate`, `insufficient-funds`, `not-found`, `rate-limited`, `server`. Os componentes reagem ao `kind` e nunca a códigos HTTP; a mensagem do 429 usa o `Retry-After`. `mapApiError()` é o operador RxJS que os serviços aplicam para isso. |
| `state/load-state.ts` | Operador RxJS `toLoadState()`: transforma uma chamada em `loading → ready \| error`. Dentro de um `switchMap`, cada nova chamada emite `loading` primeiro e uma falha vira estado, sem encerrar o fluxo. |
| `idempotency/idempotency-key-tracker.ts` | Gera o `eventId` (chave de idempotência) sem mostrá-lo ao usuário. Reutiliza a chave quando os mesmos dados são reenviados após uma falha sem confirmação; qualquer mudança nos dados, ou a confirmação do servidor, gera chave nova. |
| `models/account.model.ts` | Contratos da API: `Account`, `StatementEntry`, `Page<T>`, `TransactionRequest`, `TransactionResult`, `TransactionType`. |
| `models/statement.models.ts` | Modelos da tela de extrato: `Paging` e `StatementView` (dados da página, carregando, erro). |
| `models/transaction.model.ts` | Estado do envio do formulário (`Submission`) e os títulos exibidos para cada tipo de erro (`ERROR_TITLES`). |
| `utils/uuid.ts` | `newUuid()` com *fallback* para `crypto.getRandomValues`, porque `crypto.randomUUID` só existe em contexto seguro (HTTPS ou localhost). `isUuid()` valida o formato. |

### `features/`: as telas

Cada tela é um componente standalone com template, estilos e testes próprios, carregado por lazy loading.

| Pasta | Rota | O que faz |
|---|---|---|
| `accounts/` | `/contas` | Saldo consolidado, número de contas e um cartão por conta com o saldo atual e o link para o extrato. Trata carregamento, lista vazia e erro com "Tentar novamente". |
| `statement/` | `/contas/:id` | Saldo atual e extrato paginado em `p-table` (modo *lazy*: a paginação é feita pela API). Mostra valor com sinal (+/−), tipo e saldo após cada lançamento. Mantém a página anterior visível enquanto a próxima carrega, e ao trocar de conta volta à primeira página sem mostrar lançamentos da conta anterior. |
| `new-transaction/` | `/lancamento` e `/lancamento?conta=<id>` | Formulário reativo de lançamento: conta, tipo, valor e data. Prévia do saldo após o lançamento (só orientação; quem decide é a API), bloqueio de envio duplo e mensagens para sucesso, reenvio já registrado, processando, `eventId` em conflito, saldo insuficiente, limite de requisições e falha de rede. |

### `layout/`: estrutura das telas

| Pasta | Responsabilidade |
|---|---|
| `main-layout/` | Grade da aplicação: barra lateral e área de conteúdo com o `<router-outlet />` das telas. Em telas estreitas vira uma coluna. |
| `sidebar/` | Marca e navegação (itens definidos numa lista no componente, com o item ativo destacado). Em telas estreitas vira um cabeçalho horizontal. |
| `layout.spec.ts` | Garante que o `AppComponent` só tem o roteador, que as telas renderizam dentro do layout e que a navegação marca o item ativo. |

### `shared/`: blocos reutilizáveis

| Pasta | Responsabilidade |
|---|---|
| `input-error/` | Validação de formulários com mensagens automáticas. Detalhada [abaixo](#validação-de-formulários-sharedinput-error). |
| `error-panel/` | Painel de erro (`p-message`) com título, mensagem e botão "Tentar novamente" opcional. |
| `loading/` | Barra de progresso indeterminada com rótulo acessível (`role="status"`). |
| `page-header/` | Cabeçalho das telas: sobretítulo, título grande e um espaço para ações (`<ng-content>`). |
| `pipes/signed-money.pipe.ts` | `signedMoney`: valor em reais com sinal explícito (`+ R$ 10,00` / `− R$ 10,00`). |

### `theme/`, `styles/` e `testing/`

| Pasta / arquivo | Responsabilidade |
|---|---|
| `theme/app-preset.ts` | Preset do PrimeNG sobre o Aura: paleta do produto, cantos retos e cores das mensagens, tags e barra de progresso. |
| `theme/pt-br-translation.ts` | Textos em português dos componentes PrimeNG (calendário, paginador etc.). |
| `styles/_tokens.scss` | Variáveis CSS da identidade visual: cores, fontes, traços, largura da barra lateral. |
| `styles/_base.scss` | Reset, tipografia, `.num` (números monoespaçados e tabulares), `.eyebrow`, `.w-full`. |
| `styles/_components.scss` | Botão próprio do produto (`.btn`, `.btn--ghost`). |
| `styles/_primeng.scss` | Ajustes finos sobre o PrimeNG, incluindo o destaque de campos inválidos (`.field-invalid`). |
| `testing/test-providers.ts` | `provideTestEnvironment()` (HTTP simulado, rotas vazias, pt-BR, sem animações) e dados de exemplo usados pelos testes. |

## Fluxo de dados

```
componente ──chama──▶ serviço de API ──HttpClient──▶ /api/...
    ▲                     │
    │                     └─ erro? mapApiError() → toApiError() → ApiError { kind, message }
    │
    └── template ◀── toLoadState(): { loading } → { ready, data } | { error, ApiError }
```

- As telas de leitura combinam os parâmetros (id da rota, página) com um `BehaviorSubject` de recarga e usam `switchMap` + `toLoadState()`. "Tentar novamente" é só emitir de novo nesse subject.
- O formulário de lançamento usa signals (`signal`, `computed`, `toSignal`) para o estado do envio e a prévia de saldo.
- O template trata os três estados (`loading`, `ready`, `error`) com o controle de fluxo `@if`, sempre da mesma forma nas três telas.

## Validação de formulários: `shared/input-error`

### O problema que resolve

Sem uma solução comum, cada formulário repete a mesma coisa em todo campo: verificar se está inválido, se o usuário já interagiu, qual erro é, qual texto mostrar e onde. Isso espalha regras de exibição pelos templates, gera mensagens inconsistentes e é fácil esquecer um campo.

Aqui, **o componente só importa uma diretiva e o template não tem nenhum markup de erro**: as mensagens aparecem sozinhas abaixo de cada campo.

### Estrutura

```
shared/input-error/
├── index.ts                                  API pública (importe sempre daqui)
├── input-error.component.ts                  renderiza a lista de mensagens de um campo
├── directive/
│   └── dynamic-validator-message.directive.ts   decide quando mostrar e insere o componente
├── pipe/
│   └── error-message.pipe.ts                 converte a chave do erro no texto
├── service/
│   └── error-state-matcher.service.ts        regra de QUANDO o erro fica visível
└── validator/
    ├── validation-error-messages.token.ts    mapa chave do erro → mensagem (InjectionToken)
    └── custom-validators.ts                  validadores do projeto (maxDecimals)
```

| Peça | Papel |
|---|---|
| `DynamicValidatorMessageDirective` | Anexa-se automaticamente a todo `formControlName`, `formControl`, `formGroupName`, `ngModel` e `ngModelGroup`. Observa o controle, pergunta ao matcher se o erro deve aparecer e, se sim, cria o `InputErrorComponent` logo após o campo e aplica a classe `field-invalid`. |
| `InputErrorComponent` (`app-input-error`) | Recebe os `ValidationErrors` do controle e mostra uma linha por erro, com a cor do tema e uma animação curta. O host tem `aria-live="polite"` para leitores de tela anunciarem a mensagem. |
| `ErrorMessagePipe` (`errorMessage`) | Busca no token a função da chave do erro e a chama com o valor do erro. Se faltar mensagem para um validador, avisa no console e mostra "Valor inválido.", em vez de quebrar a tela. |
| `ErrorStateMatcherService` | Critério padrão: o erro aparece quando o campo foi **alterado** (`dirty`) ou o formulário foi **enviado**. |
| `OnTouchedErrorStateMatcherService` | Critério alternativo: também quando o usuário **sai do campo** (`touched`) sem preencher. |
| `VALIDATION_ERROR_MESSAGES` | `InjectionToken` com as mensagens. Pode ser substituído por um provider (ex.: outro idioma). |
| `CustomValidators` | `maxDecimals(n)` (valores monetários). |

### Como usar

1. Importe a diretiva no componente:

```ts
import { CustomValidators, DynamicValidatorMessageDirective } from '../../shared/input-error';

@Component({
  imports: [ReactiveFormsModule, DynamicValidatorMessageDirective],
  // ...
})
export class MeuFormularioComponent {
  readonly form = inject(NonNullableFormBuilder).group({
    valor: [null as number | null, [Validators.required, Validators.min(0.01), CustomValidators.maxDecimals(2)]],
  });
}
```

2. Escreva o template normalmente, **sem nada de erro**:

```html
<form [formGroup]="form" (ngSubmit)="salvar()">
  <label for="valor">Valor</label>
  <p-inputnumber inputId="valor" formControlName="valor" mode="currency" currency="BRL" />
</form>
```

Ao enviar com o campo vazio, aparece "Campo obrigatório." abaixo dele, e o campo ganha a borda de erro.

3. (Opcional) Troque o critério de exibição só para esse componente:

```ts
@Component({
  providers: [{ provide: ErrorStateMatcherService, useClass: OnTouchedErrorStateMatcherService }],
})
```

É o que o formulário de lançamento faz: lá, sair de um campo obrigatório vazio já mostra o erro.

4. (Opcional) Desligue a diretiva num campo específico com o atributo `withoutFormValidation`:

```html
<input formControlName="observacao" withoutFormValidation />
```

### Como funciona por dentro

1. **Início no `ngAfterViewInit`.** O `FormControlName` só associa o controle no próprio `ngOnChanges`, e a ordem das diretivas num mesmo elemento não é garantida. No `AfterViewInit` o controle já existe, sem depender de `setTimeout` ou microtask.
2. **O que ela observa.** Os eventos do controle (`control.events`: valor, status, *touched*, *pristine*) e, do formulário raiz, só o envio (`FormSubmittedEvent`) e o reset (`FormResetEvent`). Assim ela reage a digitação, saída do campo, `markAllAsTouched()`, `setErrors()` (erros do servidor), envio e reset.
3. **A cada evento**, pergunta ao `ErrorStateMatcherService` se o erro deve estar visível:
   - **sim** e há erros: cria o `InputErrorComponent` (uma única vez) com `ViewContainerRef.createComponent`, atualiza os erros e aplica `field-invalid`;
   - **não**: destrói o componente; se o campo foi alterado e está válido, aplica `field-valid`; se voltou a ficar intocado (ex.: reset), remove as duas classes.
4. **Renderização imediata.** Depois de atualizar os erros, chama `detectChanges()` no componente criado. O evento que disparou a mudança (ex.: uma resposta HTTP) pode não passar pelo ciclo de um componente `OnPush`, e a mensagem precisa aparecer na hora.
5. **Limpeza.** A assinatura é encerrada com `takeUntilDestroyed`, e o componente de erro é destruído junto com o campo.

### Mensagens disponíveis

| Chave do erro | Origem | Mensagem |
|---|---|---|
| `required` | `Validators.required` | Campo obrigatório. |
| `min` / `max` | `Validators.min` / `max` | O valor mínimo/máximo é *N* (formatado em pt-BR, ex.: 0,01). |
| `minlength` / `maxlength` | `Validators.minLength` / `maxLength` | Informe no mínimo/máximo *N* caracteres. |
| `maxDecimals` | `CustomValidators.maxDecimals(n)` | Use no máximo *N* casas decimais. |
| `server` | Erro de validação vindo da API | A própria mensagem enviada pelo servidor. |

**Para adicionar um validador novo:** crie o validador (ex.: em `CustomValidators`) retornando `{ minhaChave: valor }` e acrescente `minhaChave: (valor) => '...'` em `ERROR_MESSAGES`. Nenhum formulário precisa mudar.

**Para trocar todos os textos** (ex.: inglês), forneça outro mapa:

```ts
providers: [{ provide: VALIDATION_ERROR_MESSAGES, useValue: { required: () => 'Required field.' /* ... */ } }]
```

### Erros de validação vindos da API

Quando a API responde 400 com erros por campo (`ProblemDetails.errors`), o `toApiError` normaliza as chaves para camelCase, e o formulário faz `control.setErrors({ server: mensagem })` no campo correspondente. A diretiva mostra essa mensagem pelo mesmo caminho das validações locais, então o usuário vê o erro do servidor exatamente no campo certo.

### Estilo

- A mensagem usa a cor `--accent-ink` do tema.
- O campo inválido recebe `field-invalid`, estilizado em `styles/_primeng.scss` para input nativo, `p-inputnumber`, `p-datepicker` e `p-select`.
- O `app-input-error` é inserido como **irmão** logo após o campo. Por ser criado dinamicamente, ele não recebe o atributo de encapsulamento do componente pai; se um layout precisar posicioná-lo (ex.: dentro de uma grid), use `:host ::ng-deep` restrito ao componente.

### Adaptações em relação à versão de referência

A arquitetura segue a estrutura original (diretiva, pipe, serviço, token e componente). Na adaptação corrigi:

| Original | Problema | Agora |
|---|---|---|
| `iif(() => !!this.form, this.form!.ngSubmit, EMPTY)` | O `iif` avalia os argumentos na hora: fora de um `<form>`, `this.form!` quebrava. | Escolha com `this.form ? ... : EMPTY` e eventos do formulário raiz. |
| Escuta de `statusChanges`, `blur` e `ngSubmit` | `blur` não sobe do `input` interno dos componentes PrimeNG; `markAllAsTouched()`, reset e erros do servidor não eram percebidos. | `control.events` + `FormSubmittedEvent`/`FormResetEvent`. |
| `queueMicrotask` no `ngOnInit` | Dependia do tempo de execução; frágil em testes. | `ngAfterViewInit`. |
| Assinatura nunca encerrada | Vazamento de memória a cada campo destruído. | `takeUntilDestroyed`. |
| Pipe chamava a função mesmo sem mensagem cadastrada | Aviso no console seguido de erro. | Mensagem genérica. |
| `track trackBy` | Rastreava a função, não o erro. | `track error.key`. |
| Classes do Tailwind | O projeto não usa Tailwind. | `field-invalid` / `field-valid` com as cores do tema. |

Também foi preciso cuidar de um efeito colateral: depois de um lançamento com sucesso, o formulário continuaria marcado como "enviado" e o valor vazio apareceria como erro. O componente usa `FormGroupDirective.resetForm()`, que zera o estado de envio e emite o `FormResetEvent` que a diretiva usa para limpar tudo.

### Limitações conhecidas

- A mensagem é anunciada por `aria-live`, mas não é associada ao campo por `aria-describedby`; um leitor de tela não a lê ao focar o campo depois.
- Todas as mensagens de um campo aparecem juntas (uma por validador que falhou), não só a primeira.
- A posição é sempre logo após o elemento do controle; layouts específicos precisam de CSS no componente.

### Testes da validação

- `input-error.component.spec.ts`: mensagem por validador, uso do valor do erro (ex.: `minlength`), nada quando válido, token substituível e mensagem genérica.
- `dynamic-validator-message.directive.spec.ts`: formulário recém-aberto sem erros; erro ao alterar; troca e remoção da mensagem; classes; todos os campos ao enviar; `withoutFormValidation`; limpeza no reset; erro do servidor; critério "ao sair do campo".

## Tema e estilos

A identidade visual é modernista: grade rígida, tipografia grande (Archivo), números em fonte monoespaçada com dígitos tabulares (IBM Plex Mono), traços grossos, cantos retos e um único acento vermelho reservado a débitos e erros. Crédito e débito também se distinguem por sinal e rótulo, não só pela cor.

Para aplicar outra marca, basta alterar dois arquivos: `styles/_tokens.scss` (variáveis CSS) e `theme/app-preset.ts` (preset do PrimeNG).

## Testes

Jasmine + Karma, 79 testes. Rodam em Chrome headless com `npm run test:ci`.

| Arquivo | O que cobre |
|---|---|
| `core/errors/api-error.spec.ts` | Cada status HTTP vira o `kind` certo; 429 com e sem `Retry-After`; erros por campo normalizados. |
| `core/api/api.services.spec.ts` | URLs, métodos, corpo do `POST`, parâmetros de paginação e propagação de erros (sem chamadas reais: `HttpTestingController`). |
| `core/idempotency/idempotency-key-tracker.spec.ts` | Mesma chave no reenvio dos mesmos dados; chave nova ao mudar os dados ou após confirmação. |
| `core/utils/uuid.spec.ts` | UUID válido, *fallback* sem `crypto.randomUUID` e rejeição de formatos inválidos. |
| `features/**/*.spec.ts` | Estados de carregamento, vazio e erro; renderização; paginação; validações; envio; reenvio confirmado (200 com `Idempotent-Replayed`); respostas 409, 422, 429, 400 e falha de rede; `eventId` nunca exibido. |
| `layout/layout.spec.ts` | Raiz só com o roteador, telas dentro do layout e navegação ativa. |
| `shared/input-error/**/*.spec.ts` | Ver [testes da validação](#testes-da-validação). |

Os testes de formulário dirigem os campos pelo modelo (`form.patchValue`) e verificam o DOM resultante. Testar cliques dentro dos componentes do PrimeNG testaria a biblioteca, não a lógica do projeto.
