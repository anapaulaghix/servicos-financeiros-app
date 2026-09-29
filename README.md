# Serviços Financeiros

Motor de um serviço financeiro que recebe eventos de crédito e débito e mantém o saldo consolidado de contas bancárias. Monorepo com:

- **`backend/`** — API REST em C# / ASP.NET Core (.NET 10, LTS), PostgreSQL via EF Core.
- **`frontend/servicos-financeiros/`** — aplicação web em Angular 19 com PrimeNG.

> **Status:** solução completa de ponta a ponta. O backend processa eventos com idempotência, consistência e transacionalidade (testado contra PostgreSQL real), expõe contas e extrato paginado, e o frontend permite listar contas, ver o extrato e lançar créditos e débitos. Tudo sobe com `docker compose up`. Veja a [seção de status](#status-e-próximos-passos) para o que ficou de fora.

## Stack

| Camada | Tecnologia |
|---|---|
| API | ASP.NET Core (.NET 10, LTS), Controllers, Swagger/OpenAPI (Swashbuckle) |
| Persistência | PostgreSQL, Entity Framework Core 10 (Npgsql) |
| Testes (backend) | xUnit, Moq, FluentAssertions, Testcontainers |
| Web | Angular 19, TypeScript (strict), RxJS, PrimeNG 19, SCSS |
| Testes (web) | Jasmine + Karma |
| Infra | Docker Compose: PostgreSQL, API (.NET) e nginx servindo o Angular |

## Arquitetura

O backend segue **Clean Architecture** com um domínio inspirado em DDD. As dependências apontam sempre para dentro:

```
        ┌──────────────────────────────────────────┐
        │  Api                                     │  controllers, contratos HTTP,
        │  (composition root)                      │  mapeamento de erros, Swagger
        └───────────────┬───────────────┬──────────┘
                        │               │
                        ▼               ▼
        ┌───────────────────┐   ┌──────────────────────┐
        │  Application      │◄──│  Infrastructure      │  EF Core, PostgreSQL,
        │  casos de uso +   │   │  implementa as       │  repositórios, unit of work
        │  portas (interf.) │   │  portas              │
        └─────────┬─────────┘   └──────────────────────┘
                  │
                  ▼
        ┌───────────────────┐
        │  Domain           │  entidades, regras de negócio,
        │  (sem dependências)│  exceções de domínio
        └───────────────────┘
```

| Projeto | Responsabilidade |
|---|---|
| `ServicosFinanceiros.Domain` | `Account` (agregado raiz), `Transaction`, `TransactionType` e exceções de domínio. Não depende de nenhum outro projeto nem de framework. |
| `ServicosFinanceiros.Application` | Casos de uso (`ProcessTransactionHandler`) e **portas** (`IAccountRepository`, `ITransactionRepository`, `IUnitOfWork`). Define *o quê* precisa existir, não *como*. |
| `ServicosFinanceiros.Infrastructure` | Adaptadores das portas: `AppDbContext`, mapeamentos, repositórios e `EfUnitOfWork`. É a única camada que conhece EF Core e Npgsql. |
| `ServicosFinanceiros.Api` | Borda HTTP: controllers, contratos de request/response, tradução de exceções em `ProblemDetails` e composição das dependências. |

### Inversão e injeção de dependência (IoC)

A Application declara as interfaces que precisa; a Infrastructure as implementa. A Api é a única que enxerga ambas e faz a ligação. **Cada camada expõe seu próprio registro**, e o `Program.cs` apenas os compõe:

```csharp
builder.Services
    .AddApplication()                          // casos de uso
    .AddInfrastructure(builder.Configuration); // DbContext, repositórios, unit of work
```

- [`Application/DependencyInjection.cs`](backend/src/ServicosFinanceiros.Application/DependencyInjection.cs) — registra `IProcessTransactionHandler`.
- [`Infrastructure/DependencyInjection.cs`](backend/src/ServicosFinanceiros.Infrastructure/DependencyInjection.cs) — registra `AppDbContext`, repositórios, `IUnitOfWork` e `TimeProvider`.

Todos os serviços são `Scoped` (uma instância por requisição), o que combina com o ciclo de vida do `DbContext`. Consequências práticas: o caso de uso é testável com dependências falsas (Moq), e trocar o banco ou o mecanismo de transação não toca em Application nem em Domain. O relógio é injetado via `TimeProvider`, o que permite testar com data fixa.

## Por que Controllers e não Minimal API

Escolhi **Controllers** (`[ApiController]`). Minimal APIs são uma boa opção, e no tamanho atual (um único endpoint) a diferença de desempenho é irrelevante. A decisão é sobre como o projeto cresce e quem vai mantê-lo:

- **Organização por recurso.** O escopo pede transações, contas e extrato. Um controller por recurso mantém rotas, contratos e documentação agrupados; em Minimal API isso exige disciplina extra (grupos e extensões) para não virar um `Program.cs` inflado.
- **Contrato declarativo.** `[ApiController]` devolve 400 com `ValidationProblemDetails` automaticamente quando o payload é inválido, e `[ProducesResponseType]` + comentários XML alimentam o Swagger com todos os códigos possíveis (201, 400, 404, 409, 422). O contrato fica visível no código e na documentação.
- **Convenção conhecida.** É o padrão mais comum em times .NET, o que reduz o custo de leitura e evolução por outras pessoas, um dos critérios do teste.
- **Extensibilidade.** Filtros, model binding e validação por atributos já vêm prontos para quando entrarem autenticação, versionamento ou rate limiting.

**Trade-off assumido:** Controllers têm mais cerimônia e um pipeline um pouco mais pesado que Minimal APIs. Aceito esse custo em troca de estrutura e previsibilidade. Se o serviço ficasse restrito a poucos endpoints simples, Minimal API seria igualmente defensável.

## Regras de negócio e onde cada uma é garantida

Evento de entrada (`POST /api/transactions`):

```json
{
  "eventId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "accountId": "7b895f64-5717-4562-b3fc-2c963f66afa7",
  "type": "CREDIT",
  "amount": 150.75,
  "occurredAt": "2026-01-30T10:15:00Z"
}
```

| Regra | Como é garantida |
|---|---|
| **Idempotência** | O `eventId` é a **chave primária** de `transactions`. O handler consulta antes (`ExistsAsync`) para responder rápido com 409 e, se duas requisições idênticas chegarem ao mesmo tempo, o banco rejeita a segunda (violação de unicidade), traduzida para o mesmo 409. A checagem prévia é otimização; a garantia real é a chave primária. |
| **Consistência** | `Account.Apply` é o único caminho que altera o saldo e rejeita débitos acima dele. Como reforço, o banco tem `CHECK (balance >= 0)` e `CHECK (balance_after >= 0)`. Cada lançamento guarda `balance_after`, então o saldo pode ser reconstruído a partir do histórico. |
| **Transacionalidade** | `EfUnitOfWork` abre uma transação, executa o caso de uso e faz um único `SaveChanges` seguido de commit: lançamento e saldo são gravados juntos ou nenhum é. |
| **Concorrência na mesma conta** | A conta é lida com `SELECT ... FOR UPDATE`, bloqueio pessimista de linha que serializa eventos da mesma conta até o fim da transação. Sem isso, dois débitos simultâneos poderiam ler o mesmo saldo e ambos passar. |
| **Backend como fonte da verdade** | Duplicidade e saldo insuficiente são decididos apenas na API; o frontend só as exibe. |

**Valores monetários** usam `decimal` (nunca `double`), com `numeric(18,2)` no banco e limite de 2 casas decimais validado no domínio.

### Endpoints

| Método e rota | Descrição |
|---|---|
| `POST /api/transactions` | Processa um evento de crédito ou débito. |
| `GET /api/accounts` | Lista as contas com o saldo atual consolidado. |
| `GET /api/accounts/{id}` | Consulta uma conta. |
| `GET /api/accounts/{id}/transactions?page=1&pageSize=10` | Extrato paginado (`pageSize` de 1 a 100), do mais recente para o mais antigo. Cada linha traz `signedAmount` e `balanceAfter`, que evidenciam o impacto no saldo. |

O lado de leitura usa uma porta própria (`IAccountQueries`) com projeções `AsNoTracking`, sem passar pelo agregado: consultas não precisam das regras de escrita. O extrato é ordenado pela **data de processamento**, que é a ordem em que o saldo realmente mudou; assim a cadeia de `balanceAfter` fica coerente mesmo quando um evento com `occurredAt` antigo chega tarde.

### Contrato de resposta (`POST /api/transactions`)

| Status | Situação |
|---|---|
| `201` | Evento processado; corpo traz o lançamento e o `balanceAfter`. |
| `400` | Payload inválido (`ValidationProblemDetails`) ou transação inválida. |
| `404` | Conta não encontrada. |
| `409` | `eventId` já processado. |
| `422` | Saldo insuficiente. |

Erros usam `ProblemDetails` (RFC 9457). A tradução de exceções de domínio para HTTP fica em [`DomainExceptionHandler`](backend/src/ServicosFinanceiros.Api/ExceptionHandling/DomainExceptionHandler.cs), então controllers e casos de uso não têm `try/catch` de negócio.

## Modelo de dados

```
accounts                         transactions
─────────                        ────────────
id            uuid  PK           event_id      uuid  PK   ← idempotência
holder_name   varchar(200)       account_id    uuid  FK → accounts.id
balance       numeric(18,2)      type          varchar(10)   'Credit' | 'Debit'
created_at    timestamptz        amount        numeric(18,2)  CHECK > 0
CHECK balance >= 0               balance_after numeric(18,2)  CHECK >= 0
                                 occurred_at   timestamptz
                                 processed_at  timestamptz
                                 INDEX (account_id, processed_at DESC)
```

O índice composto sustenta o extrato paginado de uma conta, do lançamento mais recente para o mais antigo.

## Como rodar

### Pré-requisitos

- [Docker](https://www.docker.com/products/docker-desktop/) com Docker Compose. **Não é preciso instalar o PostgreSQL**: ele roda em container.
- [.NET SDK 10](https://dotnet.microsoft.com/download) para rodar/testar o backend fora do Docker (o `global.json` fixa a versão).
- Node.js 20+ e npm para o frontend.

### Com Docker Compose (PostgreSQL + API)

```bash
cp .env.example .env      # defina uma senha em POSTGRES_PASSWORD
docker compose up --build
```

A API sobe em `http://localhost:8080` (Swagger em `/swagger`), aplica as migrations e cria 3 contas de demonstração (`SEED_DEMO_DATA=true`). O compose se recusa a subir se `POSTGRES_USER` ou `POSTGRES_PASSWORD` não estiverem definidos.

Exemplo, usando uma conta de demonstração:

```bash
curl -X POST http://localhost:8080/api/transactions \
  -H "Content-Type: application/json" \
  -d '{"eventId":"'$(uuidgen)'","accountId":"11111111-1111-1111-1111-111111111111","type":"CREDIT","amount":150.75,"occurredAt":"2026-01-30T10:15:00Z"}'
```

Repetir o mesmo `eventId` devolve `409`; um débito acima do saldo devolve `422`.

Depois de subir:

| Serviço | Endereço |
|---|---|
| Aplicação web | http://localhost:4200 |
| API + Swagger | http://localhost:8080/swagger |
| PostgreSQL | `127.0.0.1:5432` (usuário e senha do `.env`) |

O `web` é o Angular compilado e servido pelo nginx (sem root), que também encaminha `/api` para a API. Assim o navegador fala com um único host: sem CORS e sem URL de API por ambiente.

### Segredos e configuração

- Credenciais ficam no `.env`, que **não é versionado** (`.gitignore`). Só o `.env.example`, com valores de exemplo, vai para o repositório.
- O PostgreSQL é publicado apenas em `127.0.0.1:5432`, então não fica acessível a outras máquinas da rede.
- A API roda no container com usuário sem privilégios (não é root), e nenhuma senha está no código nem nos `appsettings`.
- A connection string chega à API pela variável `ConnectionStrings__Postgres`. Para rodar a API fora do Docker, use *user secrets*:

```bash
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Database=servicos_financeiros;Username=app_user;Password=<sua-senha>" \
  --project backend/src/ServicosFinanceiros.Api
dotnet run --project backend/src/ServicosFinanceiros.Api   # com Database__MigrateOnStartup=true para aplicar as migrations
```

### Migrations

Ficam em `backend/src/ServicosFinanceiros.Infrastructure/Persistence/Migrations`. A ferramenta `dotnet-ef` está fixada em `dotnet-tools.json`:

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Nome> -p src/ServicosFinanceiros.Infrastructure -s src/ServicosFinanceiros.Infrastructure -o Persistence/Migrations
```

Gerar migrations não abre conexão com o banco (usa um `IDesignTimeDbContextFactory`). Nos ambientes Docker/desenvolvimento a API as aplica na inicialização (`Database__MigrateOnStartup`); em produção o ideal é aplicá-las como etapa separada do deploy.

### Testes do backend

```bash
dotnet test backend/ServicosFinanceiros.sln
```

Os **testes de integração** usam [Testcontainers](https://testcontainers.com/): sobem um PostgreSQL descartável, então **o Docker precisa estar rodando**. Para rodar só os unitários (sem Docker): `dotnet test backend/tests/ServicosFinanceiros.UnitTests`.

### Frontend

```bash
cd frontend/servicos-financeiros
npm install
npm start          # http://localhost:4200, com proxy de /api para localhost:8080 (proxy.conf.json)
npm run test:ci    # Jasmine + Karma em Chrome headless (precisa do Chrome instalado)
```

Para desenvolver, deixe a API no ar (`docker compose up -d db api`) e rode `npm start`.

## Frontend

### Organização

```
src/app/
  app.component.ts   só o <router-outlet />
  layout/            main-layout (estrutura das telas) e sidebar (navegação)
  core/              modelos tipados, serviços de API, tradução de erros, estado de carga, utilitários
  features/          accounts (lista), statement (extrato), new-transaction (formulário)
  shared/            input-error (validação de formulários), page-header, loading, error-panel, pipes
  theme/             preset do PrimeNG e traduções pt-BR
```

Componentes standalone, `OnPush`, rotas com *lazy loading* e `withComponentInputBinding` (parâmetros de rota chegam como `input()`). Removi o SSR que o `ng new` gera: é um painel sem SEO, e o SSR só acrescentaria um servidor Node ao Docker.

**`AppComponent` vazio.** A raiz só hospeda o roteador. O layout é um componente próprio (`MainLayoutComponent`), usado como rota pai das telas. Assim o layout pode variar por área (ex.: uma tela de login sem barra lateral seria só outra rota pai), é testável isoladamente e a raiz não acumula responsabilidades.

### Validação de formulários (`shared/input-error`)

As mensagens de validação são exibidas automaticamente: o componente importa a `DynamicValidatorMessageDirective` e **não escreve nenhum markup de erro no template**.

```
shared/input-error/
  directive/   DynamicValidatorMessageDirective: ativa em todo formControlName/formControl/ngModel
  pipe/        ErrorMessagePipe: chave do erro -> texto
  service/     ErrorStateMatcherService: decide QUANDO o erro aparece (há a variante OnTouched)
  validator/   VALIDATION_ERROR_MESSAGES (token com as mensagens) e CustomValidators (maxDecimals, uuid)
  input-error.component.ts   renderiza as mensagens
```

- A diretiva escuta `control.events` (valor, status, touched, pristine) e os eventos de envio e reset do formulário. Quando o critério do `ErrorStateMatcher` é atendido, cria o `InputErrorComponent` logo após o campo e aplica a classe `field-invalid`.
- **Mensagens centralizadas** num `InjectionToken`: um validador novo é uma linha no mapa, e dá para trocar os textos (ex.: outro idioma) sem tocar nos formulários.
- **Critério de exibição injetável**: o padrão é "alterado ou enviado"; o formulário de lançamento usa `OnTouchedErrorStateMatcherService` (também ao sair do campo) só com um `provider`.
- **Erros da API usam o mesmo caminho**: um 400 com erro por campo vira `setErrors({ server })` e aparece no campo certo.
- `withoutFormValidation` desliga a diretiva num campo específico.

### Decisões

- **PrimeNG como biblioteca de componentes** (tabela paginada, select, seletor de tipo, campo monetário, calendário, mensagens), tematizada por um preset próprio (`theme/app-preset.ts`) e tokens em `styles/_tokens.scss`. A identidade visual é modernista: grade rígida, tipografia grande, traços firmes, cantos retos e um único acento. Para adaptar a outra marca, basta trocar esses dois arquivos.
- **RxJS + operador `toLoadState`.** Toda carga vira um fluxo `loading → ready | error`, então cada tela trata os três estados da mesma forma e uma falha nunca quebra o fluxo. O "tentar novamente" apenas emite de novo.
- **Erros da API traduzidos em um lugar.** `toApiError` converte HTTP/`ProblemDetails` em um `ApiError` com `kind` (`network`, `validation`, `duplicate`, `insufficient-funds`, `not-found`, `server`). Os componentes só conhecem o `kind`, nunca códigos HTTP.
- **Idempotência por baixo dos panos.** O `eventId` é detalhe técnico e não aparece na tela. O `IdempotencyKeyTracker` (`core/idempotency`) gera a chave no envio e a **reutiliza enquanto o usuário reenvia exatamente os mesmos dados de uma tentativa sem confirmação** (rede caiu, erro 5xx): se a primeira chegou ao servidor, a segunda é reconhecida como duplicada e nada é lançado duas vezes. Se o usuário altera algum dado, ou o servidor confirma (sucesso ou 409), a próxima operação recebe chave nova. Um 409 é mostrado como "Lançamento já registrado" e os saldos são recarregados. `crypto.randomUUID` tem *fallback*, porque só existe em contextos seguros.
- **O backend continua sendo a fonte da verdade.** O formulário valida (obrigatórios, valor maior que zero, no máximo 2 casas, UUID) e mostra uma prévia do saldo, inclusive um aviso quando o débito excede o saldo, mas **não bloqueia** o envio: quem recusa é o servidor. Os saldos exibidos são recarregados da API depois de cada lançamento.
- **Extrato sem piscar.** A tabela mantém a página anterior enquanto a próxima carrega, em vez de sumir e reaparecer.
- **Mobile.** A barra lateral vira cabeçalho; no extrato ficam só data, valor e saldo, porque o sinal e a cor já indicam crédito ou débito.

## Testes

Os testes de backend priorizam os cenários críticos do problema, não cobertura percentual:

- **Domínio (`AccountTests`)**: crédito, débito, débito igual ao saldo, saldo insuficiente sem alterar o estado, valores não positivos e com mais de 2 casas, e o saldo final igual à soma dos lançamentos.
- **Aplicação (`ProcessTransactionHandlerTests`)**: evento duplicado não altera a conta, violação de unicidade concorrente vira "duplicado", saldo insuficiente não persiste nada, conta inexistente e execução dentro do unit of work.

- **Integração (`ProcessTransactionIntegrationTests`, PostgreSQL real via Testcontainers)**: crédito grava lançamento e saldo juntos; o mesmo evento enviado duas vezes conta uma única vez; débito acima do saldo não deixa rastro; **10 eventos idênticos em paralelo processam exatamente 1**; **10 débitos concorrentes de 20 numa conta de 100 permitem só 5 e o saldo nunca fica negativo**; o saldo final é igual à soma do histórico após atividade concorrente; e uma falha ao gravar desfaz a atualização do saldo (atomicidade).

- **Consultas (`AccountQueriesIntegrationTests`)**: listagem com saldo atual, extrato do mais recente para o mais antigo com o impacto no saldo, paginação sem repetir nem pular itens, página além do fim e conta inexistente.

Validei que os testes de concorrência detectam o problema de verdade: removendo o `FOR UPDATE` do repositório, dois deles falham.

**Testes do frontend (68):**

- **Validação (`shared/input-error`)**: mensagens por validador, token substituível e mensagem genérica para validador sem texto; a diretiva não mostra erro em formulário recém-aberto, mostra ao alterar e ao enviar, troca e remove a mensagem, aplica as classes, respeita `withoutFormValidation`, limpa tudo no reset, exibe erros do servidor e funciona com o critério "ao sair do campo".
- **Layout**: `AppComponent` só com o roteador, telas renderizadas dentro do layout e navegação com o item ativo.

- **Tradução de erros e serviços de API**: cada status HTTP vira o `kind` certo, o contrato do `POST`, os parâmetros de paginação e a propagação de erros.
- **Formulário de lançamento**: validações exibidas sob cada campo (vazio, valor zero, mais de 2 casas), prévia de saldo e aviso de débito acima do saldo, envio com o contrato da API e `eventId` gerado internamente (sem aparecer na tela), limpeza após o sucesso sem acusar o campo vazio como erro, bloqueio de envio duplo e as respostas 409, 422, falha de comunicação (o reenvio dos mesmos dados usa o mesmo `eventId`; dados alterados usam outro) e 400 com erro no campo.
- **Telas de dados**: estados de carregamento, vazio e erro com "tentar novamente"; renderização das contas e do extrato com valores e sinais; troca de página pedindo a página certa à API; e a tabela mantida visível durante a troca.

## Decisões e trade-offs

- **Bloqueio pessimista (`FOR UPDATE`) em vez de concorrência otimista.** Numa conta com muitos eventos simultâneos, o otimista geraria conflitos e retentativas; o pessimista simplesmente enfileira. O custo é a contenção na mesma conta, aceitável porque contas distintas não se bloqueiam.
- **Idempotência pela chave primária.** Simples e à prova de corrida, ao custo de acoplar o `eventId` à tabela de lançamentos. Se um mesmo `eventId` pudesse valer em várias contas, seria preciso uma chave composta.
- **`Account.Apply` devolve o lançamento em vez de o domínio conhecer o repositório.** Mantém o domínio puro; a checagem de duplicidade, que depende do histórico armazenado, fica na Application.
- **Exceções de domínio para regras violadas.** Deixam o fluxo feliz limpo e são traduzidas em um só lugar. Em caminhos de altíssimo volume, um `Result<T>` evitaria o custo de exceções.
- **Processamento síncrono.** O evento é processado dentro da requisição. Uma fila (RabbitMQ) desacoplaria a entrada do processamento, mas introduziria consistência eventual na tela; fica como evolução possível.

## Status e próximos passos

| Item | Situação |
|---|---|
| Domínio (`Account`, `Transaction`, regras) | Feito, com testes |
| Caso de uso `ProcessTransaction` (idempotência, unit of work) | Feito, com testes |
| Persistência: `DbContext`, mapeamentos, repositórios, unit of work | Feito, com testes de integração |
| Migrations do EF Core | Feito (`InitialCreate`) |
| `POST /api/transactions` + Swagger + `ProblemDetails` | Feito |
| Docker Compose (PostgreSQL + API) com migrations e seed na subida | Feito |
| Testes de integração com PostgreSQL (Testcontainers) | Feito |
| Endpoints de leitura: listar contas, extrato paginado | Feito, com testes de integração |
| Frontend Angular: contas, extrato paginado e formulário de lançamento | Feito, com 68 testes |
| Serviço `web` (nginx + Angular) no Compose | Feito |
| Criação de contas pela API/tela | Não feito (as contas de demonstração vêm do seed) |
| Autenticação (Keycloak), mensageria (RabbitMQ), cache (Redis), observabilidade | Não feito (diferenciais opcionais) |
| Testes end-to-end (navegador automatizado) | Não feito |
