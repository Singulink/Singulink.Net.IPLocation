using System.Net;

namespace Singulink.Net.IPLocation.Client;

/// <summary>
/// Interface for the Singulink IP Location API client. Errors that the service returns are thrown as <see cref="Http.Api.ApiException"/> types, such as a
/// <see cref="Http.Api.ServiceUnavailableApiException"/> with the <see cref="IPLocationErrorCodes.DatabasesNotLoaded"/> error code while the databases
/// aren't loaded.
/// </summary>
public interface IIPLocationClient
{
    /// <summary>
    /// Looks up the approximate location and network of an IP address.
    /// </summary>
    /// <param name="address">The address to look up.</param>
    /// <param name="includeHostName">Whether to also look up the address's host name with reverse DNS, which can take up to 2 seconds.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public Task<IPLocationResult> LookupAsync(IPAddress address, bool includeHostName = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the approximate locations and networks of several IP addresses. The results are in the same order as the addresses. Lookups of more than
    /// <see cref="IPLocationLimits.MaxBatchSize"/> addresses are sent in several requests.
    /// </summary>
    /// <param name="addresses">The addresses to look up.</param>
    /// <param name="includeHostName">Whether to also look up the addresses' host names with reverse DNS.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public Task<IReadOnlyList<IPLocationResult>> LookupAsync(
        IEnumerable<IPAddress> addresses, bool includeHostName = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the loaded databases and the credit their license requires.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public Task<DataInfo> GetInfoAsync(CancellationToken cancellationToken = default);
}
