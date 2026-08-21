using System.Net;

namespace FFXIVConfigManager.Application.Updates;

public sealed class UpdateProxyEndpoint
{
    public static IReadOnlyList<string> SupportedSchemes { get; } =
        ["http", "https", "socks4", "socks4a", "socks5"];

    private UpdateProxyEndpoint(string scheme, string host, int port)
    {
        Scheme = scheme;
        Host = host;
        Port = port;
        Address = new UriBuilder(scheme, host, port).Uri.AbsoluteUri;
    }

    public string Scheme { get; }

    public string Host { get; }

    public int Port { get; }

    public string Address { get; }

    public static UpdateProxyEndpoint Create(string? scheme, string? host, int port)
    {
        var normalizedScheme = scheme?.Trim().ToLowerInvariant();
        if (normalizedScheme is null || !SupportedSchemes.Contains(normalizedScheme))
        {
            throw new ArgumentException("请选择支持的代理协议。", nameof(scheme));
        }

        var normalizedHost = host?.Trim();
        if (string.IsNullOrEmpty(normalizedHost) || !IsLoopbackHost(normalizedHost))
        {
            throw new ArgumentException(
                "代理必须位于本机，请输入 localhost、127.0.0.1 或 ::1。",
                nameof(host));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "请输入 1～65535 之间的端口。");
        }

        return new UpdateProxyEndpoint(normalizedScheme, normalizedHost, port);
    }

    public static UpdateProxyEndpoint Parse(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) ||
            !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (uri.AbsolutePath.Length > 1 && uri.AbsolutePath != "/") ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("代理设置内容无效，请重新选择协议并填写本机 IP 和端口。", nameof(address));
        }

        return Create(uri.Scheme, uri.Host, uri.Port);
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
}

public interface IApplicationUpdateProxy
{
    void Configure(string? address);
}

public interface IApplicationUpdateProxyTester
{
    Task<TimeSpan> TestAsync(
        string address,
        CancellationToken cancellationToken = default);
}
