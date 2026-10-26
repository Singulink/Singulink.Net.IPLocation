using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Caching.Memory;

namespace Singulink.Net.IPLocation.Service;

/// <summary>
/// Looks up the host names of public addresses with reverse DNS. Lookups are cached, limited in number at a time, and given up after 2 seconds; a
/// lookup that timed out is only cached briefly, since the next one may well succeed.
/// </summary>
public sealed class HostNameResolver(IMemoryCache cache) : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan TimeoutCacheDuration = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _concurrencyLimit = new(16);

    /// <summary>
    /// Gets the host name of a normalized public address, or <see langword="null"/> if it has none or the lookup failed. Private addresses are never
    /// looked up, so that a public service does not reveal the names on its own network.
    /// </summary>
    public async Task<string?> GetHostNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        if (AddressScope.IsPrivate(address))
            return null;

        var key = new CacheKey(address);

        if (cache.TryGetValue(key, out string? cached))
            return cached;

        string? hostName = null;
        var cacheDuration = CacheDuration;
        await _concurrencyLimit.WaitAsync(cancellationToken);

        try
        {
            var entry = await Dns.GetHostEntryAsync(address).WaitAsync(Timeout, cancellationToken);

            // Without a PTR record the "host name" is the address itself.
            if (entry.HostName.Length > 0 && !IPAddress.TryParse(entry.HostName, out _))
                hostName = entry.HostName;
        }
        catch (TimeoutException)
        {
            cacheDuration = TimeoutCacheDuration;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            // No host name.
        }
        finally
        {
            _concurrencyLimit.Release();
        }

        cache.Set(key, hostName, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = cacheDuration, Size = 1 });
        return hostName;
    }

    public void Dispose() => _concurrencyLimit.Dispose();

    private readonly record struct CacheKey(IPAddress Address);
}
