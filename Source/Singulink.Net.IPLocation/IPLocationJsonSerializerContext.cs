using System.Text.Json;
using System.Text.Json.Serialization;

namespace Singulink.Net.IPLocation;

/// <summary>
/// Provides source-generated JSON type info for the types that the service and the client exchange.
/// </summary>
[JsonSerializable(typeof(IPLocationResult))]
[JsonSerializable(typeof(IPLocationResult[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(DataInfo))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
public sealed partial class IPLocationJsonSerializerContext : JsonSerializerContext;
