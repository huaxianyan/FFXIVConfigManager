using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using FFXIVConfigManager.Application.Updates;

namespace FFXIVConfigManager.Infrastructure.Updates;

public sealed class ApplicationUpdateProxyTester : IApplicationUpdateProxyTester
{
    private static readonly Uri TestUri = new(
        "https://api.github.com/repos/huaxianyan/FFXIVConfigManager/releases/latest");

    public async Task<TimeSpan> TestAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        var endpoint = UpdateProxyEndpoint.Parse(address);
        using var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy(endpoint.Address),
            UseProxy = true,
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FFXIVConfigManager", "proxy-test"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.GetAsync(
                TestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            stopwatch.Stop();
            return stopwatch.Elapsed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("代理测试超时，请确认代理正在运行并检查协议、IP 和端口。");
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "无法通过该代理连接 GitHub，请确认代理正在运行并允许访问 GitHub。",
                exception);
        }
    }
}
