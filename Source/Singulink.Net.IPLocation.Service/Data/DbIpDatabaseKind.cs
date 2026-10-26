namespace Singulink.Net.IPLocation.Service.Data;

/// <summary>
/// The DB-IP databases the service uses.
/// </summary>
public enum DbIpDatabaseKind
{
    /// <summary>
    /// IP to City Lite: continent, country, region, city and coordinates.
    /// </summary>
    City,

    /// <summary>
    /// IP to ASN Lite: the autonomous system (network) number and the organization that operates it.
    /// </summary>
    Asn,
}
