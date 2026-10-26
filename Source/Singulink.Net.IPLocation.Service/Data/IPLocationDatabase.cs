using System.Net;

namespace Singulink.Net.IPLocation.Service.Data;

/// <summary>
/// Looks up addresses in the loaded databases. A newly downloaded database replaces the old one without interrupting lookups: the old one is
/// disposed a minute later, once lookups that started with it have finished.
/// </summary>
public sealed class IPLocationDatabase : IDisposable
{
    private static readonly TimeSpan DisposeDelay = TimeSpan.FromMinutes(1);

    private LoadedDatabase? _city;
    private LoadedDatabase? _asn;

    /// <summary>
    /// Gets a value indicating whether the city database is loaded, which lookups need. The ASN database is optional: without it, results have no
    /// network information.
    /// </summary>
    public bool IsReady => Volatile.Read(ref _city) is not null;

    public LoadedDatabase? Get(DbIpDatabaseKind kind) => kind switch {
        DbIpDatabaseKind.City => Volatile.Read(ref _city),
        DbIpDatabaseKind.Asn => Volatile.Read(ref _asn),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Makes a database the one used for lookups of its kind.
    /// </summary>
    public void Replace(LoadedDatabase database)
    {
        var previous = database.Kind switch {
            DbIpDatabaseKind.City => Interlocked.Exchange(ref _city, database),
            DbIpDatabaseKind.Asn => Interlocked.Exchange(ref _asn, database),
            _ => throw new ArgumentOutOfRangeException(nameof(database)),
        };

        if (previous is not null)
            _ = DisposeLaterAsync(previous);
    }

    /// <summary>
    /// Looks up an address. Private addresses are not looked up.
    /// </summary>
    public IPLocationResult Lookup(IPAddress address)
    {
        address = AddressScope.Normalize(address);
        string addressText = address.ToString();

        if (AddressScope.IsPrivate(address))
            return new IPLocationResult { IPAddress = addressText, IsPrivate = true };

        var city = Volatile.Read(ref _city)?.Reader.Find<CityRecord>(address);
        var asn = Volatile.Read(ref _asn)?.Reader.Find<AsnRecord>(address);

        return new IPLocationResult {
            IPAddress = addressText,
            ContinentCode = city?.Continent?.Code,
            ContinentName = city?.Continent?.EnglishName,
            CountryCode = city?.Country?.IsoCode,
            CountryName = city?.Country?.EnglishName,
            Region = city?.Subdivisions is [var region, ..] ? region.EnglishName : null,
            City = city?.City?.EnglishName,
            Latitude = city?.Location?.Latitude,
            Longitude = city?.Location?.Longitude,
            AutonomousSystemNumber = asn?.Number,
            Organization = asn?.Organization,
        };
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _city, null)?.Dispose();
        Interlocked.Exchange(ref _asn, null)?.Dispose();
    }

    private static async Task DisposeLaterAsync(LoadedDatabase database)
    {
        await Task.Delay(DisposeDelay).ConfigureAwait(false);
        database.Dispose();
    }
}
