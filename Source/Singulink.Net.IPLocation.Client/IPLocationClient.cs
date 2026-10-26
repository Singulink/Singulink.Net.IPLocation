using System.Net;
using Singulink.Net.Http.Api.Client;

namespace Singulink.Net.IPLocation.Client;

/// <summary>
/// Client for the Singulink IP Location API. Errors that the service returns are thrown as <see cref="Http.Api.ApiException"/> types, with the error code
/// and the <c>Retry-After</c> time that the service sent (see <see cref="IPLocationErrorCodes"/>).
/// </summary>
/// <remarks>
/// The data comes from DB-IP, whose license requires apps that show results to credit DB-IP with a link: see <see cref="DataAttribution"/>.
/// </remarks>
public sealed class IPLocationClient : ApiClientBase, IIPLocationClient
{
    private readonly Uri _baseAddress;

    /// <summary>
    /// Initializes a new instance of the <see cref="IPLocationClient"/> class for the service at the specified address.
    /// </summary>
    public IPLocationClient(Uri baseAddress) : base(IPLocationJsonSerializerContext.Default)
    {
        _baseAddress = baseAddress;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IPLocationClient"/> class for the service at the specified address, which sends its requests through
    /// the specified handler instead of the handler that all clients share. The client doesn't dispose the handler.
    /// </summary>
    public IPLocationClient(Uri baseAddress, HttpMessageHandler handler) : this(baseAddress)
    {
        HttpHandler = handler;
    }

    /// <inheritdoc/>
    public Task<IPLocationResult> LookupAsync(IPAddress address, bool includeHostName = false, CancellationToken cancellationToken = default)
    {
        return GetAsync<IPLocationResult>("Lookup", [("ip", address.ToString()), ("includeHostName", includeHostName ? "true" : null)], cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IPLocationResult>> LookupAsync(
        IEnumerable<IPAddress> addresses, bool includeHostName = false, CancellationToken cancellationToken = default)
    {
        string[] allAddresses = [.. addresses.Select(a => a.ToString())];
        var results = new List<IPLocationResult>(allAddresses.Length);

        for (int start = 0; start < allAddresses.Length; start += IPLocationLimits.MaxBatchSize)
        {
            string[] batch = [.. allAddresses.Skip(start).Take(IPLocationLimits.MaxBatchSize)];
            var batchResults = await PostAsync<IPLocationResult[]>("Lookup", batch, [("includeHostName", includeHostName ? "true" : null)], cancellationToken)
                .ConfigureAwait(false);

            results.AddRange(batchResults);
        }

        return results;
    }

    /// <inheritdoc/>
    public Task<DataInfo> GetInfoAsync(CancellationToken cancellationToken = default) => GetAsync<DataInfo>("Info", cancellationToken);

    /// <inheritdoc/>
    protected override Uri GetBaseAddress() => _baseAddress;
}
