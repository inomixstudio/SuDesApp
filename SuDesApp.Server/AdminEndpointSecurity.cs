using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SuDesApp.Server;

/// <summary>Durable sink for admin request audit events.</summary>
public interface IAdminAuditStore
{
    Task CatatAuditAdminAsync(AdminAuditEntry entry);
}

public static class AdminPermissions
{
    public const string SessionRead = "session.read";
    public const string CustomersRead = "customers.read";
    public const string CustomersCreate = "customers.create";
    public const string CustomersUpdate = "customers.update";
    public const string CustomersDelete = "customers.delete";
    public const string CustomersExport = "customers.export";
    public const string DevicesRead = "devices.read";
    public const string DevicesManage = "devices.manage";
    public const string PaymentsRead = "payments.read";
    public const string PaymentsRecord = "payments.record";
    public const string PaymentsExport = "payments.export";
    /// <summary>
    /// Uji pembayaran: membuat transaksi sungguhan di akun merchant.
    /// Izin ini TERPISAH dari <see cref="PaymentsRecord"/> karena yang satu
    /// menambah hak akses dari pembukuan manual, sedangkan yang ini
    /// membuat transaksi NYATA (uang sungguhan) — jadi tidak boleh ikut
    /// dibuka ke operator yang cuma berhak mencatat pembayaran.
    /// </summary>
    public const string PaymentsTest = "payments.test";
    public const string ReconciliationRun = "payments.reconcile";
    public const string AnnouncementsRead = "announcements.read";
    public const string AnnouncementsManage = "announcements.manage";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        AdminEndpointCatalog.Routes.Values, StringComparer.Ordinal);
}

/// <summary>Single declaration of each protected endpoint and its required permission.</summary>
public static class AdminEndpointCatalog
{
    public static IReadOnlyDictionary<(string Method, string Route), string> Routes { get; } =
        new Dictionary<(string, string), string>
        {
            [("GET", "/admin/sesi")] = AdminPermissions.SessionRead,
            [("GET", "/admin/pelanggan")] = AdminPermissions.CustomersRead,
            [("POST", "/admin/pelanggan")] = AdminPermissions.CustomersCreate,
            [("POST", "/admin/kode-aktivasi")] = AdminPermissions.CustomersCreate,
            [("GET", "/admin/pelanggan/{kode}")] = AdminPermissions.CustomersRead,
            [("PUT", "/admin/pelanggan/{kode}")] = AdminPermissions.CustomersUpdate,
            [("DELETE", "/admin/pelanggan/{kode}")] = AdminPermissions.CustomersDelete,
            [("GET", "/admin/pelanggan/{kode}/perangkat")] = AdminPermissions.DevicesRead,
            [("DELETE", "/admin/pelanggan/{kode}/perangkat")] = AdminPermissions.DevicesManage,
            [("DELETE", "/admin/pelanggan/{kode}/perangkat/{perangkat}")] = AdminPermissions.DevicesManage,
            [("POST", "/admin/pelanggan/{kode}/batas")] = AdminPermissions.DevicesManage,
            [("GET", "/admin/pelanggan/{kode}/pembayaran")] = AdminPermissions.PaymentsRead,
            [("GET", "/admin/pelanggan/{kode}/pembayaran/{orderId}/bukti")] = AdminPermissions.PaymentsRead,
            [("GET", "/admin/pelanggan/{kode}/pembayaran/{orderId}/kuitansi")] = AdminPermissions.PaymentsRead,
            [("POST", "/admin/pelanggan/{kode}/tandai-lunas")] = AdminPermissions.PaymentsRecord,
            [("GET", "/admin/ekspor/desa.csv")] = AdminPermissions.CustomersExport,
            [("GET", "/admin/ekspor/pembayaran/{kode}.csv")] = AdminPermissions.PaymentsExport,
            [("POST", "/admin/rekonsiliasi")] = AdminPermissions.ReconciliationRun,
            [("GET", "/admin/uji-pembayaran")] = AdminPermissions.PaymentsTest,
            [("POST", "/admin/uji-pembayaran")] = AdminPermissions.PaymentsTest,
            [("GET", "/admin/uji-pembayaran/{orderId}")] = AdminPermissions.PaymentsTest,
            [("GET", "/admin/uji-pembayaran/notifikasi")] = AdminPermissions.PaymentsTest,
            [("GET", "/admin/pengumuman")] = AdminPermissions.AnnouncementsRead,
            [("POST", "/admin/pengumuman")] = AdminPermissions.AnnouncementsManage,
            [("DELETE", "/admin/pengumuman/{id}")] = AdminPermissions.AnnouncementsManage
        };

