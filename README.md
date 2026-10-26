# Singulink.Net.IPLocation

[![Chat on Discord](https://img.shields.io/discord/906246067773923490)](https://discord.gg/EkQhJFsBu6)
[![View nuget packages](https://img.shields.io/nuget/v/Singulink.Net.IPLocation.Client.svg)](https://www.nuget.org/packages/Singulink.Net.IPLocation.Client/)
[![Build and Test](https://github.com/Singulink/Singulink.Net.IPLocation/workflows/build%20and%20test/badge.svg)](https://github.com/Singulink/Singulink.Net.IPLocation/actions?query=workflow%3A%22build+and+test%22)

**Singulink.Net.IPLocation** is a .NET project that contains a simple client and a self-hostable service for looking up the approximate location (continent, country, region, city and coordinates) and the network operator (autonomous system number and organization) of IP addresses. The service uses the free [DB-IP](https://db-ip.com) IP to City Lite and IP to ASN Lite databases, keeps them up to date by itself, and answers lookups from memory without calling out to any other service.

### About Singulink

We are a small team of engineers and designers dedicated to building beautiful, functional, and well-engineered software solutions. We offer very competitive rates as well as fixed-price contracts and welcome inquiries to discuss any custom development / project support needs you may have.

This package is part of our **Singulink Libraries** collection. Visit https://github.com/Singulink to see our full list of publicly available libraries and other open-source projects.

## Usage

This repository is used for our own infrastructure needs and is open-sourced so that others can run the same service, instead of sending their users' IP addresses to an external service. The service runs on your own network, and your apps look up addresses with the client, which is given the service's address. The instance at https://iplocation.singulink.com is there to try the service out, for example in its Swagger UI at https://iplocation.singulink.com/swagger, and isn't meant for real use.

```csharp
var client = new IPLocationClient(new Uri("https://iplocation.example.internal/"));
var result = await client.LookupAsync(IPAddress.Parse("8.8.8.8"));

Console.WriteLine(result.GetDisplayLocation()); // Mountain View, California, United States
Console.WriteLine(result.Organization);         // Google LLC
```

Lookups of private addresses (private networks, loopback, link-local and other addresses that are not routable on the internet) return a result with `IsPrivate` set and no location. Pass `includeHostName: true` to also look up an address's host name with reverse DNS.

### Attribution

The data is licensed under the [Creative Commons Attribution 4.0 International License](https://creativecommons.org/licenses/by/4.0/), so **apps that show results must credit DB-IP** with a link: the text and address are available in `DataAttribution.Text` and `DataAttribution.Url` (`<a href="https://db-ip.com">IP Geolocation by DB-IP</a>`).

The free databases are less accurate than DB-IP's commercial ones: countries are reliable, but cities can be off, especially for mobile networks. They have no postal codes, time zones or connection types.

## Installation

A client package is available on NuGet - simply install the `Singulink.Net.IPLocation.Client` package. It brings in `Singulink.Net.IPLocation`, the package with the types that the client and the service share: `IPLocationResult`, `DataInfo`, `DataAttribution`, the error codes in `IPLocationErrorCodes`, the batch size in `IPLocationLimits` and the JSON serializer context. The client is built on the client library of the [Singulink HTTP API Toolkit](https://github.com/Singulink/Singulink.Net.Http.Api).

**Supported Runtimes**: .NET 10+ and .NET Framework 4.6.2+. Other runtimes that can use .NET Standard 2.0 libraries, such as .NET 8 and 9, get a build warning: they are not supported or tested.

`IPLocationClient` sends its requests to the service at the address given to its constructor. All clients share one `HttpClient`, so an app usually creates one client and keeps it, for example as a singleton:

```csharp
services.AddSingleton<IIPLocationClient>(new IPLocationClient(new Uri("https://iplocation.example.internal/")));
```

A lookup of more than `IPLocationLimits.MaxBatchSize` (100) addresses is sent in several requests. `GetInfoAsync` returns the loaded databases and the credit their license requires. Errors that the service returns are thrown as the toolkit's `ApiException` types, with the service's message, error code and `Retry-After` time (see [Errors](#errors)), and `ApiFailures.IsTransient` tells whether a failure is worth trying again. The constructor that takes an `HttpMessageHandler` sends the client's requests through it, for example to answer them in tests with `ApiErrorResponse.Create`.

For running the service on your own infrastructure, deploy `Singulink.Net.IPLocation.Service` to your desired hosting destination. The project is a .NET 10 web service that can be hosted in IIS, Azure, or any other suitable .NET hosting environment. It needs no database: on first start it downloads the current month's databases (about 65 MB compressed, 135 MB on disk) into its data directory, and it downloads each new monthly release as it comes out. The settings are in the `IPLocation` section of `appsettings.json`, or in environment variables like `IPLocation__DataDirectory`:

| Setting | Default | Description |
| --- | --- | --- |
| `DataDirectory` | `../data` | Where the databases and the saved copy of Cloudflare's IP ranges are kept, absolute or relative to the application directory. The default is next to the application directory so that deployments do not remove it. |
| `AutoUpdate` | `true` | Whether to download missing databases and each monthly release. Turn it off to manage the files yourself (named like `dbip-city-lite-2026-10.mmdb` and `dbip-asn-lite-2026-10.mmdb`). |

The rate limit and Cloudflare settings are in their own sections (see [Running the Service](#running-the-service)). The service has no secrets to configure.

## Running the Service

The service needs outbound HTTPS access to `download.db-ip.com` for the databases and to `www.cloudflare.com` for Cloudflare's IP ranges (see [Cloudflare](#cloudflare)), and permission to write to its data directory.

### Errors

Errors use the [error format](https://www.singulink.com/Docs/Singulink.Net.Http.Api/articles/concepts/error-format.html) of the Singulink HTTP API Toolkit: the status code and a `text/plain` message. Some errors have an error code in square brackets in front of the message, and their content type has a `format=error-code` parameter. The client throws the toolkit's exception for each status: `BadRequestApiException`, `ValidationApiException`, `TooManyRequestsApiException`, `ServerErrorApiException` or `ServiceUnavailableApiException`, with the message, the error code in `ErrorCode` and the `Retry-After` time in `RetryAfter`:

| Status | Message | Returned when |
| --- | --- | --- |
| `400` | `'{ip}' is not a valid IP address.` | An address isn't a valid IP address. |
| `400` | `The request is not valid.` | A required parameter is missing, or the body of a batch lookup isn't a JSON array of strings. For the body, the message is followed by the location of the error in it. |
| `422` | `At most 100 addresses can be looked up at once.` | A batch lookup has more than `IPLocationLimits.MaxBatchSize` (100) addresses. |
| `429` | `[rate-limited] Too many requests. Please wait a moment and try again.` | The client made too many requests (see [Rate Limit](#rate-limit)). |
| `500` | `An unexpected error has occurred and has been logged. Reference ID: {id}.` | Something unexpected failed. The error is logged with the same reference ID. |
| `503` | `[databases-not-loaded] The IP location data is still being downloaded. Please try again in a few minutes.` | The databases aren't loaded yet. |
| `503` | `[cloudflare-ranges-unavailable] The service is starting. Please try again shortly.` | The service has no Cloudflare IP ranges yet (see [Cloudflare](#cloudflare)). |

The `429` and `503` responses have a `Retry-After` header with the number of seconds to wait: until the next rate limit window, 60 while the databases are being downloaded and 10 for Cloudflare's ranges.

### Rate Limit

Each client with a public address can make 50 requests a minute, counted in windows that start at every full minute, and IPv6 clients are counted per /64 network. The limit is for the public instance, which is there to try the service out. Clients with private addresses aren't limited: private IPv4 networks, loopback, link-local, the shared address space (`100.64.0.0/10`), unique local IPv6 addresses and connections without an address. So a deployment inside a private network, whose apps reach it from such addresses, isn't limited. A request over the limit gets a `429` error (see [Errors](#errors)). The `RateLimits` settings below change the limit.

This is the [per-client rate limit](https://www.singulink.com/Docs/Singulink.Net.Http.Api/articles/guides/rate-limiting.html) of the Singulink HTTP API Toolkit, with its settings in the `RateLimits` configuration section, for example `RateLimits__PermitLimit` as an environment variable:

| Setting | Default | Description |
| --- | --- | --- |
| `PermitLimit` | `50` | The number of requests each client address can make per window. |
| `Window` | `00:01:00` | The length of a window. |
| `ExemptPrivateNetworks` | `true` | Whether clients with private addresses are exempt. |
| `ExemptNetworks` | (none) | Other addresses, or networks in CIDR notation, whose clients are exempt, such as `RateLimits__ExemptNetworks__0=203.0.113.7`. |
| `Enabled` | `true` | Whether requests are limited at all. |

Each server counts the requests it gets, so in a web farm a client can make up to the limit on each server.

### Cloudflare

The service can run behind Cloudflare's proxy. For a request whose connection comes from one of [Cloudflare's IP ranges](https://www.cloudflare.com/ips/), the service takes the client's address from Cloudflare's `CF-Connecting-IP` header and the scheme from `X-Forwarded-Proto`, so the rate limit counts each client rather than Cloudflare's servers. All other requests keep their connection's address and scheme, and any headers starting with `CF-` are removed from them, so the service can be reached through Cloudflare and directly at the same time. This is the [Cloudflare forwarding](https://www.singulink.com/Docs/Singulink.Net.Http.Api/articles/guides/cloudflare.html) of the Singulink HTTP API Toolkit.

The service downloads Cloudflare's IP ranges at startup and once a day, and saves them to `cloudflare-ip-ranges.txt` in the data directory. When the download at startup fails, it uses that saved copy. Without any ranges, as on a first start that can't download them, the service refuses every request with a `503` error (`cloudflare-ranges-unavailable`) and tries the download again every 10 seconds, until it succeeds.

The settings are in the `Cloudflare` configuration section, for example `Cloudflare__SavedRangesPath` as an environment variable (see the [toolkit's documentation](https://www.singulink.com/Docs/Singulink.Net.Http.Api/articles/guides/cloudflare.html#options) for all of them):

| Setting | Default | Description |
| --- | --- | --- |
| `SavedRangesPath` | `cloudflare-ip-ranges.txt` in the data directory | Where the downloaded ranges are saved. Relative paths are resolved against the content root. |
| `TrustedProxyNetworks` | (none) | Addresses, or networks in CIDR notation, of other proxies whose connections are trusted like Cloudflare's, such as `Cloudflare__TrustedProxyNetworks__0=127.0.0.1` for Cloudflare Tunnel's `cloudflared` on the same machine. |
| `DownloadRanges` | `true` | Whether the ranges are downloaded. When `false`, they are only read from `SavedRangesPath`, at startup and every `RefreshInterval`. |
| `RefreshInterval` | `1.00:00:00` | How often the ranges are downloaded again. |

### Hosting in IIS

The service downloads the databases and each monthly release while it runs. With IIS's defaults, it only starts with the first request and stops after 20 minutes without requests, so the first download only starts with the first request, and a new release is only downloaded while requests keep the service running. These settings keep it running:

- Set the app pool's **Start Mode** to `AlwaysRunning` and its **Idle Time-out** to `0`.
- Set the site's **Preload Enabled** to `True`. This needs IIS's Application Initialization feature (`Web-AppInit`), which isn't installed by default.

Behind Cloudflare, IIS needs no changes for the client addresses, since the service reads Cloudflare's headers itself. IIS's own logs still show the addresses of Cloudflare's servers.
