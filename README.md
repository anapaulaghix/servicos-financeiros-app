# Serviços Financeiros

[![CI](https://github.com/anapaulaghix/servicos-financeiros-app/actions/workflows/ci.yml/badge.svg)](https://github.com/anapaulaghix/servicos-financeiros-app/actions/workflows/ci.yml)

## Em 1 minuto

- **O que é:** API em .NET 10 que processa créditos e débitos em contas e uma aplicação Angular para consultar saldos, ver o extrato e lançar valores.
- **Como rodar:** `cp .env.example .env`, defina as senhas e rode `docker compose up --build`. A aplicação abre em http://localhost:4200 e o Swagger em http://localhost:8080/swagger.
- **Regras garantidas:** um evento nunca é processado duas vezes (chave primária no `eventId`), o saldo nunca fica negativo, lançamento e saldo são gravados na mesma transação e débitos simultâneos na mesma conta são enfileirados (`SELECT ... FOR UPDATE`). [Detalhes](#regras-de-negócio-e-onde-cada-uma-é-garantida).
- **Testes:** 61 no backend (incluindo concorrência e o contrato HTTP contra PostgreSQL e Redis reais) e 76 no frontend, rodando na CI a cada push. Também rodam pelo Docker, sem instalar nada: `docker compose --profile test run --rm test-backend` e `... test-frontend`.
- **Diferenciais feitos:** health checks, logs estruturados (Serilog + Elasticsearch) e rate limiting com Redis. NgRx, Keycloak e RabbitMQ estão desenhados em [Melhorias futuras](#melhorias-futuras).
- **Onde estão as decisões:** [Decisões e trade-offs](#decisões-e-trade-offs) e [Uso de IA no desenvolvimento](#uso-de-ia-no-desenvolvimento).

## Visão geral

Motor de um serviço financeiro que recebe eventos de crédito e débito e mantém o saldo consolidado de contas bancárias. Monorepo com:

- **`backend/`** — API REST em C# / ASP.NET Core (.NET 10, LTS), PostgreSQL via EF Core.
- **`frontend/servicos-financeiros/`** — aplicação web em Angular 19 com PrimeNG.

Cada projeto tem um README próprio com a estrutura de pastas e como rodá-lo isoladamente: [backend](backend/README.md) e [frontend](frontend/servicos-financeiros/README.md).

> **Status:** solução completa de ponta a ponta. O backend processa eventos com idempotência, consistência e transacionalidade (testado contra PostgreSQL real), expõe contas e extrato paginado, e o frontend permite listar contas, ver o extrato e lançar créditos e débitos. Tudo sobe com `docker compose up`. Veja a [seção de status](#status-e-próximos-passos) para o que ficou de fora.

> **Uso de IA:** o código foi escrito majoritariamente com um agente de IA (Claude Code); as decisões de arquitetura, escopo e produto foram minhas. Detalho o processo e quais decisões tomei em [Uso de IA no desenvolvimento](#uso-de-ia-no-desenvolvimento).

## Stack

| Camada | Tecnologia |
|---|---|
| API | ASP.NET Core (.NET 10, LTS), Controllers, Swagger/OpenAPI (Swashbuckle) |
| Persistência | PostgreSQL, Entity Framework Core 10 (Npgsql) |
| Testes (backend) | xUnit, Moq, FluentAssertions, Testcontainers (PostgreSQL e Redis), WebApplicationFactory |
| Observabilidade | Serilog (logs estruturados), Elasticsearch + Kibana (opcional), health checks |
| Rate limiting | Redis (janela fixa, compartilhada entre instâncias) |
| Web | Angular 19, TypeScript (strict), RxJS, PrimeNG 19, SCSS |
| Testes (web) | Jasmine + Karma |
| Infra | Docker Compose: PostgreSQL, Redis, API (.NET), nginx servindo o Angular; Elasticsearch e Kibana num profile opcional |

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
| `GET /api/transactions/{eventId}` | Consulta um lançamento (é o endereço do cabeçalho `Location` do 201). |
| `GET /api/accounts` | Lista as contas com o saldo atual consolidado. |
| `GET /api/accounts/{id}` | Consulta uma conta. |
| `GET /api/accounts/{id}/transactions?page=1&pageSize=10` | Extrato paginado (`pageSize` de 1 a 100), do mais recente para o mais antigo. Cada linha traz `signedAmount` e `balanceAfter`, que evidenciam o impacto no saldo. |

O lado de leitura usa uma porta própria (`IAccountQueries`) com projeções `AsNoTracking`, sem passar pelo agregado: consultas não precisam das regras de escrita. O extrato é ordenado pela **data de processamento**, que é a ordem em que o saldo realmente mudou; assim a cadeia de `balanceAfter` fica coerente mesmo quando um evento com `occurredAt` antigo chega tarde.

### Contrato de resposta (`POST /api/transactions`)

| Status | Situação |
|---|---|
| `201` | Evento processado; corpo traz o lançamento e o `balanceAfter`; `Location` aponta para o lançamento. |
| `400` | Campo ausente, formato inválido (ex.: `type` que não seja `"CREDIT"`/`"DEBIT"`) ou valor fora do permitido. Mensagens em português, com o campo em `errors`. |
| `404` | Conta não encontrada. |
| `409` | `eventId` já processado. |
| `422` | Saldo insuficiente. |
| `429` | Limite de requisições excedido (ver rate limiting). |

**Regras do contrato:** todos os campos são obrigatórios e validados antes de qualquer acesso ao banco. `occurredAt` aceita qualquer fuso ISO-8601 (ex.: `-03:00`) e é armazenado em UTC. O `type` só é aceito como texto. Um crédito que faria o saldo passar do limite de `numeric(18,2)` é recusado com 400. Os campos do contrato são anuláveis de propósito: em tipos de valor, o `[Required]` não detecta um campo ausente, que viraria o valor padrão (ex.: data `0001-01-01`).

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

### 1. Crie o seu `.env`

As credenciais não ficam no repositório: cada pessoa cria o próprio `.env` a partir do modelo, na raiz do projeto. O `.env` está no `.gitignore` e nunca deve ser commitado.

```bash
cp .env.example .env
```

Depois, abra o `.env` e troque os valores de exemplo:

| Variável | Obrigatória | Para que serve |
|---|---|---|
| `POSTGRES_USER` | Sim | Usuário do PostgreSQL |
| `POSTGRES_PASSWORD` | Sim | Senha do PostgreSQL |
| `POSTGRES_DB` | Não (padrão `servicos_financeiros`) | Nome do banco |
| `REDIS_PASSWORD` | Sim | Senha do Redis, usado pelo rate limiting |
| `SEED_DEMO_DATA` | Não (padrão `true`) | Cria 3 contas de demonstração na primeira subida |
| `COMPOSE_PROFILES` e `ELASTICSEARCH_URL` | Não | Ligam o Elasticsearch e o Kibana (ver abaixo) |

Se uma variável obrigatória faltar, o `docker compose up` não sobe e informa qual é. Nas senhas, evite `;`, `,` e `"`, porque elas entram em connection strings. Para gerar uma senha aleatória:

```bash
openssl rand -hex 16
```

No PowerShell, sem OpenSSL:

```powershell
-join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ })
```

As senhas só são lidas **na primeira criação** dos volumes. Se você trocar `POSTGRES_PASSWORD` depois que o banco já existe, recrie-o (isso apaga os dados locais):

```bash
docker compose down -v
```

### 2. Suba a aplicação

```bash
docker compose up --build
```

A API sobe em `http://localhost:8080` (Swagger em `/swagger`), aplica as migrations e cria 3 contas de demonstração (`SEED_DEMO_DATA=true`). O compose se recusa a subir se `POSTGRES_USER`, `POSTGRES_PASSWORD` ou `REDIS_PASSWORD` não estiverem definidos.

Para subir também o **Elasticsearch e o Kibana** (cerca de 1,5 GB de RAM a mais), descomente no `.env` as linhas `COMPOSE_PROFILES=observability` e `ELASTICSEARCH_URL=http://elasticsearch:9200`, ou passe-as no comando:

```bash
COMPOSE_PROFILES=observability ELASTICSEARCH_URL=http://elasticsearch:9200 docker compose up --build
```

Exemplo, usando uma conta de demonstração:

```bash
curl -X POST http://localhost:8080/api/transactions \
  -H "Content-Type: application/json" \
  -d '{"eventId":"'$(uuidgen)'","accountId":"11111111-1111-1111-1111-111111111111","type":"CREDIT","amount":150.75,"occurredAt":"2026-01-30T10:15:00Z"}'
```

Repetir o mesmo `eventId` devolve `409`; um débito acima do saldo devolve `422`.

### Como ver cada resposta

Na tela, a idempotência é automática e o identificador do evento fica escondido, então o 409 só aparece se a rede cair depois de o servidor processar o lançamento. Para conferir cada caso, use o Swagger (http://localhost:8080/swagger) ou a tela:

| Resposta | Como provocar |
|---|---|
| `201` | Tela **Novo lançamento**, ou `POST` no Swagger com uma conta de demonstração (ex.: `11111111-1111-1111-1111-111111111111`). |
| `409` duplicado | No Swagger, envie **duas vezes o mesmo corpo** (mesmo `eventId`). A segunda resposta é 409 e o saldo não muda. |
| `422` saldo insuficiente | Na tela, um débito maior que o saldo (a tela avisa antes, mas deixa enviar: quem decide é a API). |
| `400` | No Swagger, remova um campo ou envie `"type": 2`. |
| `404` | No Swagger, use um `accountId` que não existe. |
| `429` | Envie mais de 20 lançamentos em 10 segundos (ex.: repetindo o envio no Swagger). |

Depois de subir:

| Serviço | Endereço |
|---|---|
| Aplicação web | http://localhost:4200 |
| API + Swagger | http://localhost:8080/swagger |
| Health checks | http://localhost:8080/health/live e http://localhost:8080/health/ready |
| PostgreSQL | `127.0.0.1:5432` (usuário e senha do `.env`) |
| Kibana (profile `observability`) | http://localhost:5601 |
| Elasticsearch (profile `observability`) | http://localhost:9200 |

O Redis não é publicado: só a API o acessa, pela rede interna do Compose. A API é publicada só em `127.0.0.1`: fora desta máquina, o acesso passa pelo `web`.

O `web` é o Angular compilado e servido pelo nginx (sem root), que também encaminha `/api` para a API. Assim o navegador fala com um único host: sem CORS e sem URL de API por ambiente.

### Segredos e configuração

- Credenciais ficam no `.env`, que **não é versionado** (`.gitignore`). Só o `.env.example`, com valores de exemplo, vai para o repositório.
- O PostgreSQL é publicado apenas em `127.0.0.1:5432`, então não fica acessível a outras máquinas da rede.
- A API roda no container com usuário sem privilégios (não é root), e nenhuma senha está no código nem nos `appsettings`.
- O Redis exige senha (`REDIS_PASSWORD`) e não tem porta publicada.
- No Elasticsearch/Kibana a segurança está **desligada**, por ser um ambiente local de demonstração, e as portas ficam restritas a `127.0.0.1`. Em produção: TLS, usuários e API keys.
- A connection string chega à API pela variável `ConnectionStrings__Postgres`. Para rodar a API fora do Docker (ex.: depurar pela IDE), ela vem de *user secrets*, que ficam fora do repositório; o passo a passo está no [README do backend](backend/README.md#opção-2-api-local-banco-no-docker).

### Migrations

Ficam em `backend/src/ServicosFinanceiros.Infrastructure/Persistence/Migrations`. A ferramenta `dotnet-ef` está fixada em `dotnet-tools.json`:

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Nome> -p src/ServicosFinanceiros.Infrastructure -s src/ServicosFinanceiros.Infrastructure -o Persistence/Migrations
```

Gerar migrations não abre conexão com o banco (usa um `IDesignTimeDbContextFactory`). Nos ambientes Docker/desenvolvimento a API as aplica na inicialização (`Database__MigrateOnStartup`); em produção o ideal é aplicá-las como etapa separada do deploy.

### Integração contínua

A CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) roda a cada push e pull request na `main`, em três jobs:

| Job | O que faz |
|---|---|
| **Backend** | `dotnet build` em Release com os analisadores do .NET e as regras do `.editorconfig` (na CI, qualquer aviso quebra o build) e `dotnet test`, incluindo os testes de integração (o runner do GitHub já tem Docker para o Testcontainers). |
| **Frontend** | `npm ci`, ESLint, verificação de formatação (Prettier), testes em Chrome headless e build de produção. |
| **Docker** | Valida o `docker-compose.yml` e constrói as imagens da API e do frontend. Roda só se os dois anteriores passarem. |

### Testes pelo Docker (sem instalar .NET, Node ou Chrome)

```bash
docker compose --profile test run --rm test-backend
docker compose --profile test run --rm test-frontend
```

O `test-backend` roda os testes unitários e de integração numa imagem do SDK .NET. Os testes de integração sobem PostgreSQL e Redis com Testcontainers, usando o Docker da máquina pelo socket montado; os containers criados por eles são descartados ao final. O `test-frontend` roda o Karma com Chromium dentro do container.

### Testes do backend (com o .NET SDK instalado)

```bash
dotnet test backend/ServicosFinanceiros.sln
```

Os **testes de integração** usam [Testcontainers](https://testcontainers.com/): sobem um PostgreSQL e um Redis descartáveis, então **o Docker precisa estar rodando**. Para rodar só os unitários (sem Docker): `dotnet test backend/tests/ServicosFinanceiros.UnitTests`.

### Frontend (com Node instalado)

```bash
cd frontend/servicos-financeiros
npm install
npm start          # http://localhost:4200, com proxy de /api para localhost:8080 (proxy.conf.json)
npm run test:ci    # Jasmine + Karma em Chrome headless (precisa do Chrome instalado)
npm run lint       # ESLint
npm run format     # Prettier
```

Para desenvolver, deixe a API no ar (`docker compose up -d db api`) e rode `npm start`.

## Observabilidade e proteção da API

### Health checks

| Endpoint | O que verifica | Para que serve |
|---|---|---|
| `GET /health/live` | Só se o processo responde (nenhuma dependência) | *Liveness*: se falhar, o orquestrador reinicia a instância |
| `GET /health/ready` | PostgreSQL (`AddDbContextCheck`) e Redis (`PING`) | *Readiness*: se falhar, a instância sai do balanceamento sem ser reiniciada |

Separar os dois evita reiniciar a API só porque o banco oscilou: reiniciar não conserta o banco. O Redis fora do ar deixa o readiness como **Degraded**, e não **Unhealthy**, porque a API continua processando lançamentos sem ele (ver rate limiting). A resposta é um JSON com o status e a duração de cada verificação. Os checks são registrados pela Infrastructure (que conhece as dependências) e mapeados pela Api.

### Logs estruturados (Serilog)

- **Eventos com propriedades, não texto solto.** Ex.: `Lançamento {EventId} processado: {TransactionType} de {Amount} na conta {AccountId}...`. No Elasticsearch, `EventId`, `AccountId`, `Rejection`, `StatusCode` etc. viram campos pesquisáveis e agregáveis ("quantos saldos insuficientes na última hora?").
- **Console:** texto legível em desenvolvimento; em container, **JSON de uma linha por evento**, pronto para qualquer coletor.
- **Elasticsearch:** quando `Elasticsearch:Url` está configurado, os eventos vão para o data stream `logs-servicos_financeiros-api` (sink oficial da Elastic). Se o Elasticsearch estiver fora do ar, a API sobe normalmente e o console continua recebendo tudo.
- **Correlação:** cada evento carrega `TraceId` e `SpanId` da requisição, então todos os logs de uma chamada podem ser filtrados juntos.
- **O que é logado:** uma linha por requisição HTTP (método, rota, status, duração, IP), cada lançamento processado (só depois do commit, para o log nunca afirmar algo que não foi gravado) e cada recusa de regra de negócio como `Information`, já que recusar é comportamento esperado, não falha. Health checks ficam fora para não poluir.

Para ver no Kibana: **Discover → Create data view**, com o padrão `logs-servicos_financeiros-*`.

### Rate limiting com Redis

- O `POST /api/transactions` aceita **20 requisições a cada 10 segundos por cliente** (configurável em `RateLimiting:Transactions`). Acima disso a API responde **429** com `ProblemDetails` e o cabeçalho `Retry-After`; o front mostra quanto tempo esperar. As leituras não são limitadas.
- **Por que Redis e não o rate limiter em memória do ASP.NET:** em memória, cada instância da API teria o próprio contador, e duas instâncias dobrariam o limite. No Redis o contador é único.
- **Atômico:** `INCR` e `PEXPIRE` rodam num único script Lua, então requisições simultâneas nunca furam o limite (há teste com 50 requisições paralelas contra um limite de 10).
- **Fail-open:** se o Redis cair, a API **permite** as requisições e registra um aviso, em vez de bloquear lançamentos financeiros por causa de uma proteção auxiliar. O problema aparece no readiness como `Degraded`.
- **IP real do cliente:** a API fica atrás do nginx, então o IP vem de `X-Forwarded-For`. Só o nginx é aceito como proxy: ele tem um IP fixo na rede do Compose, e a API confia apenas nesse IP (`ReverseProxy:TrustedProxies`). Quem chama a API direto não consegue forjar o próprio IP trocando o cabeçalho a cada requisição (há teste para isso).
- **Por que não cache:** o enunciado oferece "caching **ou** rate limiting". Cachear saldos arriscaria mostrar um valor desatualizado, contrariando a regra de que a interface reflete fielmente o backend.
- Sem Redis configurado (ex.: rodar a API pelo `dotnet run`), o rate limiting fica desligado.

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
- **O backend continua sendo a fonte da verdade.** O formulário valida (obrigatórios, valor maior que zero, no máximo 2 casas) e mostra uma prévia do saldo, inclusive um aviso quando o débito excede o saldo, mas **não bloqueia** o envio: quem recusa é o servidor. Os saldos exibidos são recarregados da API depois de cada lançamento.
- **Extrato sem piscar.** A tabela mantém a página anterior enquanto a próxima carrega, em vez de sumir e reaparecer.
- **Mobile.** A barra lateral vira cabeçalho; no extrato ficam só data, valor e saldo, porque o sinal e a cor já indicam crédito ou débito.

## Testes

Os testes de backend priorizam os cenários críticos do problema, não cobertura percentual:

- **Domínio (`AccountTests`)**: crédito, débito, débito igual ao saldo, saldo insuficiente sem alterar o estado, valores não positivos e com mais de 2 casas, e o saldo final igual à soma dos lançamentos.
- **Aplicação (`ProcessTransactionHandlerTests`)**: evento duplicado não altera a conta, violação de unicidade concorrente vira "duplicado", saldo insuficiente não persiste nada, conta inexistente e execução dentro do unit of work.

- **Integração (`ProcessTransactionIntegrationTests`, PostgreSQL real via Testcontainers)**: crédito grava lançamento e saldo juntos; o mesmo evento enviado duas vezes conta uma única vez; débito acima do saldo não deixa rastro; **10 eventos idênticos em paralelo processam exatamente 1**; **10 débitos concorrentes de 20 numa conta de 100 permitem só 5 e o saldo nunca fica negativo**; o saldo final é igual à soma do histórico após atividade concorrente; e uma falha ao gravar desfaz a atualização do saldo (atomicidade).

- **Consultas (`AccountQueriesIntegrationTests`)**: listagem com saldo atual, extrato do mais recente para o mais antigo com o impacto no saldo, paginação sem repetir nem pular itens, página além do fim e conta inexistente.

- **Rate limiting (`RedisRateLimiterIntegrationTests`, Redis real)**: libera até o limite e bloqueia com `Retry-After`; cada cliente tem o próprio limite; a janela expira e libera; 50 requisições paralelas contra limite 10 liberam exatamente 10; Redis inacessível permite a requisição e deixa o readiness `Degraded`; sem Redis configurado, nada é limitado.
- **API ponta a ponta (`ApiIntegrationTests`, `WebApplicationFactory` com PostgreSQL e Redis reais)**: liveness e readiness; 429 com `ProblemDetails`, `Retry-After` e `RateLimit-Remaining`; leituras sem limite; atrás do proxy confiável, o limite contado pelo IP em `X-Forwarded-For`; e, chamando direto, um `X-Forwarded-For` forjado é ignorado.
- **Contrato HTTP (`TransactionsApiContractTests`)**: 201 com `Location`; 409; 422; 404; 400 para cada campo ausente sem tocar no banco; `occurredAt` com fuso `-03:00` armazenado em UTC; `type` numérico ou desconhecido recusado em português sem expor tipos internos; corpo ausente; crédito que estouraria o limite do banco; e página do extrato além do fim.

Validei que os testes detectam os problemas de verdade: removendo o `FOR UPDATE` do repositório, dois testes de concorrência falham; desligando o middleware de rate limiting, os dois testes de 429 falham.

**Testes do frontend (76):**

- **Validação (`shared/input-error`)**: mensagens por validador, token substituível e mensagem genérica para validador sem texto; a diretiva não mostra erro em formulário recém-aberto, mostra ao alterar e ao enviar, troca e remove a mensagem, aplica as classes, respeita `withoutFormValidation`, limpa tudo no reset, exibe erros do servidor e funciona com o critério "ao sair do campo".
- **Layout**: `AppComponent` só com o roteador, telas renderizadas dentro do layout e navegação com o item ativo.

- **Tradução de erros e serviços de API**: cada status HTTP vira o `kind` certo, o contrato do `POST`, os parâmetros de paginação e a propagação de erros.
- **Formulário de lançamento**: validações exibidas sob cada campo (vazio, valor zero, mais de 2 casas), prévia de saldo e aviso de débito acima do saldo, envio com o contrato da API e `eventId` gerado internamente (sem aparecer na tela), limpeza após o sucesso sem acusar o campo vazio como erro, bloqueio de envio duplo e as respostas 409, 422, falha de comunicação (o reenvio dos mesmos dados usa o mesmo `eventId`; dados alterados usam outro) e 400 com erro no campo.
- **Telas de dados**: estados de carregamento, vazio e erro com "tentar novamente"; renderização das contas e do extrato com valores e sinais; troca de página pedindo a página certa à API; e a tabela mantida visível durante a troca.

## Decisões e trade-offs

Cada decisão abaixo tem um custo. Registro o que ganhei, o que paguei e quando eu mudaria de ideia.

### Arquitetura e organização

- **Clean Architecture num domínio pequeno.** Ganho: regras testáveis sem banco, dependências com direção garantida pelo compilador e troca de infraestrutura sem tocar no domínio. Custo: mais projetos, arquivos e indireção (interfaces com uma implementação só) do que um CRUD exigiria. Justifica-se porque o teste avalia exatamente separação de responsabilidades e porque as regras financeiras são o coração do sistema.
- **Monorepo.** Front, back e infra versionados juntos: um `docker compose up` sobe tudo e cada commit representa um estado coerente. Custo: pipelines e deploys independentes por aplicação ficariam mais trabalhosos se os times crescessem separados.
- **Controllers em vez de Minimal API.** Mais estrutura e convenção conhecida, em troca de um pouco mais de cerimônia (ver [a seção dedicada](#por-que-controllers-e-não-minimal-api)).
- **Lado de leitura separado (`IAccountQueries`).** Consultas projetam direto para DTOs, sem carregar o agregado; é um CQRS leve. Custo: dois caminhos de acesso a dados para manter coerentes.

### Consistência e dados

- **Bloqueio pessimista (`FOR UPDATE`) em vez de concorrência otimista.** Numa conta com muitos eventos simultâneos, o otimista geraria conflitos e retentativas; o pessimista enfileira. Custo: contenção na mesma conta (eventos dela viram sequenciais) e transações que seguram a linha pelo tempo do processamento. Aceitável porque contas distintas não se bloqueiam.
- **Saldo materializado na tabela `accounts`.** Ler o saldo é uma consulta simples, sem somar o histórico. Custo: o saldo e o histórico precisam andar juntos, o que é garantido pela transação única e pelos `CHECK`s; uma alteração manual no banco poderia fazê-los divergir, e não há rotina de reconciliação (`balance` = soma dos lançamentos) rodando periodicamente.
- **Idempotência pela chave primária.** Simples e à prova de corrida. Custos: o `eventId` é único **globalmente** (se pudesse repetir entre contas, a chave seria composta) e a "memória" de idempotência nunca expira, porque é a própria tabela de lançamentos. Em volume muito alto, uma chave com prazo (ex.: no Redis) economizaria espaço, mas deixaria de proteger reenvios tardios.
- **Ordem do extrato pelo processamento, não pela ocorrência.** `balance_after` reflete a ordem em que o saldo realmente mudou, então a cadeia de saldos é sempre coerente. Custo: um evento com `occurredAt` antigo que chega tarde aparece no topo do extrato; o `occurredAt` é informativo e nada é recalculado retroativamente.
- **Paginação por offset (`page`/`pageSize`).** Permite ir direto a uma página e mostrar o total. Custos: páginas muito profundas ficam mais lentas, cada requisição faz um `COUNT`, e se um lançamento novo entrar enquanto o usuário pagina, os itens "descem" e um pode se repetir na página seguinte. Paginação por cursor (keyset) resolveria, ao custo de não saltar para uma página qualquer.
- **Valores em `decimal`/`numeric(18,2)`, moeda única.** Não há campo de moeda: o sistema assume reais. Multimoeda exigiria moeda por conta e regras de conversão.
- **Datas em UTC (`timestamptz`).** O servidor grava e compara em UTC; a conversão para o fuso do usuário é só na tela.

### API e processamento

- **Processamento síncrono.** O cliente recebe o resultado final (201, 409, 422) na mesma requisição. Custo: picos de carga chegam direto ao banco. Uma fila desacoplaria entrada e processamento, ao preço de consistência eventual (ver [Melhorias futuras](#mensageria-rabbitmq)).
- **Exceções de domínio para regras violadas.** Fluxo feliz limpo e tradução para HTTP em um lugar só. Em caminhos de altíssimo volume, um `Result<T>` evitaria o custo de lançar exceções.
- **Sem autenticação.** Qualquer cliente que alcance a API pode lançar. O rate limiting por IP mitiga abuso, mas não substitui identidade (ver Keycloak em melhorias futuras).
- **Migrations na subida da API.** Prático para o Compose; em produção, com várias instâncias, isso deveria ser uma etapa separada do deploy.
- **Contas vêm do seed.** Não há endpoint de criação de contas, porque o enunciado foca em movimentação e saldo.

### Proteção e operação

- **Rate limiting fail-open.** Priorizei a disponibilidade dos lançamentos sobre a proteção contra abuso quando o Redis cai. Num cenário com risco alto de abuso, fail-closed seria defensável.
- **Janela fixa.** Simples e barata (um contador por cliente). Custo: na virada da janela, um cliente pode fazer até o dobro do limite em poucos segundos. Janela deslizante ou *token bucket* seriam mais precisos.
- **Limite por IP.** Sem autenticação, é a identidade disponível. Custo: usuários atrás do mesmo NAT (ex.: uma empresa) dividem o mesmo limite.
- **Redis sem persistência.** Contadores de janela curta não precisam sobreviver a um reinício; reiniciar o Redis só "zera" as janelas em andamento.
- **Elasticsearch opcional e sem segurança local.** Fica num profile para o `docker compose up` padrão continuar leve; a segurança está desligada por ser ambiente de demonstração (portas só em `127.0.0.1`).
- **O que vai para os logs.** Lançamentos registram `EventId`, `AccountId`, tipo, valor e saldo, o que é útil para auditoria. O nome do titular não é logado. Em produção, dados financeiros em log pedem política de retenção e controle de acesso ao Elasticsearch.
- **Testes de integração com containers reais.** Detectam problemas que mocks não pegam (lock, transação, chave única, script Lua), ao custo de exigir Docker e rodar mais devagar que testes em memória.

### Frontend

- **Sem gerência de estado global.** Cada tela busca os próprios dados; depois de um lançamento, as contas são recarregadas da API. Custo: requisições repetidas entre telas. Com mais telas compartilhando dados, um store (NgRx) passaria a valer.
- **Chave de idempotência só em memória.** O `IdempotencyKeyTracker` protege o reenvio enquanto a tela está aberta. Se a rede cair e o usuário **recarregar a página** antes de reenviar, a chave se perde e um novo envio recebe outra chave; se a primeira tentativa tinha chegado ao servidor, o lançamento seria feito duas vezes. Guardar a tentativa pendente no `sessionStorage` fecharia essa brecha.
- **Prévia de saldo pode estar desatualizada.** Ela usa o saldo carregado na tela; outro lançamento feito em paralelo não aparece até recarregar. Por isso a prévia só orienta e o servidor decide.
- **PrimeNG.** Componentes prontos e acessíveis, ao custo de um bundle inicial maior (~495 kB, ~120 kB comprimido) do que componentes próprios.
- **Angular 19, fora do suporte.** O projeto começou no Angular 19, que saiu do suporte oficial em maio de 2026 (a 20 e a 21 estão em LTS). Atualizar exige subir Angular e PrimeNG juntos, major a major, e revalidar tema e testes; preferi não arriscar a estabilidade da entrega por isso. É a primeira atualização que eu faria, e contrasta com a escolha do .NET 10 (LTS mais recente) no backend.
- **Proxy de `/api` no nginx.** Elimina CORS e URLs por ambiente, mas acopla o front à topologia de deploy (a API precisa estar atrás do mesmo host).
- **Fontes do Google Fonts.** Dependência externa em tempo de execução e motivo de não haver Content-Security-Policy; hospedar as fontes localmente permitiria fechar a CSP.
- **Sem SSR.** Removi o SSR do `ng new`: um painel sem SEO não se beneficia dele, e ele acrescentaria um servidor Node ao Docker.

## Uso de IA no desenvolvimento

Desenvolvi este projeto com o **Claude Code**, um agente de IA para programação. Quero deixar claro como foi a divisão de papéis, porque é o que eu gostaria de saber se estivesse avaliando.

**A IA escreveu a maior parte do código, dos testes e desta documentação. As decisões foram minhas.** Eu defini o que construir, escolhi entre as alternativas, cortei escopo, revisei o resultado e pedi correções. Respondo por cada decisão registrada neste README e pelos trade-offs de cada uma.

### Como trabalhei

1. **Eu definia o objetivo ou a decisão**, a partir do enunciado e da minha experiência (ex.: "a validação de formulários deve seguir esta arquitetura de diretivas", "o `AppComponent` não deve ter nada").
2. **O agente propunha a implementação** e, quando havia caminhos diferentes, apresentava as alternativas com os custos de cada uma.
3. **Eu escolhia, ajustava ou recusava.** Algumas propostas eu recusei por estarem fora do escopo do teste (ver abaixo).
4. **O agente implementava com testes** e verificava o resultado: build, testes automatizados, execução da stack no Docker e checagem das telas no navegador.
5. **Eu revisava e pedia mudanças**, inclusive em código já pronto (ex.: trocar a biblioteca de componentes, esconder o identificador do evento).

O [`CLAUDE.md`](CLAUDE.md) na raiz é o "contrato" que usei com o agente: regras de negócio inegociáveis, convenções e comandos do projeto. Também serve como documentação para quem for manter o código, com ou sem IA.

### Decisões que tomei

| Decisão | Por quê |
|---|---|
| **Um único repositório (monorepo)** | O enunciado pede um repositório e um `docker compose up` que suba tudo. Com dois repositórios, o Compose dependeria de clonar ambos na estrutura certa. |
| **.NET 10 (LTS)** | Para um projeto novo, a versão LTS mais recente: suporte longo e sem migração próxima. |
| **Controllers em vez de Minimal API** | Organização por recurso, contrato explícito no Swagger e convenção conhecida por times .NET (ver [a seção dedicada](#por-que-controllers-e-não-minimal-api)). |
| **Clean Architecture com IoC por camada** | Cada camada registra as próprias dependências (`AddApplication()`, `AddInfrastructure()`) e o `Program.cs` só as compõe. Regras testáveis sem banco e dependências com direção garantida. |
| **Banco e toda a stack no Docker, com credenciais em `.env`** | Ninguém precisa instalar PostgreSQL nem Redis. As senhas ficam num `.env` fora do repositório, o Compose recusa subir sem elas e os serviços internos não são expostos. |
| **PrimeNG e identidade visual modernista** | Aparência de produto de uma empresa, não de um exemplo de biblioteca. Troquei o Angular Material pelo PrimeNG durante o desenvolvimento; como a camada de dados não dependia da biblioteca de UI, a troca afetou só as telas. |
| **Validação de formulários por diretivas (`shared/input-error`)** | Arquitetura que eu já usava: o componente importa uma diretiva e as mensagens aparecem sozinhas, sem markup de erro nos templates. O agente adaptou ao projeto e corrigiu problemas da versão original (ver o [README do frontend](frontend/servicos-financeiros/README.md#validação-de-formulários-sharedinput-error)). |
| **`AppComponent` vazio, layout como rota pai** | Deixar lógica e layout na raiz é má prática em projetos reais: dificulta áreas com layouts diferentes e testes isolados. |
| **Identificador do evento escondido do usuário** | O `eventId` é detalhe técnico. A tela cuida da idempotência sozinha e o usuário só pensa em "lançamento". |
| **Diferenciais: health checks, logs estruturados e rate limiting com Redis** | Os de melhor custo-benefício, que não mudam o comportamento do núcleo. Rate limiting (e não cache) porque cachear saldo contrariaria a regra de a tela refletir fielmente o backend. |
| **Deixar NgRx, Keycloak e RabbitMQ como melhorias futuras** | Custo alto ou mudança de contrato (o RabbitMQ tornaria o lançamento assíncrono). Preferi entregar bem o núcleo e documentar o desenho. |
| **Não implementar a confirmação de "lançamento repetido em 2 segundos"** | Cheguei a pedir essa funcionalidade e, depois de avaliar o impacto, desisti: não é idempotência (são eventos diferentes), fugiria do escopo e poderia quebrar integrações que enviam eventos legítimos iguais. |

### Por que os commits não têm coautoria da IA

Desliguei a linha `Co-Authored-By` automática nos commits para manter o histórico limpo. A autoria assistida está declarada aqui, de forma explícita, em vez de espalhada pelo histórico.

## Status e próximos passos

| Item | Situação |
|---|---|
| Domínio (`Account`, `Transaction`, regras) | Feito, com testes |
| Caso de uso `ProcessTransaction` (idempotência, unit of work) | Feito, com testes |
| Persistência: `DbContext`, mapeamentos, repositórios, unit of work | Feito, com testes de integração |
| Migrations do EF Core | Feito (`InitialCreate` e `StatementIndexByProcessedAt`) |
| `POST /api/transactions` + Swagger + `ProblemDetails` | Feito |
| Docker Compose (PostgreSQL + API) com migrations e seed na subida | Feito |
| Testes de integração com PostgreSQL (Testcontainers) | Feito |
| Endpoints de leitura: listar contas, extrato paginado | Feito, com testes de integração |
| Frontend Angular: contas, extrato paginado e formulário de lançamento | Feito, com 76 testes |
| Serviço `web` (nginx + Angular) no Compose | Feito |
| Health checks (liveness e readiness) | Feito, com testes (diferencial) |
| Logs estruturados com Serilog e Elasticsearch | Feito (diferencial) |
| Rate limiting com Redis | Feito, com testes (diferencial) |
| Gerência de estado (NgRx), autenticação (Keycloak), mensageria (RabbitMQ) | Melhorias futuras (abaixo) |
| Criação de contas pela API/tela | Não feito (as contas de demonstração vêm do seed) |
| CI no GitHub Actions (build, testes e imagens Docker) | Feito |
| Testes end-to-end (navegador automatizado) | Não feito |

## Melhorias futuras

Três diferenciais ficaram de fora de propósito: cada um tem custo alto ou muda o comportamento do que já funciona, e preferi entregar bem o núcleo. Este é o desenho que eu seguiria.

### Gerência de estado (NgRx)

Hoje, RxJS com o operador `toLoadState` e signals resolvem as três telas, que não compartilham estado entre si. O NgRx passa a valer quando várias telas dependem dos mesmos dados; por exemplo, o saldo de uma conta aparecendo na lista, no extrato e no formulário ao mesmo tempo. Eu usaria o **NgRx SignalStore** (mais leve e alinhado aos signals do projeto): um `AccountsStore` com as contas e os saldos, atualizado após cada lançamento, no lugar das recargas feitas por cada tela.

### Autenticação e autorização (Keycloak, OIDC/OAuth2)

- **Keycloak** no Compose, com um realm importado na subida (clientes, papéis e usuários de demonstração).
- **Front:** login pelo fluxo *Authorization Code com PKCE* (ex.: `angular-auth-oidc-client`), um interceptor HTTP que anexa o token e um guard nas rotas.
- **API:** `AddJwtBearer` validando emissor e audiência do Keycloak, `[Authorize]` nos controllers e políticas por papel (ex.: só `operador` lança; `consulta` só lê). O Swagger ganharia o fluxo OAuth2.
- **Rate limiting** passaria a contar por usuário autenticado, e não por IP.
- **Testes:** um emissor de tokens de teste na `WebApplicationFactory`, sem depender do Keycloak.

### Mensageria (RabbitMQ)

É a mudança mais profunda, porque o `POST` deixa de responder o resultado na hora:

1. A API valida o contrato, grava o evento como **pendente** e o publica na fila, na mesma transação (padrão **Outbox**, para não haver evento gravado sem mensagem nem mensagem sem evento). Responde **202 Accepted** com a URL de status.
2. Um **consumidor** processa com as mesmas regras de hoje (idempotência pela PK, `FOR UPDATE`, transação), com fila de mensagens com erro (DLQ) e novas tentativas.
3. **Ordem por conta:** particionar as mensagens por `accountId` (ex.: *consistent hash exchange*), para os eventos de uma mesma conta serem processados em sequência.
4. **Front:** o estado "processando" passa a ser real; a tela consulta o status (ou recebe por SignalR) até o evento virar processado, recusado por saldo insuficiente ou duplicado.

Ganho: absorver picos e desacoplar quem envia de quem processa. Custo: consistência eventual na tela e mais peças para operar.