    public static string? PermissionFor(string method, string route) =>
        Routes.TryGetValue((method.ToUpperInvariant(), route), out var permission) ? permission : null;
}

public sealed record AdminOperatorIdentity(string Id, string Name, IReadOnlySet<string> Permissions);

/// <summary>Loads named operator credentials and keeps only token digests in memory.</summary>
public sealed class AdminOperatorRegistry
{
    private sealed record Operator(string Id, string Name, byte[] TokenDigest, IReadOnlySet<string> Permissions);
    private readonly Dictionary<string, Operator> _operators;

    public AdminOperatorRegistry(IConfiguration configuration)
    {
        _operators = new Dictionary<string, Operator>(StringComparer.OrdinalIgnoreCase);
        var tokenDigests = new List<byte[]>();
        foreach (var section in configuration.GetSection("Lisensi:AdminOperators").GetChildren())
        {
            string id = section["Id"]?.Trim() ?? string.Empty;
            string name = section["Name"]?.Trim() ?? string.Empty;
            string token = section["Token"] ?? string.Empty;
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$") ||
                string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(char.IsControl) ||
                Encoding.UTF8.GetByteCount(token) < 32)
                throw new InvalidOperationException(
                    $"Operator admin '{section.Key}' harus memiliki Id, Name, dan Token minimal 32 byte.");

            var permissions = section.GetSection("Permissions").GetChildren()
                .Select(item => item.Value?.Trim() ?? string.Empty)
                .Where(item => item.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
            var tidakDikenal = permissions.Where(item => !AdminPermissions.All.Contains(item)).ToArray();
            if (tidakDikenal.Length > 0)
                throw new InvalidOperationException(
                    $"Operator '{id}' memiliki izin admin yang tidak dikenal: {string.Join(", ", tidakDikenal)}.");

            var tokenDigest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            if (tokenDigests.Any(existing => CryptographicOperations.FixedTimeEquals(existing, tokenDigest)))
                throw new InvalidOperationException($"Token operator admin harus unik; token untuk '{id}' sudah digunakan operator lain.");

            tokenDigests.Add(tokenDigest);
            var entry = new Operator(id, name, tokenDigest, permissions);
            if (!_operators.TryAdd(id, entry))
                throw new InvalidOperationException($"Id operator admin duplikat: '{id}'.");
        }
    }

    public bool Aktif => _operators.Count > 0;

    public AdminOperatorIdentity? Authenticate(string? id, string? token)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrEmpty(token)) return null;

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        if (!_operators.TryGetValue(id.Trim(), out var op))
        {
            // Keep token comparison work similar for unknown identities.
            _ = CryptographicOperations.FixedTimeEquals(digest, new byte[SHA256.HashSizeInBytes]);
            return null;
        }

        return CryptographicOperations.FixedTimeEquals(digest, op.TokenDigest)
            ? new AdminOperatorIdentity(op.Id, op.Name, op.Permissions)
            : null;
    }
}

/// <summary>Transport, authentication, permission and per-request audit boundary for admin APIs.</summary>
public static class AdminEndpointSecurity
{
    private static readonly object IdentityKey = new();

