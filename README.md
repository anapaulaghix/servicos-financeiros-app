# Serviços Financeiros

[![CI](https://github.com/anapaulaghix/servicos-financeiros-app/actions/workflows/ci.yml/badge.svg)](https://github.com/anapaulaghix/servicos-financeiros-app/actions/workflows/ci.yml)

Motor de um serviço financeiro que recebe eventos de crédito e débito e mantém o saldo consolidado de contas bancárias: API REST em .NET 10 + aplicação web em Angular 19 + PostgreSQL, tudo com `docker compose up`.

## Em 1 minuto

- **Como rodar:** `docker compose up --build` (sem configurar nada). Aplicação em http://localhost:4200, Swagger em http://localhost:8080/swagger.
- **Regras garantidas:** um evento nunca é processado duas vezes (chave primária no `eventId`), o saldo nunca fica negativo (domínio + `CHECK` no banco), lançamento e saldo são gravados na mesma transação e eventos simultâneos na mesma conta são enfileirados (`SELECT ... FOR UPDATE`).
- **Testes:** 73 no backend (concorrência e contrato HTTP contra PostgreSQL e Redis reais) e 79 no frontend, na CI a cada push.
- **Diferenciais feitos:** health checks, logs estruturados (Serilog + Elasticsearch) e rate limiting com Redis. NgRx, Keycloak e RabbitMQ estão [desenhados, não implementados](#melhorias-futuras).
- **Uso de IA:** o código foi escrito majoritariamente com o Claude Code; as decisões foram minhas ([detalhes](#uso-de-ia-no-desenvolvimento)).

> [!IMPORTANT]
> **Ao testar duplicidade: reenviar o mesmo evento não devolve erro, e isso é proposital.**
>
> | Requisição | Resposta | Saldo |
> |---|---|---|
> | 1º envio de um `eventId` | `201 Created` | muda |
> | **Mesmo `eventId` com os mesmos dados** | **`200 OK`** + cabeçalho `Idempotent-Replayed: true` + o lançamento original | **não muda** |
> | Mesmo `eventId` com outros dados (ex.: outro `amount`) | `409 Conflict` | não muda |
>
> O evento duplicado **é rejeitado**: nada é lançado de novo. O que muda é a resposta: quem reenvia porque perdeu a resposta (timeout, queda de rede) recebe o resultado do lançamento que já foi feito, em vez de um erro que o deixaria sem saber se deu certo. É o padrão de APIs de pagamento como a da Stripe e do rascunho IETF do cabeçalho `Idempotency-Key`. O `409` fica para o que de fato é erro: reutilizar um identificador para outro evento.

Para ver no Swagger, envie duas vezes o mesmo corpo:

```bash
curl -i -X POST http://localhost:8080/api/transactions -H "Content-Type: application/json" \
  -d '{"eventId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","accountId":"11111111-1111-1111-1111-111111111111","type":"CREDIT","amount":150.75,"occurredAt":"2026-01-30T10:15:00Z"}'
# 1ª vez: HTTP/1.1 201 Created
# 2ª vez: HTTP/1.1 200 OK / Idempotent-Replayed: true  (mesmo corpo de resposta, saldo inalterado)
# Mesmo eventId com "amount":99: HTTP/1.1 409 Conflict
```

## Arquitetura

```
 navegador ──▶ web (nginx) ──/api──▶ api (.NET) ──▶ PostgreSQL
               Angular compilado          │
                                          └──▶ Redis (rate limiting)
```

O nginx serve o Angular e encaminha `/api`, então o navegador fala com um único host: sem CORS e sem URL de API por ambiente.

O backend segue **Clean Architecture** com domínio inspirado em DDD; as dependências apontam para dentro e o compilador garante isso pelas referências entre projetos:

```
Api ──▶ Application ──▶ Domain
 │           ▲
 └──▶ Infrastructure
```

| Projeto | Responsabilidade |
|---|---|
| `Domain` | `Account` (agregado raiz; `Apply()` é o único caminho que altera o saldo), `Transaction` e exceções de domínio. Sem dependências. |
| `Application` | Caso de uso `ProcessTransactionHandler` e portas (`IAccountRepository`, `ITransactionRepository`, `IUnitOfWork`, `IAccountQueries`). |
| `Infrastructure` | EF Core/Npgsql, repositórios, unit of work, migrations, Redis, health checks. |
| `Api` | Controllers, contratos HTTP, tradução de exceções em `ProblemDetails`, composition root. |

Cada camada registra as próprias dependências (`AddApplication()`, `AddInfrastructure()`) e o `Program.cs` só as compõe. O frontend separa `core/` (serviços de API, modelos tipados, tradução de erros), `features/` (uma pasta por tela), `shared/` e `layout/`.

Estrutura de pastas, caminho de uma requisição e configuração: [README do backend](backend/README.md) e [README do frontend](frontend/servicos-financeiros/README.md).

## Regras de negócio e onde cada uma é garantida

| Regra | Como é garantida |
|---|---|
| **Idempotência** | `eventId` é a **chave primária** de `transactions`. O handler consulta o `eventId` antes de abrir a transação (caminho rápido); se requisições iguais chegarem juntas, o banco aceita uma e recusa as outras por violação de unicidade, e estas seguem a regra do reenvio acima. A garantia real é a chave primária. |
| **Consistência** | `Account.Apply` recusa débito acima do saldo; o banco reforça com `CHECK (balance >= 0)` e `CHECK (balance_after >= 0)`. Cada lançamento guarda `balance_after`, então o saldo é reconstruível pelo histórico. |
| **Transacionalidade** | `EfUnitOfWork`: uma transação, um `SaveChanges`, um commit. Lançamento e saldo são gravados juntos ou nada é gravado. |
| **Concorrência** | A conta é lida com `SELECT ... FOR UPDATE`: eventos da mesma conta são serializados até o commit. Sem isso, dois débitos simultâneos poderiam ler o mesmo saldo e ambos passar. |
| **Backend como fonte da verdade** | Duplicidade e saldo insuficiente são decididos só na API. O front valida e mostra uma prévia do saldo, mas não bloqueia o envio. |

Valores monetários são `decimal` / `numeric(18,2)`, com no máximo 2 casas validadas no domínio.

### Endpoints

| Método e rota | Descrição |
|---|---|
| `POST /api/transactions` | Processa um evento de crédito ou débito. |
| `GET /api/transactions/{eventId}` | Consulta um lançamento (destino do `Location` do 201). |
| `GET /api/accounts` / `GET /api/accounts/{id}` | Contas com o saldo consolidado. |
| `GET /api/accounts/{id}/transactions?page=1&pageSize=10` | Extrato paginado, do mais recente para o mais antigo, com `signedAmount` e `balanceAfter` por linha. |

Respostas do `POST`: `201` processado · `200` reenvio (ver acima) · `400` payload inválido (mensagens em português, campo em `errors`) · `404` conta inexistente · `409` `eventId` reutilizado com outros dados · `422` saldo insuficiente · `429` limite de requisições. Erros seguem `ProblemDetails` (RFC 9457), traduzidos num único lugar ([`DomainExceptionHandler`](backend/src/ServicosFinanceiros.Api/ExceptionHandling/DomainExceptionHandler.cs)).

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
                                 sequence      bigint  IDENTITY (ordem de gravação)
                                 UNIQUE INDEX (account_id, sequence DESC)
```

O extrato é ordenado por `sequence`, numerada pelo banco no `INSERT`. Como os lançamentos de uma conta são gravados em série (`FOR UPDATE`), essa é exatamente a ordem em que o saldo mudou, e a cadeia de `balance_after` fica coerente mesmo quando um evento com `occurredAt` antigo chega tarde. `processed_at` não serve para ordenar porque depende do relógio de cada instância da API.

## Como rodar

**Pré-requisito:** Docker com Docker Compose. PostgreSQL, Redis, .NET e Node rodam em containers.

```bash
docker compose up --build
```

A API aplica as migrations e cria 3 contas de demonstração. Depois de subir:

| Serviço | Endereço |
|---|---|
| Aplicação web | http://localhost:4200 |
| API + Swagger | http://localhost:8080/swagger |
| Health checks | http://localhost:8080/health/live e `/health/ready` |

**Credenciais (opcional):** sem `.env`, valem senhas padrão de desenvolvimento local (o PostgreSQL e a API só escutam em `127.0.0.1` e o Redis não é publicado). Para usar as suas, `cp .env.example .env` e troque os valores; o `.env` não é versionado. As senhas só são lidas na criação dos volumes: se trocar depois, `docker compose down -v` (apaga os dados locais).

**Elasticsearch e Kibana (opcional, ~1,5 GB de RAM):**

```bash
COMPOSE_PROFILES=observability ELASTICSEARCH_URL=http://elasticsearch:9200 docker compose up --build
```

Kibana em http://localhost:5601 (data view `logs-servicos_financeiros-*`).

### Como provocar cada resposta

Na tela, o `eventId` fica escondido e a idempotência é automática: o reenvio só acontece se a rede cair depois de o servidor processar, e a tela mostra "Lançamento já registrado". Para os demais casos, use o Swagger:

| Resposta | Como provocar |
|---|---|
| `201` | Tela **Novo lançamento**, ou `POST` com a conta `11111111-1111-1111-1111-111111111111`. |
| `200` reenvio | Envie **duas vezes o mesmo corpo**. |
| `409` | Reenvie o mesmo `eventId` mudando o `amount`. |
| `422` | Um débito maior que o saldo (a tela avisa, mas deixa enviar: quem decide é a API). |
| `400` / `404` | Remova um campo ou envie `"type": 2` / use um `accountId` inexistente. |
| `429` | Mais de 20 lançamentos em 10 segundos. |

### Testes

Sem instalar .NET, Node ou Chrome:

```bash
docker compose --profile test run --rm test-backend
docker compose --profile test run --rm test-frontend
```

Com as ferramentas instaladas:

```bash
dotnet test backend/ServicosFinanceiros.sln                # integração exige Docker (Testcontainers)
cd frontend/servicos-financeiros && npm install && npm run test:ci
```

A CI ([`ci.yml`](.github/workflows/ci.yml)) roda build com avisos como erro, testes de backend (com integração), ESLint, Prettier, testes e build do front, e o build das imagens Docker.

> No dia a dia, em ambientes de produção, estou acostumada a escrever pipelines no **Azure Pipelines** (Azure DevOps). Usei o GitHub Actions aqui porque o repositório está no GitHub; as etapas (build, testes, cobertura mínima e imagens Docker) seriam as mesmas num pipeline YAML do Azure.

**O que os testes cobrem:** os principais cenários críticos, com cobertura percentual de mínimo 80% para testes unitários no frontend e backend e 5% para testes integrados no backend (No meu dia a dia, essas são as métricas atuais). No backend, contra PostgreSQL real: 10 eventos idênticos em paralelo gravam exatamente 1; 10 débitos concorrentes de 20 numa conta de 100 permitem só 5; o saldo final é igual à soma do histórico; uma falha ao gravar desfaz o saldo; e o contrato HTTP completo. Removendo o `FOR UPDATE`, os testes de concorrência falham. No frontend: validações do formulário, estados de carregamento/vazio/erro, cada resposta da API (incluindo reenvio, 409, 422, 429 e falha de rede com reuso do `eventId`) e os serviços. Lista completa nos READMEs do [backend](backend/README.md#testes) e do [frontend](frontend/servicos-financeiros/README.md#testes).

## Diferenciais implementados

- **Health checks:** `/health/live` (só o processo) e `/health/ready` (PostgreSQL e Redis). Separados para o orquestrador não reiniciar a API só porque o banco oscilou.
- **Logs estruturados:** Serilog com propriedades pesquisáveis (`EventId`, `AccountId`, `Rejection`), JSON no container, `TraceId` por requisição e envio opcional ao Elasticsearch. Lançamentos são logados só depois do commit.
  > Minha experiência prática com observabilidade é com os recursos da nuvem Azure, como **Application Insights** e **Azure Monitor** (Log Analytics). Usei Elasticsearch e Kibana aqui porque são as ferramentas citadas no enunciado.
- **Rate limiting com Redis:** 20 lançamentos a cada 10 s por cliente, contador atômico (script Lua) compartilhado entre instâncias, 429 com `Retry-After`. **Fail-open**: se o Redis cair, os lançamentos continuam. Escolhi rate limiting em vez de cache porque cachear saldo arriscaria mostrar valor desatualizado.

Detalhes de configuração no [README do backend](backend/README.md#observabilidade-e-rate-limiting).

## Decisões e trade-offs

| Decisão | Ganho | Custo |
|---|---|---|
| **Clean Architecture num domínio pequeno** | Regras testáveis sem banco, dependências com direção garantida. | Mais projetos e interfaces com uma implementação só. |
| **Bloqueio pessimista (`FOR UPDATE`)** | Eventos da mesma conta enfileiram em vez de conflitar e retentar. | Contenção na mesma conta; contas distintas não se bloqueiam. |
| **Idempotência pela chave primária** | Simples e à prova de corrida. | `eventId` único globalmente; a "memória" nunca expira (é a própria tabela). |
| **Reenvio responde 200, não 409** | Cliente que perdeu a resposta recebe o resultado. | Quem espera "duplicado = erro" precisa olhar o cabeçalho `Idempotent-Replayed`. |
| **Saldo materializado em `accounts`** | Leitura de saldo sem somar o histórico. | Saldo e histórico precisam andar juntos (transação + `CHECK`s); não há rotina de reconciliação. |
| **Processamento síncrono** | O cliente recebe o resultado final na hora. | Picos chegam direto ao banco (ver RabbitMQ em melhorias futuras). |
| **Paginação por offset** | Salto para qualquer página e total exibido. | Páginas profundas mais lentas; um lançamento novo pode repetir um item na página seguinte. |
| **Exceções de domínio + handler único** | Fluxo feliz limpo, sem `try/catch` nos controllers. | Custo de lançar exceções; um `Result<T>` seria melhor em altíssimo volume. |
| **Controllers em vez de Minimal API** | Organização por recurso, contrato declarativo no Swagger, convenção conhecida. | Um pouco mais de cerimônia. |
| **Rate limiting fail-open, janela fixa, por IP** | Disponibilidade dos lançamentos; simples e barato. | Sem Redis não há proteção; até 2× o limite na virada da janela; clientes atrás do mesmo NAT dividem o limite. |
| **Migrations na subida da API** | `docker compose up` sem passos extras. | Em produção, com várias instâncias, deveria ser etapa separada do deploy. |
| **Contas de demonstração por seed na subida, e não por `InsertData` nas migrations** | O enunciado não pede criação de contas (não há endpoint nem tela), então elas precisam existir antes do primeiro uso. O seed passa pelo domínio (`Account.Open` + `Apply`), então o crédito inicial vira lançamento e o saldo bate com o extrato; e fica fora das migrations, que rodam em todos os ambientes. | Só roda na subida da API com `SeedOnStartup` e `MigrateOnStartup` ligados ([detalhes](backend/README.md#contas-de-demonstração-seed)). |
| **Sem gerência de estado global no front** | Menos código; as telas não compartilham estado. | Requisições repetidas entre telas. |
| **Chave de idempotência do front só em memória** | Simples. | Recarregar a página após uma falha de rede perde a chave; `sessionStorage` fecharia a brecha. |
| **Dinheiro como `number` no front** | Os valores vêm prontos da API e só são exibidos ou usados na prévia (arredondada a 2 casas). | Não serve para cálculo financeiro; quem calcula é o backend, em `decimal`. |
| **Angular 19** | Estabilidade da entrega. | Já saiu do suporte oficial; seria a primeira atualização a fazer. |

Sem autenticação, sem endpoint de criação de contas (vêm do seed) e sem testes end-to-end: ficaram de fora do escopo.

## Uso de IA no desenvolvimento

Desenvolvi este projeto com o **Claude Code**. **A IA escreveu parte do código, dos testes e da documentação; as decisões foram minhas.** Eu defini o que construir, escolhi entre as alternativas, cortei escopo, revisei e pedi correções, e respondo por cada decisão deste README.

Como trabalhei: eu definia o objetivo ou a decisão; o agente propunha a implementação e, quando havia caminhos diferentes, os custos de cada um; eu escolhia, ajustava ou recusava; o agente implementava com testes e verificava (build, testes, stack no Docker, telas no navegador); eu revisava. O [`CLAUDE.md`](CLAUDE.md) é o "contrato" com o agente: regras inegociáveis, convenções e comandos.

Algumas decisões que tomei:

- **Monorepo**, para um único `docker compose up` subir tudo num clone limpo.
- **Trocar Angular Material por PrimeNG** no meio do desenvolvimento; como a camada de dados não dependia da UI, só as telas mudaram.
- **Validação de formulários por diretivas** (`shared/input-error`), uma arquitetura que eu já usava, adaptada e corrigida ([detalhes](frontend/servicos-financeiros/README.md#validação-de-formulários-sharedinput-error)).
- **`AppComponent` vazio** e layout como rota pai.
- **Esconder o `eventId` do usuário**: é detalhe técnico, a tela cuida da idempotência sozinha.
- **Não implementar** um aviso de "lançamento repetido em 2 segundos": não é idempotência (são eventos diferentes) e poderia quebrar integrações que enviam eventos legítimos iguais.
- **Deixar NgRx, Keycloak e RabbitMQ para depois**, para entregar bem o núcleo.

Desliguei a linha `Co-Authored-By` automática nos commits; a autoria assistida está declarada aqui.

## Melhorias futuras

- **NgRx SignalStore:** um `AccountsStore` com contas e saldos, atualizado após cada lançamento, no lugar das recargas por tela. Passa a valer quando várias telas compartilharem esses dados.
- **Keycloak (OIDC/OAuth2):** realm importado no Compose; no front, Authorization Code com PKCE, interceptor e guard; na API, `AddJwtBearer` e políticas por papel (só `operador` lança). O rate limiting passaria a contar por usuário.
- **RabbitMQ:** a API grava o evento como pendente e o publica com o padrão **Outbox**, respondendo `202 Accepted`; um consumidor processa com as mesmas regras de hoje, com DLQ e retentativas, particionado por `accountId` para manter a ordem por conta. Ganho: absorver picos. Custo: consistência eventual na tela.
- **Outros:** `sessionStorage` para a chave de idempotência do front, reconciliação periódica de saldo, paginação por cursor, atualizar para o Angular 21 e testes end-to-end.
