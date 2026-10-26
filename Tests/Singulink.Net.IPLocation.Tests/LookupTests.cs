using System.Net.Http.Json;

namespace Singulink.Net.IPLocation.Tests;

[TestClass]
public class LookupTests
{
    private static ServiceTestHost? _host;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        if (ServiceTestHost.DownloadDirectory is not { } downloadDirectory)
            return;

        var month = ServiceTestHost.GetDownloadMonth(downloadDirectory);
        _host = new ServiceTestHost(month.ToDateTime(new TimeOnly(12, 0)).AddDays(14), autoUpdate: true, downloadDirectory);
        await _host.WaitForDatabasesAsync();
    }

    [ClassCleanup]
    public static void ClassCleanup() => _host?.Dispose();

    [TestMethod]
    public async Task Lookup_PublicAddress_ReturnsLocationAndNetwork()
    {
        var result = await GetHost().Client.LookupAsync(IPAddress.Parse("8.8.8.8"));

        result.IPAddress.ShouldBe("8.8.8.8");
        result.IsPrivate.ShouldBeFalse();
        result.ContinentCode.ShouldBe("NA");
        result.CountryCode.ShouldBe("US");
        result.CountryName.ShouldBe("United States");
        result.Region.ShouldNotBeNullOrEmpty();
        result.City.ShouldNotBeNullOrEmpty();
        result.Latitude.ShouldNotBeNull();
        result.Longitude.ShouldNotBeNull();
        result.AutonomousSystemNumber.ShouldBe(15169);
        result.Organization.ShouldBe("Google LLC");
        result.HostName.ShouldBeNull();
        result.GetDisplayLocation().ShouldNotBeNull().ShouldEndWith(", United States");
    }

    [TestMethod]
    public async Task Lookup_IPv6Address_ReturnsLocation()
    {
        var result = await GetHost().Client.LookupAsync(IPAddress.Parse("2607:f8b0:4005:80f::200e"));

        result.CountryCode.ShouldBe("US");
        result.AutonomousSystemNumber.ShouldBe(15169);
    }

    [TestMethod]
    public async Task Lookup_MappedIPv4Address_LooksUpIPv4Address()
    {
        var result = await GetHost().Client.LookupAsync(IPAddress.Parse("::ffff:8.8.8.8"));

        result.IPAddress.ShouldBe("8.8.8.8");
        result.CountryCode.ShouldBe("US");
    }

    [TestMethod]
    public async Task Lookup_PrivateAddress_ReturnsNoLocation()
    {
        var result = await GetHost().Client.LookupAsync(IPAddress.Parse("192.168.1.10"), includeHostName: true);

        result.IsPrivate.ShouldBeTrue();
        result.CountryCode.ShouldBeNull();
        result.Organization.ShouldBeNull();
        result.HostName.ShouldBeNull();
        result.GetDisplayLocation().ShouldBeNull();
    }

    [TestMethod]
    public async Task Lookup_IncludeHostName_ReturnsReverseDnsName()
    {
        var result = await GetHost().Client.LookupAsync(IPAddress.Parse("8.8.8.8"), includeHostName: true);

        result.HostName.ShouldBe("dns.google");
    }

    [TestMethod]
    public async Task LookupMany_ReturnsResultsInOrder()
    {
        string[] addresses = ["8.8.8.8", "10.0.0.1", "1.1.1.1"];
        var results = await GetHost().Client.LookupAsync(addresses.Select(IPAddress.Parse));

        results.Select(r => r.IPAddress).ShouldBe(addresses);
        results[0].Organization.ShouldBe("Google LLC");
        results[1].IsPrivate.ShouldBeTrue();
        results[2].CountryCode.ShouldNotBeNull();
    }

    [TestMethod]
    public async Task LookupMany_MoreThanBatchSize_SplitsIntoSeveralRequests()
    {
        var addresses = Enumerable.Range(0, 250).Select(i => new IPAddress([8, 8, (byte)(i / 256), (byte)(i % 256)])).ToList();
        var results = await GetHost().Client.LookupAsync(addresses);

        results.Select(r => r.IPAddress).ShouldBe(addresses.Select(a => a.ToString()));
    }

    [TestMethod]
    public async Task Lookup_InvalidAddress_ReturnsBadRequest()
    {
        using var response = await GetHost().HttpClient.GetAsync("/Lookup?ip=nope");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldBe("'nope' is not a valid IP address.");
    }

    [TestMethod]
    public async Task LookupMany_TooManyAddresses_ReturnsUnprocessableEntity()
    {
        string[] addresses = Enumerable.Repeat("8.8.8.8", IPLocationLimits.MaxBatchSize + 1).ToArray();
        using var response = await GetHost().HttpClient.PostAsJsonAsync("/Lookup", addresses);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldBe($"At most {IPLocationLimits.MaxBatchSize} addresses can be looked up at once.");
    }

    [TestMethod]
    public async Task Info_ReturnsLoadedDatabasesAndAttribution()
    {
        var info = await GetHost().GetInfoAsync();
        string month = ServiceTestHost.GetDownloadMonth(ServiceTestHost.DownloadDirectory!).ToString("yyyy-MM");

        info.City.ShouldNotBeNull().Month.ShouldBe(month);
        info.City.DatabaseType.ShouldContain("City");
        info.Asn.ShouldNotBeNull().Month.ShouldBe(month);
        info.Attribution.ShouldBe(DataAttribution.Text);
        info.AttributionUrl.ShouldBe(DataAttribution.Url);
        info.LicenseName.ShouldBe(DataAttribution.LicenseName);
        info.LicenseUrl.ShouldBe(DataAttribution.LicenseUrl);
    }

    private static ServiceTestHost GetHost()
    {
        if (_host is null)
            Assert.Inconclusive($"Set {ServiceTestHost.DownloadDirectoryVariable} to a directory with DB-IP's .mmdb.gz downloads to run this test.");

        return _host;
    }
}
