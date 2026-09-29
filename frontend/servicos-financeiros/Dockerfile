# Contexto de build: a pasta frontend/servicos-financeiros/
FROM node:22-alpine AS build
WORKDIR /app

# Instala as dependências primeiro para aproveitar o cache de camadas.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build -- --configuration production

# Imagem final: só os arquivos estáticos e o nginx, rodando sem privilégios de root.
FROM nginxinc/nginx-unprivileged:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/servicos-financeiros/browser /usr/share/nginx/html
EXPOSE 8080
