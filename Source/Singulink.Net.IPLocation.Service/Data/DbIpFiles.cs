using System.Globalization;

namespace Singulink.Net.IPLocation.Service.Data;

/// <summary>
/// Names DB-IP's monthly database files, e.g. <c>dbip-city-lite-2026-10.mmdb</c>.
/// </summary>
public static class DbIpFiles
{
    public static readonly DbIpDatabaseKind[] Kinds = [DbIpDatabaseKind.City, DbIpDatabaseKind.Asn];

    private const string Extension = ".mmdb";

    public static string GetPrefix(DbIpDatabaseKind kind) => kind switch {
        DbIpDatabaseKind.City => "dbip-city-lite-",
        DbIpDatabaseKind.Asn => "dbip-asn-lite-",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Gets the text that the database type in a file's metadata must contain, e.g. <c>DBIP-City-Lite</c>.
    /// </summary>
    public static string GetDatabaseType(DbIpDatabaseKind kind) => kind switch {
        DbIpDatabaseKind.City => "DBIP-City-Lite",
        DbIpDatabaseKind.Asn => "DBIP-ASN-Lite",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string GetFileName(DbIpDatabaseKind kind, DateOnly month) =>
        GetPrefix(kind) + month.ToString("yyyy-MM", CultureInfo.InvariantCulture) + Extension;

    public static Uri GetDownloadUri(Uri baseAddress, DbIpDatabaseKind kind, DateOnly month) => new(baseAddress, GetFileName(kind, month) + ".gz");

    /// <summary>
    /// Gets the month of a database file from its name, or <see langword="null"/> if it is not a file of that kind.
    /// </summary>
    public static DateOnly? GetMonth(DbIpDatabaseKind kind, string fileName)
    {
        string prefix = GetPrefix(kind);

        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            return null;

        string month = fileName[prefix.Length..^Extension.Length];
        return DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result : null;
    }

    /// <summary>
    /// Gets the first day of the month that <paramref name="utcNow"/> falls in.
    /// </summary>
    public static DateOnly GetMonth(DateTime utcNow) => new(utcNow.Year, utcNow.Month, 1);
}
