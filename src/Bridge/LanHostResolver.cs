using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DgLabSocketSpire2.Bridge;

internal static class LanHostResolver
{
    public static string ResolvePreferredHost(string configuredHost)
    {
        if (!string.IsNullOrWhiteSpace(configuredHost))
        {
            return configuredHost.Trim();
        }

        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(addr => addr.Address)
                .Where(addr => addr.AddressFamily == AddressFamily.InterNetwork)
                .Where(addr => !IPAddress.IsLoopback(addr))
                .Select(addr => addr.ToString())
                .Where(addr => !addr.StartsWith("169.254.", StringComparison.Ordinal))
                .ToList();

            var preferred = candidates.FirstOrDefault(ip => ip.StartsWith("192.168.", StringComparison.Ordinal))
                ?? candidates.FirstOrDefault(ip => ip.StartsWith("10.", StringComparison.Ordinal))
                ?? candidates.FirstOrDefault(ip => ip.StartsWith("172.", StringComparison.Ordinal))
                ?? candidates.FirstOrDefault();

            return string.IsNullOrWhiteSpace(preferred) ? "127.0.0.1" : preferred;
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Failed to resolve LAN host automatically: {ex.Message}");
            return "127.0.0.1";
        }
    }
}
