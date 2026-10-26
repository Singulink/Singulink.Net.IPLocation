namespace Singulink.Net.IPLocation;

/// <summary>
/// Provides the error codes of the service's own errors, which tell apart errors with the same status code.
/// </summary>
public static class IPLocationErrorCodes
{
    /// <summary>
    /// The error code of a <c>503</c> response to a lookup while the databases aren't loaded, for example while they are downloaded after the service's
    /// first start.
    /// </summary>
    public const string DatabasesNotLoaded = "databases-not-loaded";
}
