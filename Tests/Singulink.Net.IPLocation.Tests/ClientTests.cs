using System.Text;
using System.Text.Json;
using Singulink.Net.Http.Api.Client;

namespace Singulink.Net.IPLocation.Tests;

/// <summary>
/// Tests the requests the client sends and how it reads the responses, which a fake handler sends, so they don't need the service or its data.
/// </summary>
[TestClass]
public class ClientTests
{
    private static readonly Uri BaseAddress = new("https://iplocation.example.com/");

    [TestMethod]
    [DataRow(false, "/Lookup?ip=8.8.8.8")]
    [DataRow(true, "/Lookup?ip=8.8.8.8&includeHostName=true")]
    public async Task Lookup_SendsTheAddress(bool includeHostName, string pathAndQuery)
    {
        var handler = new FakeHandler((_, _) => Json("""{"ipAddress":"8.8.8.8","isPrivate":false,"countryCode":"US"}"""));
        var client = new IPLocationClient(BaseAddress, handler);

        var result = await client.LookupAsync(IPAddress.Parse("8.8.8.8"), includeHostName);

        result.IPAddress.ShouldBe("8.8.8.8");
        result.CountryCode.ShouldBe("US");
        handler.Requests.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Get),
            r => r.RequestUri!.PathAndQuery.ShouldBe(pathAndQuery));
    }

    [TestMethod]
    public async Task LookupMany_SplitsLargeLookupsIntoBatches()
    {
        // Answers each batch with a result for each of its addresses, in order.
        var handler = new FakeHandler((_, body) => {
            string[] batch = JsonSerializer.Deserialize<string[]>(body!)!;
            return Json(JsonSerializer.Serialize(batch.Select(ip => new { ipAddress = ip })));
        });

        var client = new IPLocationClient(BaseAddress, handler);
        var addresses = Enumerable.Range(0, 250).Select(i => new IPAddress([10, 0, (byte)(i / 256), (byte)(i % 256)])).ToList();

        var results = await client.LookupAsync(addresses);

        results.Select(r => r.IPAddress).ShouldBe(addresses.Select(a => a.ToString()));
        handler.Requests.Count.ShouldBe(3);
        handler.Requests.ShouldAllBe(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/Lookup");
        handler.Bodies.Select(b => JsonSerializer.Deserialize<string[]>(b)!.Length).ShouldBe([100, 100, 50]);
        IPLocationLimits.MaxBatchSize.ShouldBe(100);
    }

    [TestMethod]
    public async Task GetInfo_ReadsTheDatabasesAndAttribution()
    {
        var handler = new FakeHandler((_, _) => Json("""
            {"city":{"month":"2026-10","buildDateUtc":"2026-10-01T00:00:00Z","databaseType":"DBIP-City-Lite"},"asn":null,
             "attribution":"IP Geolocation by DB-IP","attributionUrl":"https://db-ip.com",
             "licenseName":"CC BY 4.0","licenseUrl":"https://creativecommons.org/licenses/by/4.0/"}
            """));

        var info = await new IPLocationClient(BaseAddress, handler).GetInfoAsync();

        info.City.ShouldNotBeNull().Month.ShouldBe("2026-10");
        info.Asn.ShouldBeNull();
        info.Attribution.ShouldBe(DataAttribution.Text);
        info.AttributionUrl.ShouldBe(DataAttribution.Url);
        info.LicenseName.ShouldBe(DataAttribution.LicenseName);
        info.LicenseUrl.ShouldBe(DataAttribution.LicenseUrl);
        handler.Requests.ShouldHaveSingleItem().RequestUri!.PathAndQuery.ShouldBe("/Info");
    }

    [TestMethod]
    public async Task Errors_AreThrownAsApiExceptions()
    {
        var notLoaded = new ServiceUnavailableApiException("The IP location data is still being downloaded. Please try again in a few minutes.") {
            ErrorCode = IPLocationErrorCodes.DatabasesNotLoaded,
            RetryAfter = TimeSpan.FromMinutes(1),
        };

        var client = new IPLocationClient(BaseAddress, new FakeHandler((_, _) => ApiErrorResponse.Create(notLoaded)));
        var ex = await Should.ThrowAsync<ServiceUnavailableApiException>(() => client.LookupAsync(IPAddress.Parse("8.8.8.8")));

        ex.Message.ShouldBe(notLoaded.Message);
        ex.ErrorCode.ShouldBe(IPLocationErrorCodes.DatabasesNotLoaded);
        ex.RetryAfter.ShouldBe(TimeSpan.FromMinutes(1));

        var tooMany = new ValidationApiException("At most 100 addresses can be looked up at once.");
        client = new IPLocationClient(BaseAddress, new FakeHandler((_, _) => ApiErrorResponse.Create(tooMany)));

        (await Should.ThrowAsync<ValidationApiException>(() => client.GetInfoAsync())).Message.ShouldBe(tooMany.Message);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (body is not null)
                Bodies.Add(body);

            return respond(request, body);
        }
    }
}