    public static IApplicationBuilder UseTransportSecurity(this IApplicationBuilder app, bool development)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers["Cache-Control"] = "private, no-store";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
            }

            if (!path.Equals("/healthz", StringComparison.OrdinalIgnoreCase) &&
                !TransportPermintaan.Aman(context.Request, context.Connection.RemoteIpAddress, development))
            {
                context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                context.Response.Headers["Upgrade"] = "TLS/1.2, HTTP/1.1";
                return;
            }

            await next();
        });
        return app;
    }

    public static IApplicationBuilder UseAdminOperatorSecurity(
        this IApplicationBuilder app, AdminOperatorRegistry operators)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var halamanAdmin = path.Equals("/admin", StringComparison.OrdinalIgnoreCase) &&
                               HttpMethods.IsGet(context.Request.Method);
            if (!path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase) || halamanAdmin)
            {
                await next();
                return;
            }

            var endpoint = context.GetEndpoint() as RouteEndpoint;
            string route = endpoint?.RoutePattern.RawText ?? "/admin/{unknown}";
            string? permission = AdminEndpointCatalog.PermissionFor(context.Request.Method, route);
            var identity = operators.Authenticate(
                context.Request.Headers["X-Admin-Operator"].ToString(),
                AmbilBearer(context.Request.Headers.Authorization.ToString()));

            if (identity is null)
            {
                context.Response.StatusCode = operators.Aktif
                    ? StatusCodes.Status401Unauthorized
                    : StatusCodes.Status503ServiceUnavailable;
                await AuditAsync(context, null, permission ?? "unknown", "deny", route);
                return;
            }

            bool sesiSendiri = permission == AdminPermissions.SessionRead;
            if (permission is null || (!sesiSendiri && !identity.Permissions.Contains(permission)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Items[IdentityKey] = identity;
                await AuditAsync(context, identity, permission ?? "unknown", "deny", route);
                return;
            }

            context.Items[IdentityKey] = identity;
            await AuditAsync(context, identity, permission, "started", route, StatusCodes.Status202Accepted);
            try
            {
                await next();
            }
            catch
            {
                await AuditAsync(context, identity, permission, "error", route, StatusCodes.Status500InternalServerError);
                throw;
            }

            await AuditAsync(context, identity, permission, context.Response.StatusCode < 400 ? "allow" : "error", route);
        });
        return app;
    }

    public static AdminOperatorIdentity? GetAdminOperator(this HttpContext context) =>
        context.Items.TryGetValue(IdentityKey, out var value) ? value as AdminOperatorIdentity : null;

    private static string? AmbilBearer(string authorization)
    {
        const string prefix = "Bearer ";
        return authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[prefix.Length..].Trim()
            : null;
    }

    private static async Task AuditAsync(
        HttpContext context, AdminOperatorIdentity? identity, string permission,
        string outcome, string route, int? statusOverride = null)
    {
        var entry = new AdminAuditEntry(
            identity?.Id, identity?.Name, permission, outcome, context.Request.Method,
            route, statusOverride ?? context.Response.StatusCode, context.TraceIdentifier);
        try
        {
            await context.RequestServices.GetRequiredService<IAdminAuditStore>().CatatAuditAdminAsync(entry);
        }
        catch (Exception ex)
        {
            var loggerKritis = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("SuDesApp.AdminAudit");
            ServerLog.AuditAdminGagal(loggerKritis, ex, context.TraceIdentifier);
            throw;
        }

        // "started" hanya dicatat ke penyimpanan audit: satu permintaan admin
        // sebelumnya menghasilkan DUA baris log (mulai + selesai), padahal log
        // konsol/berkas hanya perlu hasil akhirnya. Audit durable tetap lengkap.
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("SuDesApp.AdminAudit");
        if (!string.Equals(outcome, "started", StringComparison.Ordinal) &&
            logger.IsEnabled(LogLevel.Information))
        {
            ServerLog.AuditAdminTindakan(
                logger,
                outcome,
                identity?.Id ?? "anonymous",
                identity?.Name ?? "unknown",
                permission,
                context.Request.Method,
                route,
                statusOverride ?? context.Response.StatusCode,
                context.TraceIdentifier);
        }
    }
}
