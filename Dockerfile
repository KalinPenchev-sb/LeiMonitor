FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/LeiMonitor.Core/LeiMonitor.Core.csproj src/LeiMonitor.Core/
COPY src/LeiMonitor.Data/LeiMonitor.Data.csproj src/LeiMonitor.Data/
COPY src/LeiMonitor.Worker/LeiMonitor.Worker.csproj src/LeiMonitor.Worker/
RUN dotnet restore src/LeiMonitor.Worker/LeiMonitor.Worker.csproj

COPY src/LeiMonitor.Core/ src/LeiMonitor.Core/
COPY src/LeiMonitor.Data/ src/LeiMonitor.Data/
COPY src/LeiMonitor.Worker/ src/LeiMonitor.Worker/

# Re-run restore after full source copy so publish does not use host-generated obj/assets.
RUN dotnet restore src/LeiMonitor.Worker/LeiMonitor.Worker.csproj

RUN dotnet publish src/LeiMonitor.Worker/LeiMonitor.Worker.csproj \
    -c Release \
    -o /app \
    --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app

RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app .

ENTRYPOINT ["dotnet", "LeiMonitor.Worker.dll"]
