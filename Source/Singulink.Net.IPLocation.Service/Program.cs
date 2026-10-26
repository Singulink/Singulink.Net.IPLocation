using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Singulink.Net.Http.Api;
using Singulink.Net.Http.Api.Service;
using Singulink.Net.IPLocation;
using Singulink.Net.IPLocation.Service;
using Singulink.Net.IPLocation.Service.Data;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<IPLocationOptions>()
    .Bind(builder.Configuration.GetSection("IPLocation"))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<IPLocationOptions>, IPLocationOptionsValidator>();

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPLocationDatabase>();
builder.Services.AddSingleton<HostNameResolver>();
builder.Services.AddHostedService<DbIpDataUpdater>();
builder.Services.AddHttpClient(DbIpDataUpdater.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(10));

// Host names are cached per address, so the cache is bounded against lookups of many different addresses.
builder.Services.AddMemoryCache(cacheOptions => cacheOptions.SizeLimit = 100_000);

// Errors are reported in the API toolkit's format: the status code and a text/plain message.
builder.Services.AddApiExceptionHandler();
builder.Services.AddApiResponseHandling();
builder.Services.AddApiJson(IPLocationJsonSerializerContext.Default);

// The client addresses that Cloudflare forwards, with Cloudflare's IP ranges saved in the data directory.
builder.Services.AddCloudflareForwarding();
builder.Services.AddOptions<CloudflareForwardingOptions>()
    .Bind(builder.Configuration.GetSection("Cloudflare"))
    .Configure<IOptions<IPLocationOptions>>((cloudflareOptions, options) =>
        cloudflareOptions.SavedRangesPath ??= Path.Combine(options.Value.GetDataDirectoryPath(), IPLocationOptions.CloudflareRangesFileName));

builder.Services.AddRateLimiter(limiterOptions => limiterOptions.SetClientFixedWindowGlobalLimiter(limit => {
    limit.PermitLimit = 50;

    // Internal apps reach the service from private addresses.
    limit.ExemptPrivateNetworks = true;

    builder.Configuration.GetSection("RateLimits").Bind(limit);
}));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(swaggerOptions => {
    swaggerOptions.SwaggerDoc("v1", new OpenApiInfo {
        Title = "Singulink IP Location",
        Version = "v1",
        Description =
            "Approximate location and network operator of IP addresses, from DB-IP's free IP to City Lite and IP to ASN Lite databases, " +
            $"updated monthly. {DataAttribution.Text} ({DataAttribution.Url}), licensed under {DataAttribution.LicenseName} " +
            $"({DataAttribution.LicenseUrl}): apps that show results must credit DB-IP as well.",
    });

    swaggerOptions.OperationFilter<ResponseDescriptionFilter>();
});

WebApplication app = builder.Build();

// First, so that everything after it sees the client's address and scheme rather than Cloudflare's.
app.UseCloudflareForwarding();
app.UseApiResponseHandling();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseRateLimiter();

app.MapGet("/", () => Results.Redirect("/swagger"))
    .ExcludeFromDescription();

(string, string) notLoaded = (IPLocationErrorCodes.DatabasesNotLoaded, "The databases aren't loaded yet, for example while they're being downloaded.");

app.MapGet("/Lookup", LookupAsync)
    .WithSummary("Looks up an IP address.")
    .WithDescription("Set includeHostName to also look up the address's host name with reverse DNS, which can take up to 2 seconds.")
    .Produces<IPLocationResult>()
    .ProducesBadRequest("The address is missing, or it isn't a valid IP address.")
    .ProducesServiceErrors(notLoaded);

app.MapPost("/Lookup", LookupManyAsync)
    .WithSummary("Looks up several IP addresses.")
    .WithDescription("The results are in the same order as the addresses. Set includeHostName to also look up host names with reverse DNS.")
    .Produces<IPLocationResult[]>()
    .ProducesBadRequest("The body isn't a JSON array of strings, or an address isn't a valid IP address.")
    .ProducesValidationError($"There are more than {IPLocationLimits.MaxBatchSize} addresses.")
    .ProducesServiceErrors(notLoaded);

app.MapGet("/Info", GetInfo)
    .WithSummary("Describes the loaded databases and the credit their license requires.")
    .Produces<DataInfo>()
    .ProducesServiceErrors();

app.Run();

static async Task<IResult> LookupAsync(
    string ip, bool? includeHostName, IPLocationDatabase database, HostNameResolver hostNameResolver, CancellationToken cancellationToken)
{
    if (!database.IsReady)
        throw NotReady();

    if (!IPAddress.TryParse(ip, out var address))
        throw InvalidAddress(ip);

    return TypedResults.Ok(await LookupCoreAsync(address, includeHostName is true, database, hostNameResolver, cancellationToken));
}

static async Task<IResult> LookupManyAsync(
    string[] ips, bool? includeHostName, IPLocationDatabase database, HostNameResolver hostNameResolver, CancellationToken cancellationToken)
{
    if (!database.IsReady)
        throw NotReady();

    // A well-formed request over a limit, as opposed to a malformed one (400).
    if (ips.Length > IPLocationLimits.MaxBatchSize)
        throw new ValidationApiException($"At most {IPLocationLimits.MaxBatchSize} addresses can be looked up at once.");

    var addresses = new IPAddress[ips.Length];

    for (int i = 0; i < ips.Length; i++)
    {
        if (!IPAddress.TryParse(ips[i], out var address))
            throw InvalidAddress(ips[i]);

        addresses[i] = address;
    }

    var results = await Task.WhenAll(addresses.Select(a => LookupCoreAsync(a, includeHostName is true, database, hostNameResolver, cancellationToken)));
    return TypedResults.Ok(results);
}

static async Task<IPLocationResult> LookupCoreAsync(
    IPAddress address, bool includeHostName, IPLocationDatabase database, HostNameResolver hostNameResolver, CancellationToken cancellationToken)
{
    var result = database.Lookup(address);

    if (!includeHostName)
        return result;

    string? hostName = await hostNameResolver.GetHostNameAsync(IPAddress.Parse(result.IPAddress), cancellationToken);
    return result with { HostName = hostName };
}

static DataInfo GetInfo(IPLocationDatabase database)
{
    return new DataInfo(
        GetDatabaseInfo(database.Get(DbIpDatabaseKind.City)),
        GetDatabaseInfo(database.Get(DbIpDatabaseKind.Asn)),
        DataAttribution.Text,
        DataAttribution.Url,
        DataAttribution.LicenseName,
        DataAttribution.LicenseUrl);

    static DatabaseInfo? GetDatabaseInfo(LoadedDatabase? loaded) => loaded is null ? null : new DatabaseInfo(
        loaded.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture), loaded.BuildDateUtc, loaded.DatabaseType);
}

static ServiceUnavailableApiException NotReady() => new("The IP location data is still being downloaded. Please try again in a few minutes.") {
    ErrorCode = IPLocationErrorCodes.DatabasesNotLoaded,
    RetryAfter = TimeSpan.FromMinutes(1),
};

static BadRequestApiException InvalidAddress(string? ip) => new($"'{ip}' is not a valid IP address.");

/// <summary>
/// The service's entry point, public for in-process tests.
/// </summary>
public partial class Program;
