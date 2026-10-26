using Microsoft.Extensions.Options;

namespace Singulink.Net.IPLocation.Service;

/// <summary>
/// Options for the service, read from the <c>IPLocation</c> configuration section.
/// </summary>
public sealed class IPLocationOptions
{
    /// <summary>
    /// The name of the saved copy of Cloudflare's IP ranges in the data directory.
    /// </summary>
    public const string CloudflareRangesFileName = "cloudflare-ip-ranges.txt";

    /// <summary>
    /// Gets or sets the directory that holds the downloaded databases, absolute or relative to the application's directory. Default is <c>../data</c>,
    /// next to the application directory, so that deployments do not remove it.
    /// </summary>
    public string DataDirectory { get; set; } = "../data";

    /// <summary>
    /// Gets or sets the address that DB-IP's free databases are downloaded from.
    /// </summary>
    public Uri DownloadBaseAddress { get; set; } = new("https://download.db-ip.com/free/");

    /// <summary>
    /// Gets or sets a value indicating whether to download the databases when they are missing and each month's new release. Default is <see
    /// langword="true"/>.
    /// </summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>
    /// Gets or sets how often to check for a new release while the current month's databases are not loaded yet. Default is 1 hour.
    /// </summary>
    public TimeSpan UpdateCheckInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets the full path of <see cref="DataDirectory"/>.
    /// </summary>
    public string GetDataDirectoryPath() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, DataDirectory));
}

/// <summary>
/// Validates <see cref="IPLocationOptions"/> when the service starts.
/// </summary>
internal sealed class IPLocationOptionsValidator : IValidateOptions<IPLocationOptions>
{
    public ValidateOptionsResult Validate(string? name, IPLocationOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.DataDirectory))
            failures.Add("The IPLocation:DataDirectory setting must not be empty.");

        if (!options.DownloadBaseAddress.IsAbsoluteUri)
            failures.Add("The IPLocation:DownloadBaseAddress setting must be an absolute address.");

        if (options.UpdateCheckInterval <= TimeSpan.Zero)
            failures.Add("The IPLocation:UpdateCheckInterval setting must be positive.");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
