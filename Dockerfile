# SuDesApp lisensi server — image container untuk Railway/Render/VPS.
#
# Build lalu jalankan dengan environment variables (lihat docs/deploy-server.md):
#   SUPABASE_HOST, SUPABASE_SSLMODE
#   SUPABASE_DB_USER (role terbatas: sudes_server.<project-ref>) + SUPABASE_DB_PASSWORD
#   SUPABASE_MIGRATE_USER + SUPABASE_MIGRATE_PASSWORD (opsional — hanya untuk
#     menerapkan schema.sql saat start; tanpa itu skema tidak diubah)
#   MIDTRANS_SERVER_KEY, MIDTRANS_SANDBOX, SUDES_ADMIN_OPERATORS__0__TOKEN, LISENSI_KEY_PATH
#   ASPNETCORE_ENVIRONMENT=Production, ASPNETCORE_URLS=http://+:8080
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
# Perhatikan: global.json ikut disalin agar SDK container PERSIS sama dengan
# SDK yang dipakai CI dan mesin developer (version 11.0.100-rc.3, rollForward
# latestMajor). Tanpa ini, tag mengambang sdk:11.0 diam-diam memakai patch
# terbaru yang berbeda.
COPY global.json ./
COPY SuDesApp.Server/SuDesApp.Server.csproj SuDesApp.Server/
RUN dotnet restore SuDesApp.Server/SuDesApp.Server.csproj
COPY SuDesApp.Server/ SuDesApp.Server/
RUN dotnet publish SuDesApp.Server/SuDesApp.Server.csproj -c Release -o /app --no-restore
RUN dotnet publish SuDesApp.Server/healthcheck/healthcheck.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
WORKDIR /app
# Server lisensi memegang data pembayaran & data per-tenant — jangan jalan
# sebagai root. Image aspnet sudah menyediakan user "app" (UID 1654).
COPY --from=build --chown=app:app /app .
USER app
# Jalur default kunci penandatangan lisensi: wajib di-mount volume agar token
# tidak berubah kuncinya tiap container diganti (lihat docs/deploy-server.md).
ENV LISENSI_KEY_PATH=/lisensi/keys/lisensi-signing.key \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_gcServer=0
EXPOSE 8080
# Railway/Render memakai ini untuk membedakan "masih start" dari "macet".
# Endpoint /healthz selalu dijawab 200 selama proses hidup, apa pun state DB.
# Probe TCP murni (bukan /dev/tcp di dash) yang portable di image aspnet.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD dotnet exec /app/healthcheck.dll
ENTRYPOINT ["dotnet", "SuDesApp.Server.dll"]