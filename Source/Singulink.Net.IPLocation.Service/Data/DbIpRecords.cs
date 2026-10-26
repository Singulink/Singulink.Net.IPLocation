using MaxMind.Db;

namespace Singulink.Net.IPLocation.Service.Data;

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1649 // File name should match first type name

// Records as stored in DB-IP's databases, which use the same layout as MaxMind's GeoIP2 and GeoLite2 databases. Only the fields the service
// returns are read.

internal sealed class CityRecord
{
    [Constructor]
    public CityRecord(
        [MapKey("city")] NamedRecord? city = null,
        [MapKey("continent")] ContinentRecord? continent = null,
        [MapKey("country")] CountryRecord? country = null,
        [MapKey("location")] LocationRecord? location = null,
        [MapKey("subdivisions")] List<NamedRecord>? subdivisions = null)
    {
        City = city;
        Continent = continent;
        Country = country;
        Location = location;
        Subdivisions = subdivisions;
    }

    public NamedRecord? City { get; }

    public ContinentRecord? Continent { get; }

    public CountryRecord? Country { get; }

    public LocationRecord? Location { get; }

    public List<NamedRecord>? Subdivisions { get; }
}

internal class NamedRecord
{
    [Constructor]
    public NamedRecord([MapKey("names")] Dictionary<string, string>? names = null)
    {
        Names = names;
    }

    public Dictionary<string, string>? Names { get; }

    public string? EnglishName => Names?.GetValueOrDefault("en");
}

internal sealed class ContinentRecord : NamedRecord
{
    [Constructor]
    public ContinentRecord([MapKey("code")] string? code = null, [MapKey("names")] Dictionary<string, string>? names = null) : base(names)
    {
        Code = code;
    }

    public string? Code { get; }
}

internal sealed class CountryRecord : NamedRecord
{
    [Constructor]
    public CountryRecord([MapKey("iso_code")] string? isoCode = null, [MapKey("names")] Dictionary<string, string>? names = null) : base(names)
    {
        IsoCode = isoCode;
    }

    public string? IsoCode { get; }
}

internal sealed class LocationRecord
{
    [Constructor]
    public LocationRecord([MapKey("latitude")] double? latitude = null, [MapKey("longitude")] double? longitude = null)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double? Latitude { get; }

    public double? Longitude { get; }
}

internal sealed class AsnRecord
{
    [Constructor]
    public AsnRecord(
        [MapKey("autonomous_system_number")] long? number = null,
        [MapKey("autonomous_system_organization")] string? organization = null)
    {
        Number = number;
        Organization = organization;
    }

    public long? Number { get; }

    public string? Organization { get; }
}
