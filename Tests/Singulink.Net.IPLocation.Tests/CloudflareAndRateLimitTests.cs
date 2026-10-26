using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Singulink.Net.Http.Api.Client;
using Singulink.Net.Http.Api.Service.Testing;

namespace Singulink.Net.IPLocation.Tests;

/// <summary>
/// Tests the client addresses that Cloudflare forwards, the per-client rate limit and the error responses, with requests from chosen addresses, and the
/// client's errors. The databases aren't loaded, so the requests go to <c>/Info</c>.
/// </summary>
[TestClass]
public class CloudflareAndRateLimitTests
{
    private const string RejectionMessage = "Too many requests. Please wait a moment and try again.";

    // Half a minute into a rate limit window. The fake clock stands still, so windows don't end during a test.
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 30, DateTimeKind.Utc);

    // In the hosts' saved copy of Cloudflare's ranges.
    private static readonly IPAddress CloudflareAddress = IPAddress.Parse("173.245.48.1");

    private static readonly IPAddress PublicClient = IPAddress.Parse("203.0.113.7");
    private static readonly IPAddress OtherPublicClient = IPAddress.Parse("198.51.100.7");

    [TestMethod]
    public async Task SavedRangesInTheDataDirectory_AreUsed()
    {
        using var host = new ServiceTestHost(Now, autoUpdate: false, null);

        HttpContext context = await SendAsync(host, IPAddress.Parse("2400:cb00::1"), forwardedFor: PublicClient);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        context.Connection.RemoteIpAddress.ShouldBe(PublicClient);
    }

    [TestMethod]
    public async Task DirectConnection_KeepsItsAddressAndLosesCloudflareHeaders()
    {
        using var host = new ServiceTestHost(Now, autoUpdate: false, null);

        HttpContext context = await SendAsync(host, OtherPublicClient, forwardedFor: PublicClient);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        context.Connection.RemoteIpAddress.ShouldBe(OtherPublicClient);
        context.Request.Headers.ContainsKey("CF-Connecting-IP").ShouldBeFalse();
    }

    [TestMethod]
    public async Task PublicClients_AreLimitedSeparately()
    {
        using var host = new ServiceTestHost(Now, autoUpdate: false, null, settings: new() { ["RateLimits:PermitLimit"] = "3" });

        for (int i = 0; i < 3; i++)
            (await SendAsync(host, PublicClient)).Response.StatusCode.ShouldBe(StatusCodes.Status200OK);

        HttpContext rejected = await SendAsync(host, PublicClient);

        rejected.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        rejected.Response.ContentType.ShouldBe("text/plain; charset=utf-8; format=error-code");
        rejected.Response.Headers.RetryAfter.ToString().ShouldBe("30");
        (await ReadBodyAsync(rejected)).ShouldBe("[rate-limited] " + RejectionMessage);

        (await SendAsync(host, OtherPublicClient)).Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [TestMethod]
    public async Task DefaultLimit_Is50RequestsPerMinute()
    {
        using var host = new ServiceTestHost(Now, autoUpdate: false, null);

        for (int i = 0; i < 50; i++)
            (await SendAsync(host, PublicClient)).Response.StatusCode.ShouldBe(StatusCodes.Status200OK);

        (await SendAsync(host, PublicClient)).Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
    }

    [TestMethod]
    [DataRow("10.1.2.3")]
    [DataRow("172.16.0.9")]
    [DataRow("192.168.1.20")]
    [DataRow("127.0.0.1")]
    [DataRow("::1")]
    [DataRow("fd00::5")]
    [DataRow("::ffff:10.1.2.3")]
    public async Task PrivateClients_AreExempt(string address)
    {
        using var host = new ServiceTestHost(Now, autoUpdate: false, null, settings: new() { ["RateLimits:PermitLimit"] = "3" });

        for (int i = 0; i < 10; i++)
            (await SendAsync(host, IPAddress.Parse(address))).Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [TestMethod]
    public async Task ClientsThroughCloudflare_AreLimitedByTheirOwnAddress()
    {
        using var host = new ServiceTestHost(Now, autoUpdate: false, null, settings: new() { ["RateLimits:PermitLimit"] = "2" });

        for (int i = 0; i < 2; i++)
            (await SendAsync(host, CloudflareAddress, forwardedFor: PublicClient)).Response.StatusCode.ShouldBe(StatusCodes.Status200OK);

        (await SendAsync(host, CloudflareAddress, forwardedFor: PublicClient)).Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        (await SendAsync(host, CloudflareAddress, forwardedFor: OtherPublicClient)).Response.StatusCode.ShouldBe(StatusCodes.Status200OK);

        // An internal app that reaches the service through Cloudflare from a private address is exempt too.
        for (int i = 0; i < 5; i++)
        {
            HttpContext context = await SendAsync(host, CloudflareAddress, forwardedFor: IPAddress.Parse("10.0.0.8"));
            context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        }
    }

    [TestMethod]
    public async Task NotReady_Returns503WithRetryAfter()
    {
        using var host = new ServiceTestHost(Now, autoUpdate: false, null);

        using var response = await host.HttpClient.GetAsync("/Lookup?ip=8.8.8.8");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromMinutes(1));
        response.Content.Headers.ContentType.ShouldNotBeNull().ToString().ShouldBe("text/plain; charset=utf-8; format=error-code");
        (await response.Content.ReadAsStringAsync()).ShouldBe(
            "[databases-not-loaded] The IP location data is still being downloaded. Please try again in a few minutes.");
    }

    [TestMethod]
    public async Task OpenApi_DescribesErrorResponsesWithTheirErrorCodes()
    {
        using var host = new ServiceTestHost(Now, autoUpdate: false, null);

        JsonElement paths = JsonDocument.Parse(await host.HttpClient.GetStringAsync("/swagger/v1/swagger.json")).RootElement.GetProperty("paths");

        foreach (string method in (string[])["get", "post"])
        {
            JsonElement responses = paths.GetProperty("/Lookup").GetProperty(method).GetProperty("responses");

            string[] statusCodes = method is "get" ? ["200", "400", "429", "503"] : ["200", "400", "422", "429", "503"];
            responses.EnumerateObject().Select(r => r.Name).ShouldBe(statusCodes, ignoreOrder: true);
            GetDescription(responses, "400").ShouldNotBeNullOrEmpty();
            GetDescription(responses, "429").ShouldContain("`[rate-limited]`");
            GetDescription(responses, "503").ShouldSatisfyAllConditions(
                d => d.ShouldContain("`[databases-not-loaded]`"),
                d => d.ShouldContain("`[cloudflare-ranges-unavailable]`"),
                d => d.ShouldContain("Retry-After"));
        }

        JsonElement infoResponses = paths.GetProperty("/Info").GetProperty("get").GetProperty("responses");

        infoResponses.EnumerateObject().Select(r => r.Name).ShouldBe(["200", "429", "503"], ignoreOrder: true);
        GetDescription(infoResponses, "503").ShouldContain("`[cloudflare-ranges-unavailable]`");
        GetDescription(infoResponses, "503").ShouldNotContain("databases-not-loaded");

        static string GetDescription(JsonElement responses, string statusCode) =>
            responses.GetProperty(statusCode).GetProperty("description").GetString().ShouldNotBeNull();
    }

    [TestMethod]
    public async Task Client_RateLimited_ThrowsTooManyRequests()
    {
        // Test server connections have no address, which counts as a private one.
        using var host = new ServiceTestHost(Now, autoUpdate: false, null, settings: new() {
            ["RateLimits:PermitLimit"] = "1",
            ["RateLimits:ExemptPrivateNetworks"] = "false",
        });

        var notReady = await Should.ThrowAsync<ServiceUnavailableApiException>(() => host.Client.LookupAsync(IPAddress.Parse("8.8.8.8")));
        var rejected = await Should.ThrowAsync<TooManyRequestsApiException>(() => host.Client.LookupAsync(IPAddress.Parse("8.8.8.8")));

        notReady.ErrorCode.ShouldBe(IPLocationErrorCodes.DatabasesNotLoaded);
        rejected.ErrorCode.ShouldBe(ApiErrorCodes.RateLimited);
        rejected.Message.ShouldBe(RejectionMessage);
        rejected.RetryAfter.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [TestMethod]
    public async Task Client_NoCloudflareRanges_ThrowsServiceUnavailable()
    {
        // As on a first start that can't download Cloudflare's lists.
        using var host = new ServiceTestHost(Now, autoUpdate: false, null, writeCloudflareRanges: false);

        var ex = await Should.ThrowAsync<ServiceUnavailableApiException>(() => host.Client.LookupAsync(IPAddress.Parse("8.8.8.8")));

        ex.ErrorCode.ShouldBe(ApiErrorCodes.CloudflareRangesUnavailable);
        ex.Message.ShouldBe("The service is starting. Please try again shortly.");
        ex.RetryAfter.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task Client_ConnectionFailure_IsTransient()
    {
        using var host = new ServiceTestHost(Now, autoUpdate: false, null);
        var faults = new FaultInjectionHandler(host.Server.CreateHandler()).FailBeforeSending(_ => true);
        var client = new IPLocationClient(new Uri("https://localhost/"), faults);

        var ex = await Should.ThrowAsync<HttpRequestException>(() => client.GetInfoAsync());
        ApiFailures.IsTransient(ex).ShouldBeTrue();
    }

    private static Task<HttpContext> SendAsync(ServiceTestHost host, IPAddress remoteAddress, IPAddress? forwardedFor = null)
    {
        return host.Server.SendAsync(context => {
            context.Request.Method = HttpMethods.Get;
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = "/Info";
            context.Connection.RemoteIpAddress = remoteAddress;

            if (forwardedFor is not null)
                context.Request.Headers["CF-Connecting-IP"] = forwardedFor.ToString();
        });
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }
}
