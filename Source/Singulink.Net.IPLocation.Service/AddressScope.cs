using System.Net;
using System.Net.Sockets;

namespace Singulink.Net.IPLocation.Service;

/// <summary>
/// Tells public addresses from ones that are not routable on the internet, which have no location.
/// </summary>
public static class AddressScope
{
    // Special-purpose ranges that are not globally reachable, from the IANA IPv4 and IPv6 special-purpose address registries.
    private static readonly IPNetwork[] NonPublicNetworks = [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"), // Shared address space (carrier-grade NAT)
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"), // Documentation
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"), // Benchmarking
        IPNetwork.Parse("198.51.100.0/24"), // Documentation
        IPNetwork.Parse("203.0.113.0/24"), // Documentation
        IPNetwork.Parse("224.0.0.0/4"), // Multicast
        IPNetwork.Parse("240.0.0.0/4"), // Reserved, and the broadcast address
        IPNetwork.Parse("::/127"), // Unspecified and loopback
        IPNetwork.Parse("100::/64"), // Discard-only
        IPNetwork.Parse("2001:db8::/32"), // Documentation
        IPNetwork.Parse("fc00::/7"), // Unique local
        IPNetwork.Parse("fe80::/10"), // Link-local
        IPNetwork.Parse("ff00::/8"), // Multicast
    ];

    /// <summary>
    /// Returns the canonical form of an address: IPv4 addresses mapped to IPv6 become IPv4, and IPv6 scope IDs are removed.
    /// </summary>
    public static IPAddress Normalize(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            return address.MapToIPv4();

        if (address.AddressFamily is AddressFamily.InterNetworkV6 && address.ScopeId is not 0)
            return new IPAddress(address.GetAddressBytes());

        return address;
    }

    /// <summary>
    /// Determines whether a normalized address is not publicly routable (private, loopback, link-local, shared, documentation, multicast or reserved).
    /// </summary>
    public static bool IsPrivate(IPAddress address)
    {
        foreach (var network in NonPublicNetworks)
        {
            if (network.Contains(address))
                return true;
        }

        return false;
    }
}
