# Serviços Financeiros — Teste Técnico Fullstack Pleno

Motor de um serviço financeiro que gerencia e atualiza saldos de contas bancárias.
Monorepo: API REST em .NET + aplicação web em Angular + PostgreSQL, tudo via Docker Compose.
O enunciado original (PDF) não é versionado.

## Estrutura

- `backend/` — solution .NET 10 LTS (`ServicosFinanceiros.sln`)
  - `src/ServicosFinanceiros.Domain` — entidades, regras e exceções de domínio (sem dependências externas)
  - `src/ServicosFinanceiros.Application` — casos de uso e interfaces (portas); `AddApplication()` registra os serviços
  - `src/ServicosFinanceiros.Infrastructure` — EF Core, Npgsql, migrations, repositórios; `AddInfrastructure()` registra os serviços
  - `src/ServicosFinanceiros.Api` — controllers, Swagger, tradução de exceções em ProblemDetails; composition root (`Program.cs`)
  - `tests/ServicosFinanceiros.UnitTests` — xUnit, Moq, FluentAssertions
  - `tests/ServicosFinanceiros.IntegrationTests` — xUnit + Testcontainers (PostgreSQL real; exige Docker rodando)
- `frontend/servicos-financeiros/` — Angular 19 + PrimeNG (standalone, OnPush, RxJS, sem SSR)
  - `src/app/core` — modelos, serviços de API, `toApiError`, `toLoadState`; `features/` — telas; `shared/` — blocos reutilizáveis
  - `src/app/layout` — `MainLayoutComponent` (rota pai das telas) e `SidebarComponent`; o `AppComponent` contém só o `<router-outlet />` e deve continuar assim
  - `src/app/shared/input-error` — validação de formulários: importe a `DynamicValidatorMessageDirective` no componente; mensagens em `VALIDATION_ERROR_MESSAGES`, validadores em `CustomValidators`. Não escreva markup de erro por campo nos templates
  - `src/app/theme/app-preset.ts` e `src/styles/_tokens.scss` — identidade visual (modernista); trocar a marca é mexer só neles
- `docker-compose.yml` (raiz) — postgres + api + web (nginx servindo o Angular e fazendo proxy de `/api`)
- `.env` (não versionado) guarda as credenciais; `.env.example` é o modelo

Dependências entre camadas: Api → Application/Infrastructure; Infrastructure → Application → Domain.
O Domain não referencia nenhuma outra camada.
Cada camada expõe seu próprio registro de IoC (`DependencyInjection.cs`); o `Program.cs` só os compõe.

## Comandos

```bash
# Backend
dotnet build backend/ServicosFinanceiros.sln
dotnet test backend/ServicosFinanceiros.sln                  # inclui integração (Docker)
dotnet test backend/tests/ServicosFinanceiros.UnitTests      # só unitários, sem Docker
dotnet run --project backend/src/ServicosFinanceiros.Api

# Banco + API via Docker (exige .env com POSTGRES_USER/POSTGRES_PASSWORD)
docker compose up --build

# Migrations (a partir de backend/; a ferramenta dotnet-ef está fixada em dotnet-tools.json)
dotnet tool restore
dotnet ef migrations add <Nome> -p src/ServicosFinanceiros.Infrastructure -s src/ServicosFinanceiros.Infrastructure -o Persistence/Migrations

# Frontend (em frontend/servicos-financeiros)
npm install
npm start          # ng serve com proxy de /api para localhost:8080 (a API precisa estar no ar)
npm run test:ci    # Jasmine + Karma, Chrome headless
```

## Regras de negócio (inegociáveis)

Evento de entrada: `{ eventId, accountId, type: CREDIT|DEBIT, amount, occurredAt }`.

1. **Idempotência:** um `eventId` nunca é processado duas vezes (índice único no banco + tratamento de duplicidade).
2. **Consistência:** o saldo nunca fica negativo e reflete exatamente o histórico.
3. **Transacionalidade:** gravar a transação e atualizar o saldo na mesma transação de banco.
4. **Backend é a fonte da verdade:** validações do front são só UX; duplicidade e saldo insuficiente são decididos pela API.

Valores monetários usam `decimal` (nunca `double`/`float`). Concorrência na mesma conta deve ser tratada (lock/versão de linha).

## Convenções

- Código e identificadores em inglês nos nomes técnicos; mensagens ao usuário em português.
- C#: nullable habilitado, injeção de dependência por construtor, sem lógica de negócio em controllers.
- TypeScript: modelos tipados, `strict`, sem `any`; consumo da API via serviços com RxJS. A UI usa PrimeNG; a chamada à API é sempre relativa (`/api`), nunca uma URL absoluta.
- Frontend: erros da API são traduzidos por `toApiError`; cargas de dados usam `toLoadState`. O `eventId` nunca é exibido ao usuário: o `IdempotencyKeyTracker` o gera no envio e o reutiliza só no reenvio dos mesmos dados sem confirmação.
- Erros da API seguem `ProblemDetails`; o front trata loading, erro de comunicação e erros de validação.
- Testes priorizam cenários críticos: saldo insuficiente, duplicidade, concorrência (back); validações de formulário, estados de tela e serviços (front).
- Commits pequenos, mensagens no padrão Conventional Commits (`feat:`, `fix:`, `test:`, `chore:`), em português.
- Nunca adicionar `Co-Authored-By` nem linhas de atribuição em commits ou PRs.
- Não commitar segredos; usar `.env` (ignorado) e `.env.example`. Nenhuma senha em `appsettings`; a connection string vem de `ConnectionStrings__Postgres` (env var ou user-secrets).

## Entrega

README.md na raiz explicando arquitetura, decisões, trade-offs, e como rodar
(`docker compose up`) a aplicação e os testes de backend e frontend.
