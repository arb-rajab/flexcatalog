# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/FlexCatalog.Api/FlexCatalog.Api.csproj src/FlexCatalog.Api/
RUN dotnet restore src/FlexCatalog.Api/FlexCatalog.Api.csproj

COPY src/FlexCatalog.Api/ src/FlexCatalog.Api/
RUN dotnet publish src/FlexCatalog.Api/FlexCatalog.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd --gid 10001 flexcatalog \
    && useradd --uid 10001 --gid flexcatalog --shell /usr/sbin/nologin --no-create-home flexcatalog

COPY --from=build /app/publish .

USER flexcatalog

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "FlexCatalog.Api.dll"]
