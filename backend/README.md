# Serviços Financeiros — Backend

API REST em **C# / ASP.NET Core (.NET 10, LTS)** que recebe eventos de crédito e débito e mantém o saldo consolidado das contas, com **PostgreSQL** via **Entity Framework Core 10**. Garante idempotência por `eventId`, saldo nunca negativo, gravação transacional e tratamento de concorrência na mesma conta.

A visão geral da solução (arquitetura de ponta a ponta, regras de negócio, trade-offs) está no [README da raiz](../README.md).

## Sumário

- [Como rodar](#como-rodar)
- [Estrutura de pastas](#estrutura-de-pastas)
- [Caminho de uma requisição](#caminho-de-uma-requisição)
- [Configuração](#configuração)
- [Migrations](#migrations)
- [Testes](#testes)

## Como rodar

### Pré-requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download). O `global.json` fixa a linha 10 do SDK.
- Docker, para o PostgreSQL e para os testes de integração.

### Opção 1: tudo pelo Docker (recomendado)

Na **raiz do repositório**, rode (o `.env` é opcional; sem ele valem senhas padrão de desenvolvimento, ver [README da raiz](../README.md#1-opcional-crie-o-seu-env)):

```bash
docker compose up --build
```

A API sobe em http://localhost:8080 com PostgreSQL, Redis, migrations aplicadas e contas de demonstração.

### Opção 2: API local, banco no Docker

Útil para depurar pela IDE.

1. Suba só o banco (na raiz do repositório). Se a API do Docker estiver rodando, pare-a para liberar a porta 8080:

```bash
docker compose up -d db
docker compose stop api
```

2. Informe a connection string por *user secrets*, que ficam fora do repositório (use o usuário e a senha do seu `.env` ou, sem ele, `app_user` / `dev_only_postgres`):

```bash
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=servicos_financeiros;Username=<usuario>;Password=<senha>" --project src/ServicosFinanceiros.Api
```

3. Rode a API. Os perfis de desenvolvimento (`launchSettings.json`) já aplicam as migrations e criam as contas de demonstração:

```bash
dotnet run --project src/ServicosFinanceiros.Api --launch-profile http --urls http://localhost:8080
```

O Swagger fica em http://localhost:8080/swagger. A porta 8080 é a mesma que o proxy do frontend (`npm start`) espera. Sem a connection string do Redis, o rate limiting fica desligado; isso é intencional para o desenvolvimento local.

### Opção 3: pelo Visual Studio

Requer o **Visual Studio 2026** (versão 18) com a carga de trabalho "Desenvolvimento para ASP.NET e Web": o Visual Studio 2022 não abre projetos .NET 10. VS Code com a extensão C# Dev Kit e JetBrains Rider também funcionam.

1. Suba o banco: `docker compose up -d db` (na raiz do repositório).
2. Abra `backend/ServicosFinanceiros.sln`.
3. No Solution Explorer, clique com o botão direito em **ServicosFinanceiros.Api** → **Manage User Secrets** e cole, com o usuário e a senha do seu `.env` (sem ele, `app_user` / `dev_only_postgres`):

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=servicos_financeiros;Username=<usuario>;Password=<senha>"
  }
}
```

4. Defina **ServicosFinanceiros.Api** como projeto de inicialização, escolha o perfil **http** e rode (F5). O Swagger abre em http://localhost:5077/swagger.

Sem o passo 3, a API encerra na subida com `Connection string 'Postgres' não configurada`. É intencional: nenhuma senha fica no código ou nos `appsettings`. Para usar o frontend com a API rodando pelo Visual Studio, altere o `target` do `frontend/servicos-financeiros/proxy.conf.json` para `http://localhost:5077`, ou use a opção 2, que roda na porta 8080.

### Comandos úteis

| Comando | O que faz |
|---|---|
| `dotnet build` | Compila a solution (com os analisadores do .NET e as regras do `.editorconfig`) |
| `dotnet test` | Todos os testes (os de integração exigem Docker rodando) |
| `dotnet test tests/ServicosFinanceiros.UnitTests` | Só os unitários, sem Docker |
| `dotnet tool restore` | Instala o `dotnet-ef` na versão fixada em `dotnet-tools.json` |

Os comandos acima são executados a partir da pasta `backend/`.

## Estrutura de pastas

```
backend/
├── ServicosFinanceiros.sln
├── global.json                  versão do SDK
├── Directory.Build.props        configuração comum: nullable, analisadores, avisos como erro na CI
├── .editorconfig                estilo e convenções de nomes verificados no build
├── dotnet-tools.json            dotnet-ef fixado
├── Dockerfile / .dockerignore   imagem de produção (sdk → aspnet, sem root) e estágio de testes
├── src/
│   ├── ServicosFinanceiros.Domain/          regras de negócio puras
│   ├── ServicosFinanceiros.Application/     casos de uso e portas
│   ├── ServicosFinanceiros.Infrastructure/  banco, Redis, health checks
│   └── ServicosFinanceiros.Api/             HTTP, observabilidade, composição
└── tests/
    ├── ServicosFinanceiros.UnitTests/         xUnit + Moq + FluentAssertions
    └── ServicosFinanceiros.IntegrationTests/  Testcontainers + WebApplicationFactory
```

As dependências apontam sempre para dentro, e o compilador garante isso pelas referências entre projetos:

```
Api ──▶ Application ──▶ Domain
 │           ▲
 └──▶ Infrastructure
```

O **Domain** não referencia nenhum outro projeto nem framework. A **Application** define interfaces (portas) que a **Infrastructure** implementa. A **Api** é o *composition root*: a única camada que conhece todas as outras e as liga.

### `ServicosFinanceiros.Domain`

| Pasta / arquivo | Responsabilidade |
|---|---|
| `Accounts/Account.cs` | Agregado raiz. `Account.Open()` cria a conta; `Apply()` é o **único** caminho que altera o saldo: valida o evento (valor > 0, no máximo 2 casas, tipo válido), recusa débito acima do saldo e devolve o lançamento gerado. |
| `Accounts/Transaction.cs` | Lançamento imutável, identificado pelo `EventId`. Guarda `BalanceAfter` (saldo após o lançamento) e expõe `SignedAmount` (+ crédito, − débito). |
| `Accounts/TransactionType.cs` | `Credit` e `Debit`. |
| `Exceptions/` | `DomainException` (base) e as regras violadas: `InsufficientFundsException`, `InvalidTransactionException`, `DuplicateEventException`, `AccountNotFoundException`. |

### `ServicosFinanceiros.Application`

| Pasta / arquivo | Responsabilidade |
|---|---|
| `Transactions/ProcessTransactionHandler.cs` | Caso de uso de escrita. Dentro de uma transação: verifica duplicidade do `eventId`, carrega a conta **com bloqueio de linha**, chama `Account.Apply()` e registra o lançamento. Uma violação de chave única (dois eventos idênticos simultâneos) vira `DuplicateEventException`. Loga o lançamento só depois do commit. |
| `Transactions/ProcessTransactionCommand.cs` | Dados de entrada do caso de uso. |
| `Transactions/ITransactionQueries.cs` | Consulta de um lançamento pelo `eventId` (endereço do `Location` do 201). |
| `Transactions/IProcessTransactionHandler.cs` | Contrato do caso de uso, usado pelo controller e pelos testes. |
| `Abstractions/` | Portas implementadas pela Infrastructure: `IAccountRepository` (`GetByIdForUpdateAsync`), `ITransactionRepository`, `IUnitOfWork` (executa um trabalho numa transação) e `UniqueConstraintViolationException` (sinaliza violação de unicidade sem expor detalhes do banco). |
| `Accounts/` | Lado de leitura: `IAccountQueries` (listar contas, consultar uma conta, extrato paginado) e os DTOs `AccountSummary`, `StatementEntry` e `PagedResult<T>`. |
| `DependencyInjection.cs` | `AddApplication()`: registra os casos de uso. |

### `ServicosFinanceiros.Infrastructure`

| Pasta / arquivo | Responsabilidade |
|---|---|
| `Persistence/AppDbContext.cs` | `DbContext` com `Accounts` e `Transactions`; aplica os mapeamentos da pasta `Configurations`. |
| `Persistence/Configurations/` | Mapeamento EF Core: nomes de tabela e coluna em snake_case, `numeric(18,2)`, **chave primária em `event_id`** (idempotência), `CHECK`s (`balance >= 0`, `amount > 0`, `balance_after >= 0`), chave estrangeira e o índice `(account_id, processed_at DESC)` do extrato. |
| `Persistence/Repositories/AccountRepository.cs` | Lê a conta com `SELECT ... FOR UPDATE`: a linha fica bloqueada até o fim da transação, então eventos da mesma conta são processados em fila. |
| `Persistence/Repositories/TransactionRepository.cs` | Verifica se um `eventId` já existe e adiciona lançamentos. |
| `Persistence/Repositories/TransactionQueries.cs` | Implementa `ITransactionQueries`. |
| `Persistence/Repositories/AccountQueries.cs` | Implementa `IAccountQueries` com projeções `AsNoTracking` (sem carregar o agregado). O extrato é ordenado por data de processamento, com desempate pelo `event_id` para a paginação ser estável. |
| `Persistence/EfUnitOfWork.cs` | Abre a transação, executa o caso de uso, faz um único `SaveChanges` e o commit. Traduz a violação de unicidade do PostgreSQL (`23505`) para `UniqueConstraintViolationException`. |
| `Persistence/DatabaseInitializer.cs` | Aplica as migrations e, opcionalmente, cria 3 contas de demonstração com um crédito inicial registrado como lançamento. |
| `Persistence/AppDbContextFactory.cs` | Usada só pelo `dotnet ef` para gerar migrations sem conexão com o banco. |
| `Persistence/Migrations/` | `InitialCreate` (tabelas, chaves, checks) e `StatementIndexByProcessedAt` (índice do extrato por data de processamento). |
| `RateLimiting/` | `IRateLimiter`, `RedisRateLimiter` (janela fixa com `INCR` + `PEXPIRE` num script Lua atômico; *fail-open* se o Redis cair) e `NoRateLimiter` (usado quando o Redis não está configurado). |
| `HealthChecks/RedisHealthCheck.cs` | `PING` no Redis; indisponível = `Degraded`, porque a API segue funcionando sem ele. |
| `DependencyInjection.cs` | `AddInfrastructure()`: `DbContext`, repositórios, unit of work, `TimeProvider`, Redis (opcional) e os health checks do PostgreSQL e do Redis com a tag `ready`. |

### `ServicosFinanceiros.Api`

| Pasta / arquivo | Responsabilidade |
|---|---|
| `Program.cs` | Composition root: logs, `AddApplication()` + `AddInfrastructure()`, controllers, `ProblemDetails`, rate limiting, forwarded headers e Swagger. Define a ordem do pipeline HTTP. |
| `Controllers/TransactionsController.cs` | `POST /api/transactions` (responde 201 com `Location`) e `GET /api/transactions/{eventId}`. O `POST` converte o contrato em comando e chama o caso de uso. Documenta as respostas 201, 400, 404, 409, 422 e 429 no Swagger. Tem `[RateLimit]`. |
| `Controllers/AccountsController.cs` | `GET /api/accounts`, `GET /api/accounts/{id}` e `GET /api/accounts/{id}/transactions?page&pageSize` (1 a 100 por página). |
| `Contracts/` | `TransactionRequest` (contrato de entrada com campos anuláveis, para o `[Required]` detectar campo ausente, e conversão de `occurredAt` para UTC) e `TransactionResponse`. O domínio nunca é exposto diretamente. |
| `ExceptionHandling/ValidationProblemFactory.cs` | Monta o 400 de payload inválido em português, com o campo em `errors` e sem expor mensagens internas do desserializador. |
| `ExceptionHandling/DomainExceptionHandler.cs` | Traduz exceções de domínio em `ProblemDetails` num único lugar: duplicado → 409, saldo insuficiente → 422, conta inexistente → 404, dados inválidos → 400. Loga cada recusa. Por isso os controllers não têm `try/catch`. |
| `Observability/StructuredLogging.cs` | Serilog: texto legível em desenvolvimento, JSON no container, envio opcional ao Elasticsearch e uma linha de log por requisição HTTP (health checks ficam de fora). |
| `Observability/HealthCheckEndpoints.cs` | `/health/live` (só o processo) e `/health/ready` (PostgreSQL e Redis), com resposta em JSON. |
| `RateLimiting/` | `[RateLimit("política")]`, as opções por política (`PermitLimit`, `WindowSeconds`) e o middleware que responde 429 com `Retry-After`. |
| `appsettings*.json` | Níveis de log, flags de banco e Swagger, limites do rate limiting. **Nenhuma senha**: connection strings vêm de variáveis de ambiente ou *user secrets*. |

### `tests/`

| Projeto / arquivo | O que cobre |
|---|---|
| `UnitTests/Domain/AccountTests.cs` | Regras do agregado: crédito, débito, débito igual ao saldo, saldo insuficiente sem alterar o estado, valores inválidos e saldo igual à soma dos lançamentos. |
| `UnitTests/Application/ProcessTransactionHandlerTests.cs` | Caso de uso com dependências falsas (Moq): duplicidade, violação de unicidade concorrente, saldo insuficiente, conta inexistente e uso do unit of work. |
| `IntegrationTests/PostgresFixture.cs`, `RedisFixture.cs` | Sobem PostgreSQL e Redis reais uma vez por execução (Testcontainers) e aplicam as migrations. |
| `IntegrationTests/ProcessTransactionIntegrationTests.cs` | Idempotência, atomicidade e **concorrência** contra o banco real (ex.: 10 débitos simultâneos numa conta de 100 permitem só 5). |
| `IntegrationTests/AccountQueriesIntegrationTests.cs` | Listagem, ordem do extrato, impacto no saldo e paginação sem repetir nem pular itens. |
| `IntegrationTests/RedisRateLimiterIntegrationTests.cs` | Limite, janela, clientes independentes, atomicidade sob concorrência e *fail-open*. |
| `IntegrationTests/TransactionsApiContractTests.cs` | Contrato HTTP do `POST /api/transactions`: 201 com `Location`, 409, 422, 404, 400 por campo, fuso horário, enum, corpo ausente e casos-limite. |
| `IntegrationTests/ApiIntegrationTests.cs` | A API inteira em memória (`WebApplicationFactory`): health checks, 429 e IP do cliente vindo do proxy. |

## Caminho de uma requisição

`POST /api/transactions` com um débito:

1. **Pipeline HTTP:** forwarded headers (IP real atrás do nginx) → log da requisição → tratamento de exceções → roteamento → **rate limiting** (429 se excedido).
2. **Controller:** o `[ApiController]` valida o contrato (400 automático se inválido) e chama `IProcessTransactionHandler`.
3. **Caso de uso**, dentro do `EfUnitOfWork` (uma transação):
   1. o `eventId` já existe? → `DuplicateEventException`;
   2. `SELECT ... FOR UPDATE` na conta (outros eventos da mesma conta esperam);
   3. `account.Apply(...)` valida e recusa se o saldo for insuficiente;
   4. o lançamento é adicionado; um único `SaveChanges` grava lançamento e saldo; commit.
4. **Resposta:** 201 com o lançamento e o `balanceAfter`. Se alguma regra foi violada, o `DomainExceptionHandler` responde com o `ProblemDetails` correspondente e nada foi gravado.

## Configuração

| Chave | Padrão | Descrição |
|---|---|---|
| `ConnectionStrings:Postgres` | (obrigatória) | Connection string do PostgreSQL. No Docker vem da variável `ConnectionStrings__Postgres`. |
| `ConnectionStrings:Redis` | vazio | Redis do rate limiting. Vazio = rate limiting desligado. |
| `Database:MigrateOnStartup` | `false` | Aplica as migrations ao subir (ligado no Docker). |
| `Database:SeedOnStartup` | `false` | Cria as contas de demonstração, se o banco estiver vazio. |
| `Swagger:Enabled` | `false` | Liga o Swagger fora do ambiente Development. |
| `RateLimiting:Transactions:PermitLimit` / `WindowSeconds` | `20` / `10` | Limite de lançamentos por cliente e janela. |
| `Elasticsearch:Url` | vazio | Envia os logs ao Elasticsearch. Vazio = só console. |
| `ReverseProxy:TrustedProxies` | vazio | IPs dos proxies autorizados a informar o IP do cliente (`X-Forwarded-For`). No Docker, o IP fixo do nginx. |
| `Serilog:MinimumLevel` | `Information` | Nível mínimo de log, com overrides por namespace. |

Qualquer chave pode vir por variável de ambiente trocando `:` por `__` (ex.: `RateLimiting__Transactions__PermitLimit=50`).

## Migrations

```bash
dotnet tool restore
dotnet ef migrations add <Nome> -p src/ServicosFinanceiros.Infrastructure -s src/ServicosFinanceiros.Infrastructure -o Persistence/Migrations
```

Para ver o SQL que será executado:

```bash
dotnet ef migrations script -p src/ServicosFinanceiros.Infrastructure -s src/ServicosFinanceiros.Infrastructure
```

A própria Infrastructure é o projeto de *startup* do `dotnet ef` (pela `AppDbContextFactory`), então gerar migrations não depende da API nem de conexão com o banco.

## Testes

```bash
dotnet test
```

São 20 testes unitários e 41 de integração. Sem o .NET instalado, rode-os pelo Docker: `docker compose --profile test run --rm test-backend` (na raiz do repositório). Os de integração sobem containers de PostgreSQL e Redis, por isso **o Docker precisa estar rodando**. Eles priorizam os cenários críticos (concorrência, idempotência, atomicidade, rate limiting) em vez de cobertura percentual, e foram validados contra regressões: removendo o `FOR UPDATE` ou o middleware de rate limiting, os testes correspondentes falham.
