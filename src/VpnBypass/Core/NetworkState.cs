using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VpnBypass.Core;

/// <summary>Which IP families can actually leave this machine. Cached, refreshed on address changes.</summary>
public static class NetworkState
{
    private static (bool V4, bool V6)? _cached;

    static NetworkState()
    {
        NetworkChange.NetworkAddressChanged += (_, _) => _cached = null;
        NetworkChange.NetworkAvailabilityChanged += (_, _) => _cached = null;
    }

    public static bool HasIpv4 => Probe().V4;
    public static bool HasIpv6 => Probe().V6;

    /// <summary>Usable addresses first: IPv4 (most reliable) then IPv6; unusable families dropped.</summary>
    public static IEnumerable<IPAddress> Order(IEnumerable<IPAddress> addresses)
    {
        var (v4, v6) = Probe();
        var list = addresses.ToList();
        var ordered = list.Where(a => a.AddressFamily == AddressFamily.InterNetwork && v4)
            .Concat(list.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6 && v6))
            .ToList();
        // If detection is wrong, still try everything rather than nothing.
        return ordered.Count > 0 ? ordered : list;
    }

    private static (bool V4, bool V6) Probe()
    {
        if (_cached is { } c) return c;
        bool v4 = false, v6 = false;
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var a in n.GetIPProperties().UnicastAddresses.Select(u => u.Address))
                {
                    // 169.254.x.x means DHCP failed; link-local/ULA IPv6 can't reach the internet.
                    if (a.AddressFamily == AddressFamily.InterNetwork && !(a.GetAddressBytes() is [169, 254, ..]))
                        v4 = true;
                    else if (a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6LinkLocal && !a.IsIPv6SiteLocal && !a.IsIPv6UniqueLocal && !IPAddress.IsLoopback(a))
                        v6 = true;
                }
            }
        }
        catch
        {
            v4 = v6 = true; // Unknown: allow everything.
        }
        _cached = (v4, v6);
        return (v4, v6);
    }
}
