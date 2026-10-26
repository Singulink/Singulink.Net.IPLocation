namespace Singulink.Net.IPLocation;

/// <summary>
/// Represents the approximate location and network of an IP address. Location and network properties are <see langword="null"/> when they are not
/// known, which is always the case for private addresses.
/// </summary>
public record IPLocationResult
{
    /// <summary>
    /// Gets the IP address that was looked up, in its canonical form (IPv4 addresses mapped to IPv6 are shown as IPv4).
    /// </summary>
    public required string IPAddress { get; init; }

    /// <summary>
    /// Gets a value indicating whether the address is not publicly routable (for example a private network, loopback or link-local address), in which
    /// case it has no location.
    /// </summary>
    public bool IsPrivate { get; init; }

    /// <summary>
    /// Gets the two-letter continent code, e.g. <c>NA</c> for North America.
    /// </summary>
    public string? ContinentCode { get; init; }

    /// <summary>
    /// Gets the continent name in English.
    /// </summary>
    public string? ContinentName { get; init; }

    /// <summary>
    /// Gets the ISO 3166-1 alpha-2 country code, e.g. <c>CA</c>.
    /// </summary>
    public string? CountryCode { get; init; }

    /// <summary>
    /// Gets the country name in English.
    /// </summary>
    public string? CountryName { get; init; }

    /// <summary>
    /// Gets the name of the region (state, province or other first-level subdivision of the country) in English.
    /// </summary>
    public string? Region { get; init; }

    /// <summary>
    /// Gets the city name in English.
    /// </summary>
    public string? City { get; init; }

    /// <summary>
    /// Gets the approximate latitude of the location.
    /// </summary>
    public double? Latitude { get; init; }

    /// <summary>
    /// Gets the approximate longitude of the location.
    /// </summary>
    public double? Longitude { get; init; }

    /// <summary>
    /// Gets the number of the autonomous system (the network) that the address belongs to.
    /// </summary>
    public long? AutonomousSystemNumber { get; init; }

    /// <summary>
    /// Gets the name of the organization that operates the network the address belongs to, usually an internet service provider, a mobile carrier or a
    /// hosting company.
    /// </summary>
    public string? Organization { get; init; }

    /// <summary>
    /// Gets the host name that the address resolves to with a reverse DNS lookup. Only looked up when requested.
    /// </summary>
    public string? HostName { get; init; }

    /// <summary>
    /// Gets the location as display text, e.g. <c>"Toronto, Ontario, Canada"</c>, or <see langword="null"/> if it is not known. Parts that repeat the
    /// previous one (such as a city-state's region) are left out.
    /// </summary>
    public string? GetDisplayLocation()
    {
        var parts = new List<string>(3);

        foreach (string? part in new[] { City, Region, CountryName })
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            if (parts.Count > 0 && string.Equals(parts[parts.Count - 1], part, StringComparison.OrdinalIgnoreCase))
                continue;

            parts.Add(part!);
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }
}
