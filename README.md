# Serviços Financeiros

Motor de um serviço financeiro que recebe eventos de crédito e débito e mantém o saldo consolidado de contas bancárias. Monorepo com:

- **`backend/`** — API REST em C# / ASP.NET Core (.NET 10, LTS), PostgreSQL via EF Core.
- **`frontend/servicos-financeiros/`** — aplicação web em Angular 19.

> **Status:** o backend tem domínio, caso de uso de processamento de eventos, persistência mapeada e o endpoint `POST /api/transactions`, todos com testes. Ainda **não** estão prontos: migrations do EF Core, endpoints de leitura (contas e extrato), as telas do Angular e o `docker-compose.yml`. Veja a [seção de status](#status-e-próximos-passos).

## Stack

| Camada | Tecnologia |
|---|---|
| API | ASP.NET Core (.NET 10, LTS), Controllers, Swagger/OpenAPI (Swashbuckle) |
| Persistência | PostgreSQL, Entity Framework Core 10 (Npgsql) |
| Testes (backend) | xUnit, Moq, FluentAssertions |
| Web | Angular 19, TypeScript |

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

### Contrato de resposta

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
                                 INDEX (account_id, occurred_at DESC)
```

O índice composto sustenta o extrato paginado de uma conta, do lançamento mais recente para o mais antigo.

## Como rodar

### Pré-requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) (o `global.json` fixa a versão)
- Node.js 20+ e npm (frontend)
- PostgreSQL acessível (ver connection string abaixo)

### Backend

```bash
dotnet build backend/ServicosFinanceiros.sln
dotnet test backend/ServicosFinanceiros.sln
dotnet run --project backend/src/ServicosFinanceiros.Api
```

A connection string de desenvolvimento está em `appsettings.Development.json` (`ConnectionStrings:Postgres`). Em outros ambientes, defina a variável `ConnectionStrings__Postgres`. Com a API no ar em desenvolvimento, o Swagger fica em `/swagger`.

### Frontend

```bash
cd frontend/servicos-financeiros
npm install
npm start     # http://localhost:4200
npm test
```

### Docker Compose

Ainda não implementado. O objetivo é `docker compose up` subir PostgreSQL, API e web.

## Testes

Os testes de backend priorizam os cenários críticos do problema, não cobertura percentual:

- **Domínio (`AccountTests`)**: crédito, débito, débito igual ao saldo, saldo insuficiente sem alterar o estado, valores não positivos e com mais de 2 casas, e o saldo final igual à soma dos lançamentos.
- **Aplicação (`ProcessTransactionHandlerTests`)**: evento duplicado não altera a conta, violação de unicidade concorrente vira "duplicado", saldo insuficiente não persiste nada, conta inexistente e execução dentro do unit of work.

**Limitação conhecida:** a transacionalidade real, o `FOR UPDATE` e a violação de chave primária dependem do PostgreSQL e ainda não têm teste de integração. Os testes atuais usam dependências falsas. O próximo passo natural é testar a infraestrutura contra um PostgreSQL de verdade (Testcontainers).

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
| Persistência: `DbContext`, mapeamentos, repositórios, unit of work | Feito (sem teste de integração) |
| `POST /api/transactions` + Swagger + `ProblemDetails` | Feito |
| Migrations do EF Core | Pendente |
| Endpoints de leitura: listar contas, extrato paginado | Pendente |
| Frontend Angular (contas, extrato, formulário) | Pendente (apenas o projeto gerado) |
| `docker-compose.yml` e Dockerfiles | Pendente |
| Testes de integração com PostgreSQL | Pendente |
