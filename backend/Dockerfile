# Contexto de build: a pasta backend/
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia só os .csproj primeiro para aproveitar o cache de restore das dependências.
COPY global.json ./
COPY src/ServicosFinanceiros.Domain/*.csproj src/ServicosFinanceiros.Domain/
COPY src/ServicosFinanceiros.Application/*.csproj src/ServicosFinanceiros.Application/
COPY src/ServicosFinanceiros.Infrastructure/*.csproj src/ServicosFinanceiros.Infrastructure/
COPY src/ServicosFinanceiros.Api/*.csproj src/ServicosFinanceiros.Api/
RUN dotnet restore src/ServicosFinanceiros.Api

COPY src/ src/
RUN dotnet publish src/ServicosFinanceiros.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# A imagem já traz um usuário sem privilégios; a API não roda como root.
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ServicosFinanceiros.Api.dll"]
