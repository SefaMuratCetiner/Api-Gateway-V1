using System.Collections.Concurrent;
using NJsonSchema;
using NSwag;

namespace ApiGateway.Proxy.Validation;

/// <summary>A parsed contract and the ETag it was fetched with.</summary>
public sealed record LoadedContract(ContractKind Kind, OpenApiDocument? OpenApi, JsonSchema? Schema, string? ETag, DateTimeOffset LoadedAt);

/// <summary>The last good version of every contract referenced by a route. Thread-safe.</summary>
public sealed class ContractStore
{
    private readonly ConcurrentDictionary<(Uri Url, ContractKind Kind), LoadedContract> _contracts = new();

    public LoadedContract? Find(Uri url, ContractKind kind) => _contracts.GetValueOrDefault((url, kind));

    public void Set(Uri url, LoadedContract contract) => _contracts[(url, contract.Kind)] = contract;

    /// <summary>Parses a contract document; throws when it is not a valid document of that kind.</summary>
    public static async Task<LoadedContract> ParseAsync(string json, ContractKind kind, string? etag, DateTimeOffset now) => kind switch
    {
        ContractKind.OpenApi => new LoadedContract(kind, await OpenApiDocument.FromJsonAsync(json), null, etag, now),
        _ => new LoadedContract(kind, null, await JsonSchema.FromJsonAsync(json), etag, now),
    };
}
