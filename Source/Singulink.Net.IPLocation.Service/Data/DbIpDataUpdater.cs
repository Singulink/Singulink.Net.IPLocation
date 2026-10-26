using System.IO.Compression;
using System.Net;
using MaxMind.Db;
using Microsoft.Extensions.Options;

namespace Singulink.Net.IPLocation.Service.Data;

/// <summary>
/// Loads the newest databases on disk at startup, downloads them when they are missing, and downloads each month's release (DB-IP publishes on the
/// first of the month). A failed download leaves the loaded databases in use and is retried on the next check.
/// </summary>
public sealed partial class DbIpDataUpdater(
    IPLocationDatabase database,
    IOptions<IPLocationOptions> options,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    ILogger<DbIpDataUpdater> logger) : BackgroundService
{
    public const string HttpClientName = "DbIp";

    /// <summary>
    /// Opens the newest databases on disk before the service starts taking requests, then starts checking for downloads in the background.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        string dataDirectory = options.Value.GetDataDirectoryPath();
        Directory.CreateDirectory(dataDirectory);

        foreach (var kind in DbIpFiles.Kinds)
            LoadNewestFile(kind, dataDirectory);

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        string dataDirectory = settings.GetDataDirectoryPath();

        if (!settings.AutoUpdate)
        {
            if (!database.IsReady)
                LogNoDatabases(dataDirectory);

            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var kind in DbIpFiles.Kinds)
                await UpdateAsync(kind, dataDirectory, settings.DownloadBaseAddress, stoppingToken);

            await Task.Delay(settings.UpdateCheckInterval, timeProvider, stoppingToken);
        }
    }

    private void LoadNewestFile(DbIpDatabaseKind kind, string dataDirectory)
    {
        var files = Directory.EnumerateFiles(dataDirectory)
            .Select(path => (Path: path, Month: DbIpFiles.GetMonth(kind, Path.GetFileName(path))))
            .Where(f => f.Month is not null)
            .OrderByDescending(f => f.Month);

        foreach (var (path, month) in files)
        {
            try
            {
                var loaded = LoadedDatabase.Open(kind, month!.Value, path);
                database.Replace(loaded);
                LogLoaded(kind, FormatMonth(loaded.Month), loaded.BuildDateUtc);

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDatabaseException)
            {
                LogInvalidFile(ex, path);
            }
        }
    }

    private async Task UpdateAsync(DbIpDatabaseKind kind, string dataDirectory, Uri downloadBaseAddress, CancellationToken cancellationToken)
    {
        var currentMonth = DbIpFiles.GetMonth(timeProvider.GetUtcNow().UtcDateTime);
        var loadedMonth = database.Get(kind)?.Month;

        if (loadedMonth >= currentMonth)
            return;

        // Early on the first of the month the new release may not be out yet: with nothing loaded, last month's will do in the meantime.
        DateOnly[] months = loadedMonth is null ? [currentMonth, currentMonth.AddMonths(-1)] : [currentMonth];

        foreach (var month in months)
        {
            try
            {
                string? path = await DownloadAsync(kind, month, dataDirectory, downloadBaseAddress, cancellationToken);

                if (path is null)
                    continue;

                var loaded = LoadedDatabase.Open(kind, month, path);
                database.Replace(loaded);
                LogLoaded(kind, FormatMonth(month), loaded.BuildDateUtc);

                DeleteOldFiles(kind, dataDirectory, month);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or IOException
                or UnauthorizedAccessException or InvalidDataException or InvalidDatabaseException)
            {
                LogDownloadFailed(ex, kind, FormatMonth(month));
                return;
            }
        }
    }

    /// <summary>
    /// Downloads and decompresses a database, and checks that it opens. Returns the path of the database file, or <see langword="null"/> if that
    /// month's release is not published yet.
    /// </summary>
    private async Task<string?> DownloadAsync(
        DbIpDatabaseKind kind, DateOnly month, string dataDirectory, Uri downloadBaseAddress, CancellationToken cancellationToken)
    {
        var uri = DbIpFiles.GetDownloadUri(downloadBaseAddress, kind, month);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            LogNotPublished(kind, FormatMonth(month));
            return null;
        }

        response.EnsureSuccessStatusCode();
        LogDownloading(kind, FormatMonth(month), uri);

        string path = Path.Combine(dataDirectory, DbIpFiles.GetFileName(kind, month));
        string downloadPath = path + ".download";

        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var decompressed = new GZipStream(source, CompressionMode.Decompress))
            await using (var target = File.Create(downloadPath))
            {
                await decompressed.CopyToAsync(target, cancellationToken);
            }

            // Make sure the file is a valid database of the right kind before it takes the place of anything.
            LoadedDatabase.Open(kind, month, downloadPath).Dispose();
            File.Move(downloadPath, path, overwrite: true);

            return path;
        }
        finally
        {
            File.Delete(downloadPath);
        }
    }

    /// <summary>
    /// Deletes the files of a kind that are older than the previous month's, which is kept in case the new one turns out to be bad.
    /// </summary>
    private void DeleteOldFiles(DbIpDatabaseKind kind, string dataDirectory, DateOnly newMonth)
    {
        var keepFrom = newMonth.AddMonths(-1);

        foreach (string path in Directory.EnumerateFiles(dataDirectory))
        {
            if (DbIpFiles.GetMonth(kind, Path.GetFileName(path)) is { } month && month < keepFrom)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogDeleteFailed(ex, path);
                }
            }
        }
    }

    private static string FormatMonth(DateOnly month) => month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    [LoggerMessage(LogLevel.Information, "Loaded the {Kind} database for {Month} (built {BuildDate:u}).")]
    private partial void LogLoaded(DbIpDatabaseKind kind, string month, DateTime buildDate);

    [LoggerMessage(LogLevel.Information, "Downloading the {Kind} database for {Month} from {Uri}.")]
    private partial void LogDownloading(DbIpDatabaseKind kind, string month, Uri uri);

    [LoggerMessage(LogLevel.Information, "The {Kind} database for {Month} is not published yet.")]
    private partial void LogNotPublished(DbIpDatabaseKind kind, string month);

    [LoggerMessage(LogLevel.Warning, "Could not download the {Kind} database for {Month}. The loaded database stays in use and the download is retried later.")]
    private partial void LogDownloadFailed(Exception exception, DbIpDatabaseKind kind, string month);

    [LoggerMessage(LogLevel.Warning, "Could not open the database file {Path}.")]
    private partial void LogInvalidFile(Exception exception, string path);

    [LoggerMessage(LogLevel.Warning, "Could not delete the old database file {Path}.")]
    private partial void LogDeleteFailed(Exception exception, string path);

    [LoggerMessage(LogLevel.Warning, "There are no databases in {Directory} and automatic updates are off, so lookups fail until databases are added.")]
    private partial void LogNoDatabases(string directory);
}
