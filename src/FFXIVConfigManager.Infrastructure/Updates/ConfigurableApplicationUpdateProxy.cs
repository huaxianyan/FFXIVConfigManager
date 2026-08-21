using System.Net;
using FFXIVConfigManager.Application.Updates;

namespace FFXIVConfigManager.Infrastructure.Updates;

public sealed class ConfigurableApplicationUpdateProxy : IWebProxy, IApplicationUpdateProxy
{
    private Uri? _proxyUri;

    public ICredentials? Credentials
    {
        get => null;
        set
        {
            if (value is not null)
            {
                throw new NotSupportedException("当前不支持需要账号或密码的代理。");
            }
        }
    }

    public void Configure(string? address)
    {
        var proxyUri = address is null
            ? null
            : new Uri(UpdateProxyEndpoint.Parse(address).Address, UriKind.Absolute);
        Volatile.Write(ref _proxyUri, proxyUri);
    }

    public Uri GetProxy(Uri destination) =>
        Volatile.Read(ref _proxyUri) ?? destination;

    public bool IsBypassed(Uri host) => Volatile.Read(ref _proxyUri) is null;
}
