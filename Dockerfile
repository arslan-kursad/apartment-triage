# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/ApartmentTriage.Web/ApartmentTriage.Web.csproj \
    -c Release -o /app

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# ONNX model download — ADR-0008 Strategy A (bake into image at build time).
# scripts/ directory is preserved so download-models.sh resolves MODELS_DIR
# to /app/models (dirname of scripts/ → /app → models/ sibling).
COPY scripts/ scripts/
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && chmod +x scripts/download-models.sh \
    && ./scripts/download-models.sh

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ApartmentTriage.Web.dll"]
