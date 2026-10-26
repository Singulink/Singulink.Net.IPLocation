using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Singulink.Net.IPLocation.Tests;

/// <summary>
/// Runs the service in process with its own data directory and a fake clock. Database downloads are served from the directory named by the
/// <c>IPLOCATION_TEST_DATA</c> environment variable, which holds DB-IP's <c>.mmdb.gz</c> files as downloaded from DB-IP; tests that need data are
/// inconclusive without it.
/// </summary>
internal sealed class ServiceTestHost : IDisposable
{
    public const string DownloadDirectoryVariable = "IPLOCATION_TEST_DATA";

    /// <summary>
    /// The saved copy of Cloudflare's IP ranges that hosts read, since they don't download them.
    /// </summary>
    public const string CloudflareRanges = "173.245.48.0/20\n103.21.244.0/22\n2400:cb00::/32\n2606:4700::/32\n# end\n";

    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceTestHost"/> class and starts the service.
    /// </summary>
    /// <param name="utcNow">The time of the fake clock, which stands still.</param>
    /// <param name="autoUpdate">The <c>IPLocation:AutoUpdate</c> setting.</param>
    /// <param name="downloadDirectory">The directory that database downloads are served from, or <see langword="null"/> for none.</param>
    /// <param name="dataDirectory">The service's data directory, or <see langword="null"/> for a new temp directory.</param>
    /// <param name="settings">Other settings.</param>
    /// <param name="writeCloudflareRanges">Whether to write a saved copy of Cloudflare's IP ranges into the data directory, which the service reads
    /// instead of downloading them.</param>
    public ServiceTestHost(
        DateTime utcNow,
        bool autoUpdate,
        string? downloadDirectory,
        string? dataDirectory = null,
        Dictionary<string, string?>? settings = null,
        bool writeCloudflareRanges = true)
    {
        DataDirectory = dataDirectory ?? CreateTempDirectory();
        Downloads = new FakeDownloadHandler(downloadDirectory);

        Dictionary<string, string?> config = new() {
            ["IPLocation:DataDirectory"] = DataDirectory,
            ["IPLocation:AutoUpdate"] = autoUpdate ? "true" : "false",
            ["IPLocation:DownloadBaseAddress"] = "https://download.test/free/",
            ["Cloudflare:DownloadRanges"] = "false",
        };

        if (writeCloudflareRanges)
            File.WriteAllText(Path.Combine(DataDirectory, IPLocationOptions.CloudflareRangesFileName), CloudflareRanges);

        foreach ((string key, string? value) in settings ?? [])
            config[key] = value;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(config));

            builder.ConfigureTestServices(services => {
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(utcNow, TimeSpan.Zero)));
                services.AddHttpClient(DbIpDataUpdater.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Downloads);
            });
        });

        HttpClient = _factory.CreateClient();

        // HTTPS, so the HTTPS redirection middleware has nothing to do.
        Client = new IPLocationClient(new Uri("https://localhost/"), _factory.Server.CreateHandler());
    }

    public static string? DownloadDirectory =>
        Environment.GetEnvironmentVariable(DownloadDirectoryVariable) is { Length: > 0 } directory && Directory.Exists(directory) ? directory : null;

    public string DataDirectory { get; }

    public FakeDownloadHandler Downloads { get; }

    public TestServer Server => _factory.Server;

    public HttpClient HttpClient { get; }

    public IPLocationClient Client { get; }

    public static string RequireDownloadDirectory()
    {
        string? directory = DownloadDirectory;

        if (directory is null)
            Assert.Inconclusive($"Set {DownloadDirectoryVariable} to a directory with DB-IP's .mmdb.gz downloads to run this test.");

        return directory;
    }

    /// <summary>
    /// Gets the newest month that the download directory has both databases for.
    /// </summary>
    public static DateOnly GetDownloadMonth(string downloadDirectory)
    {
        DateOnly? GetNewest(DbIpDatabaseKind kind) => Directory.EnumerateFiles(downloadDirectory, "*.mmdb.gz")
            .Select(path => DbIpFiles.GetMonth(kind, Path.GetFileNameWithoutExtension(path)))
            .Max();

        var city = GetNewest(DbIpDatabaseKind.City);
        var asn = GetNewest(DbIpDatabaseKind.Asn);

        if (city is null || asn is null || city != asn)
            Assert.Inconclusive($"{downloadDirectory} must have the city and ASN downloads of the same month.");

        return city.Value;
    }

    /// <summary>
    /// Decompresses a month's downloads into a new data directory, as if the service had downloaded them earlier.
    /// </summary>
    public static string CreateDataDirectory(string downloadDirectory, DateOnly month)
    {
        string dataDirectory = CreateTempDirectory();

        foreach (var kind in DbIpFiles.Kinds)
        {
            string fileName = DbIpFiles.GetFileName(kind, month);

            using var source = new GZipStream(File.OpenRead(Path.Combine(downloadDirectory, fileName + ".gz")), CompressionMode.Decompress);
            using var target = File.Create(Path.Combine(dataDirectory, fileName));
            source.CopyTo(target);
        }

        return dataDirectory;
    }

    public static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "iplocation-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        return directory;
    }

    public async Task<DataInfo> GetInfoAsync() => (await HttpClient.GetFromJsonAsync<DataInfo>("/Info"))!;

    /// <summary>
    /// Waits for both databases to be loaded (downloads happen in the background after startup).
    /// </summary>
    public async Task<DataInfo> WaitForDatabasesAsync()
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);

        while (true)
        {
            var info = await GetInfoAsync();

            if (info.City is not null && info.Asn is not null)
                return info;

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The databases were not loaded in time.");

            await Task.Delay(200);
        }
    }

    public void Dispose()
    {
        HttpClient.Dispose();
        _factory.Dispose();

        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Memory-mapped files can stay locked for a moment after the host stops; the temp directory is cleaned up eventually.
        }
    }
}

/// <summary>
/// Serves database downloads from a local directory and records the files that were requested. Files that are not there are not found, like
/// releases DB-IP has not published yet.
/// </summary>
internal sealed class FakeDownloadHandler(string? directory) : HttpMessageHandler
{
    public ConcurrentQueue<string> RequestedFiles { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string fileName = Path.GetFileName(request.RequestUri!.AbsolutePath);
        RequestedFiles.Enqueue(fileName);

        string? path = directory is null ? null : Path.Combine(directory, fileName);

        if (path is null || !File.Exists(path))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(path)) });
    }
}
