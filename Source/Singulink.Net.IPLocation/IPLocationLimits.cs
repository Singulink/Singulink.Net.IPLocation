namespace Singulink.Net.IPLocation;

/// <summary>
/// Provides the limits of the service's requests.
/// </summary>
public static class IPLocationLimits
{
    /// <summary>
    /// The maximum number of addresses in one batch lookup. The service rejects a batch with more with a <c>422</c> error, and the client splits larger
    /// lookups into batches of this size.
    /// </summary>
    public const int MaxBatchSize = 100;
}
