# Serviços Financeiros — Teste Técnico Fullstack Pleno

Motor de um serviço financeiro que gerencia e atualiza saldos de contas bancárias.
Monorepo: API REST em .NET + aplicação web em Angular + PostgreSQL, tudo via Docker Compose.
O enunciado original (PDF) não é versionado.

## Estrutura

- `backend/` — solution .NET 8 (`ServicosFinanceiros.sln`)
  - `src/ServicosFinanceiros.Domain` — entidades, regras e exceções de domínio (sem dependências externas)
  - `src/ServicosFinanceiros.Application` — casos de uso, DTOs e interfaces (portas)
  - `src/ServicosFinanceiros.Infrastructure` — EF Core, Npgsql, migrations, repositórios
  - `src/ServicosFinanceiros.Api` — controllers, Swagger, composição de dependências
  - `tests/ServicosFinanceiros.UnitTests` — xUnit, Moq, FluentAssertions
- `frontend/servicos-financeiros/` — Angular 19 (componentes, serviços, RxJS, Angular Material)
- `docker-compose.yml` (raiz) — api + web + postgres (a criar)

Dependências entre camadas: Api → Application/Infrastructure; Infrastructure → Application → Domain.
O Domain não referencia nenhuma outra camada.

## Comandos

```bash
# Backend
dotnet build backend/ServicosFinanceiros.sln
dotnet test backend/ServicosFinanceiros.sln
dotnet run --project backend/src/ServicosFinanceiros.Api

# Frontend (em frontend/servicos-financeiros)
npm install
npm start          # ng serve
npm test           # ng test
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
- TypeScript: modelos tipados, `strict`, sem `any`; consumo da API via serviços com RxJS.
- Erros da API seguem `ProblemDetails`; o front trata loading, erro de comunicação e erros de validação.
- Testes priorizam cenários críticos: saldo insuficiente, duplicidade, concorrência (back); validações de formulário, estados de tela e serviços (front).
- Commits pequenos, mensagens no padrão Conventional Commits (`feat:`, `fix:`, `test:`, `chore:`), em português.
- Nunca adicionar `Co-Authored-By` nem linhas de atribuição em commits ou PRs.
- Não commitar segredos; usar `.env` (ignorado) e `.env.example`.

## Entrega

README.md na raiz explicando arquitetura, decisões, trade-offs, e como rodar
(`docker compose up`) a aplicação e os testes de backend e frontend.
