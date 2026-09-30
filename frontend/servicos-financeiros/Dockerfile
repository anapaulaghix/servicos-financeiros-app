# Contexto de build: a pasta frontend/servicos-financeiros/

# Dependências primeiro, para aproveitar o cache de camadas.
FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

# Testes (docker compose --profile test run --rm test-frontend): Chromium para o Karma.
FROM deps AS test
RUN apk add --no-cache chromium
ENV CHROME_BIN=/usr/bin/chromium-browser
COPY . .
CMD ["npm", "run", "test:ci"]

FROM deps AS build
COPY . .
RUN npm run build -- --configuration production

# Imagem final: só os arquivos estáticos e o nginx, rodando sem privilégios de root.
FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist/servicos-financeiros/browser /usr/share/nginx/html
EXPOSE 8080
