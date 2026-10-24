# SuDesApp lisensi server — image container untuk Railway/Render/VPS.
#
# Build lalu jalankan dengan environment variables (lihat docs/deploy-server.md):
#   SUPABASE_HOST, SUPABASE_SSLMODE
#   SUPABASE_DB_USER (role terbatas: sudes_server.<project-ref>) + SUPABASE_DB_PASSWORD
#   SUPABASE_MIGRATE_USER + SUPABASE_MIGRATE_PASSWORD (opsional — hanya untuk
#     menerapkan schema.sql saat start; tanpa itu skema tidak diubah)
#   MIDTRANS_SERVER_KEY, MIDTRANS_SANDBOX, SUDES_ADMIN_OPERATORS__0__TOKEN, LISENSI_KEY_PATH
#   ASPNETCORE_ENVIRONMENT=Production, ASPNETCORE_URLS=http://+:8080
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SuDesApp.Server/SuDesApp.Server.csproj SuDesApp.Server/
RUN dotnet restore SuDesApp.Server/SuDesApp.Server.csproj
# Perhatikan: global.json ikut disalin agar SDK container PERSIS sama dengan
# SDK yang dipakai CI dan mesin developer (version 10.0.401, rollForward disable).
# Tanpa ini, tag mengambang sdk:10.0 diam-diam memakai patch terbaru yang berbeda.
COPY global.json ./
COPY SuDesApp.Server/SuDesApp.Server.csproj SuDesApp.Server/
RUN dotnet restore SuDesApp.Server/SuDesApp.Server.csproj
COPY SuDesApp.Server/ SuDesApp.Server/
RUN dotnet publish SuDesApp.Server/SuDesApp.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
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
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD ["/bin/sh", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /healthz HTTP/1.0\\r\\n\\r\\n' >&3 && head -1 <&3 | grep -q 200"]
ENTRYPOINT ["dotnet", "SuDesApp.Server.dll"]