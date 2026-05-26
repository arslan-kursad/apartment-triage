# ── Build stage ────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore \
    src/ApartmentTriage.Web/ApartmentTriage.Web.csproj
RUN dotnet publish \
    src/ApartmentTriage.Web/ApartmentTriage.Web.csproj \
    -c Release -o /app/publish \
    /p:UseAppHost=false

# ── Runtime stage ───────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# ONNX model download (ADR-0008 Strategy A)
COPY scripts/ scripts/
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && chmod +x scripts/download-models.sh \
    && ./scripts/download-models.sh

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ApartmentTriage.Web.dll"]
