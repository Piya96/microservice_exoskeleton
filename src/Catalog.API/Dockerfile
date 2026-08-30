FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY BuildingBlocks/EventBus/BuildingBlocks.EventBus.csproj BuildingBlocks/EventBus/
COPY Catalog.API/Catalog.API.csproj Catalog.API/
RUN dotnet restore Catalog.API/Catalog.API.csproj
COPY BuildingBlocks/EventBus/ BuildingBlocks/EventBus/
COPY Catalog.API/ Catalog.API/
RUN dotnet publish Catalog.API/Catalog.API.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
ENV CATALOG_DB_PATH=/data/catalog.db
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Catalog.API.dll"]
