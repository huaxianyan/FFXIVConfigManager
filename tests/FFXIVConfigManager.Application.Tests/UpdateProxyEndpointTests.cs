using FFXIVConfigManager.Application.Updates;

namespace FFXIVConfigManager.Application.Tests;

public sealed class UpdateProxyEndpointTests
{
    [Fact]
    public void Create_LocalProxyProtocolsReturnCanonicalEndpoints()
    {
        var inputs = new[]
        {
            ("http", "localhost", 7890),
            ("https", "127.0.0.1", 7890),
            ("socks4", "127.0.0.2", 1080),
            ("socks4a", "localhost", 1080),
            ("socks5", "::1", 1080),
        };

        var endpoints = inputs
            .Select(input => UpdateProxyEndpoint.Create(input.Item1, input.Item2, input.Item3))
            .ToArray();

        Assert.All(endpoints, endpoint =>
            Assert.EndsWith("/", endpoint.Address, StringComparison.Ordinal));
    }

    [Fact]
    public void Create_NonLocalHostOrInvalidPortIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            UpdateProxyEndpoint.Create("socks5", "192.168.1.2", 1080));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UpdateProxyEndpoint.Create("http", "127.0.0.1", 0));
    }
}
