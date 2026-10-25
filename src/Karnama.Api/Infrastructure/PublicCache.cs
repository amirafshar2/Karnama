using Karnama.Application.Abstractions;
using Microsoft.AspNetCore.OutputCaching;

namespace Karnama.Api.Infrastructure;

internal sealed class PublicCache(IOutputCacheStore store) : IPublicCache
{
    public const string Policy = "site";
    public const string Tag = "site";
    public async Task EvictAsync(CancellationToken ct) => await store.EvictByTagAsync(Tag, ct);
}
