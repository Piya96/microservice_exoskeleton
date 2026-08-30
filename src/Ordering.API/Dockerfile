FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY BuildingBlocks/EventBus/BuildingBlocks.EventBus.csproj BuildingBlocks/EventBus/
COPY Catalog.API/Catalog.API.csproj Catalog.API/
COPY Ordering.API/Ordering.API.csproj Ordering.API/
RUN dotnet restore Ordering.API/Ordering.API.csproj
COPY BuildingBlocks/EventBus/ BuildingBlocks/EventBus/
COPY Catalog.API/ Catalog.API/
COPY Ordering.API/ Ordering.API/
RUN dotnet publish Ordering.API/Ordering.API.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
ENV ORDERING_DB_PATH=/data/ordering.db
ENV CATALOG_API_URL=http://catalog-api:8080
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ordering.API.dll"]
