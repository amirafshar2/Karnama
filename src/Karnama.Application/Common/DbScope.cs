using Karnama.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Karnama.Application.Common;

/// <summary>
/// Runs a database read in its own DI scope. Use it inside <c>HybridCache.GetOrCreateAsync</c> factories: the cache shares one
/// factory run between every request that asks for the same key at the same time, so the factory must not use the first
/// caller's request-scoped DbContext (if that request ends or is aborted first, the others get "disposed context" errors).
/// </summary>
public sealed class DbScope(IServiceScopeFactory scopes)
{
    public async Task<T> ReadAsync<T>(Func<IAppDbContext, CancellationToken, Task<T>> read, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<IAppDbContext>(), ct);
    }
}
