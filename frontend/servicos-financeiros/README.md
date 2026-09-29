# Serviços Financeiros — Frontend

Aplicação web em Angular 19 com PrimeNG. A documentação completa (arquitetura, decisões, como rodar tudo com Docker) está no [README da raiz](../../README.md#frontend).

## Comandos

```bash
npm install
npm start          # http://localhost:4200, com proxy de /api para http://localhost:8080
npm run build      # build de produção em dist/
npm run test:ci    # testes (Jasmine + Karma, Chrome headless)
```

A API precisa estar no ar para o `npm start` funcionar: `docker compose up -d db api` na raiz do repositório.
