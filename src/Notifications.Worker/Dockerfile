FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY BuildingBlocks/EventBus/BuildingBlocks.EventBus.csproj BuildingBlocks/EventBus/
COPY Catalog.API/Catalog.API.csproj Catalog.API/
COPY Ordering.API/Ordering.API.csproj Ordering.API/
COPY Notifications.Worker/Notifications.Worker.csproj Notifications.Worker/
RUN dotnet restore Notifications.Worker/Notifications.Worker.csproj
COPY BuildingBlocks/EventBus/ BuildingBlocks/EventBus/
COPY Catalog.API/ Catalog.API/
COPY Ordering.API/ Ordering.API/
COPY Notifications.Worker/ Notifications.Worker/
RUN dotnet publish Notifications.Worker/Notifications.Worker.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Notifications.Worker.dll"]
