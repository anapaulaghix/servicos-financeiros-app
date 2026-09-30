# Contexto de build: a pasta backend/
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia só os arquivos de projeto primeiro para aproveitar o cache de restore das dependências.
COPY global.json Directory.Build.props .editorconfig ./
COPY src/ServicosFinanceiros.Domain/*.csproj src/ServicosFinanceiros.Domain/
COPY src/ServicosFinanceiros.Application/*.csproj src/ServicosFinanceiros.Application/
COPY src/ServicosFinanceiros.Infrastructure/*.csproj src/ServicosFinanceiros.Infrastructure/
COPY src/ServicosFinanceiros.Api/*.csproj src/ServicosFinanceiros.Api/
RUN dotnet restore src/ServicosFinanceiros.Api

COPY src/ src/
RUN dotnet publish src/ServicosFinanceiros.Api -c Release -o /app --no-restore

# Testes (docker compose --profile test run --rm test-backend). Os de integração sobem PostgreSQL
# e Redis com Testcontainers, usando o Docker do host pelo socket montado no Compose.
FROM build AS test
COPY ServicosFinanceiros.sln ./
COPY tests/ tests/
RUN dotnet restore ServicosFinanceiros.sln
CMD ["dotnet", "test", "ServicosFinanceiros.sln", "--no-restore", "--verbosity", "quiet"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# A imagem já traz um usuário sem privilégios; a API não roda como root.
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ServicosFinanceiros.Api.dll"]
