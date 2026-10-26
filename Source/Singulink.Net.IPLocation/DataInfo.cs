namespace Singulink.Net.IPLocation;

/// <summary>
/// Describes the loaded databases and the credit their license requires.
/// </summary>
/// <param name="City">The city database, or <see langword="null"/> if it is not loaded yet.</param>
/// <param name="Asn">The ASN database, or <see langword="null"/> if it is not loaded yet.</param>
/// <param name="Attribution">The credit text to show with results.</param>
/// <param name="AttributionUrl">The address the credit links to.</param>
/// <param name="License">The license of the data.</param>
public sealed record DataInfo(DatabaseInfo? City, DatabaseInfo? Asn, string Attribution, string AttributionUrl, string License);

/// <summary>
/// Describes a loaded database.
/// </summary>
/// <param name="Month">The monthly release, e.g. <c>2026-10</c>.</param>
/// <param name="BuildDateUtc">When the database was built.</param>
/// <param name="DatabaseType">The database type from the file's metadata.</param>
public sealed record DatabaseInfo(string Month, DateTime BuildDateUtc, string DatabaseType);
