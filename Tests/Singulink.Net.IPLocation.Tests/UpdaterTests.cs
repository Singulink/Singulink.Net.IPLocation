namespace Singulink.Net.IPLocation.Tests;

[TestClass]
public class UpdaterTests
{
    [TestMethod]
    public async Task Lookup_NoDatabases_ReturnsServiceUnavailable()
    {
        using var host = new ServiceTestHost(DateTime.UtcNow, autoUpdate: false, downloadDirectory: null);

        var ex = await Should.ThrowAsync<ServiceUnavailableApiException>(() => host.Client.LookupAsync(IPAddress.Parse("8.8.8.8")));

        ex.Message.ShouldBe("The IP location data is still being downloaded. Please try again in a few minutes.");
        ex.ErrorCode.ShouldBe(IPLocationErrorCodes.DatabasesNotLoaded);
        ex.RetryAfter.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [TestMethod]
    public async Task Startup_NoDatabases_DownloadsCurrentMonth()
    {
        string downloadDirectory = ServiceTestHost.RequireDownloadDirectory();
        var month = ServiceTestHost.GetDownloadMonth(downloadDirectory);

        using var host = new ServiceTestHost(month.ToDateTime(new TimeOnly(12, 0)).AddDays(14), autoUpdate: true, downloadDirectory);
        var info = await host.WaitForDatabasesAsync();

        info.City!.Month.ShouldBe(month.ToString("yyyy-MM"));
        info.Asn!.Month.ShouldBe(month.ToString("yyyy-MM"));

        // The data directory also has the saved copy of Cloudflare's IP ranges that the test host writes.
        Directory.GetFiles(host.DataDirectory).Select(Path.GetFileName).Order().ShouldBe(
            DbIpFiles.Kinds.Select(k => DbIpFiles.GetFileName(k, month)).Append(IPLocationOptions.CloudflareRangesFileName).Order());
    }

    [TestMethod]
    public async Task Startup_CurrentMonthNotPublished_DownloadsPreviousMonth()
    {
        string downloadDirectory = ServiceTestHost.RequireDownloadDirectory();
        var month = ServiceTestHost.GetDownloadMonth(downloadDirectory);
        var nextMonth = month.AddMonths(1);

        // Two hours into the first of the month, before DB-IP has published that month's release.
        using var host = new ServiceTestHost(nextMonth.ToDateTime(new TimeOnly(2, 0)), autoUpdate: true, downloadDirectory);
        var info = await host.WaitForDatabasesAsync();

        info.City!.Month.ShouldBe(month.ToString("yyyy-MM"));
        host.Downloads.RequestedFiles.ShouldContain(DbIpFiles.GetFileName(DbIpDatabaseKind.City, nextMonth) + ".gz");
    }

    [TestMethod]
    public async Task Startup_DatabasesOnDisk_LoadsThemBeforeTakingRequests()
    {
        string downloadDirectory = ServiceTestHost.RequireDownloadDirectory();
        var month = ServiceTestHost.GetDownloadMonth(downloadDirectory);
        string dataDirectory = ServiceTestHost.CreateDataDirectory(downloadDirectory, month);

        using var host = new ServiceTestHost(month.ToDateTime(new TimeOnly(12, 0)).AddDays(14), autoUpdate: false, null, dataDirectory);

        // No waiting: the files are opened during startup.
        var result = await host.Client.LookupAsync(IPAddress.Parse("8.8.8.8"));

        result.CountryCode.ShouldBe("US");
        host.Downloads.RequestedFiles.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task NewMonth_ReleaseNotPublished_KeepsServingLoadedMonth()
    {
        string downloadDirectory = ServiceTestHost.RequireDownloadDirectory();
        var month = ServiceTestHost.GetDownloadMonth(downloadDirectory);
        var nextMonth = month.AddMonths(1);
        string dataDirectory = ServiceTestHost.CreateDataDirectory(downloadDirectory, month);

        using var host = new ServiceTestHost(nextMonth.ToDateTime(new TimeOnly(12, 0)).AddDays(3), autoUpdate: true, downloadDirectory, dataDirectory);

        // The loaded month stays in use while the new release is missing (the download is retried on the next check).
        await WaitForRequestAsync(host, DbIpFiles.GetFileName(DbIpDatabaseKind.Asn, nextMonth) + ".gz");
        var info = await host.GetInfoAsync();

        info.City!.Month.ShouldBe(month.ToString("yyyy-MM"));
        host.Downloads.RequestedFiles.ShouldNotContain(DbIpFiles.GetFileName(DbIpDatabaseKind.City, month) + ".gz");
        (await host.Client.LookupAsync(IPAddress.Parse("8.8.8.8"))).CountryCode.ShouldBe("US");
    }

    [TestMethod]
    public async Task Download_NewMonth_DeletesOlderFiles()
    {
        string downloadDirectory = ServiceTestHost.RequireDownloadDirectory();
        var month = ServiceTestHost.GetDownloadMonth(downloadDirectory);
        string dataDirectory = ServiceTestHost.CreateTempDirectory();

        // A file from long ago (that is not even a valid database) is deleted once the new month is in place; the previous month is kept.
        string oldFile = Path.Combine(dataDirectory, DbIpFiles.GetFileName(DbIpDatabaseKind.City, month.AddMonths(-6)));
        string previousFile = Path.Combine(dataDirectory, DbIpFiles.GetFileName(DbIpDatabaseKind.City, month.AddMonths(-1)));
        File.WriteAllText(oldFile, "not a database");
        File.WriteAllText(previousFile, "not a database either");

        using var host = new ServiceTestHost(month.ToDateTime(new TimeOnly(12, 0)).AddDays(14), autoUpdate: true, downloadDirectory, dataDirectory);
        await host.WaitForDatabasesAsync();

        File.Exists(oldFile).ShouldBeFalse();
        File.Exists(previousFile).ShouldBeTrue();
    }

    private static async Task WaitForRequestAsync(ServiceTestHost host, string fileName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (!host.Downloads.RequestedFiles.Contains(fileName))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"{fileName} was not requested.");

            await Task.Delay(100);
        }
    }
}
