using FluentAssertions;
using Karnama.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Karnama.UnitTests;

public sealed class LocalMediaStoreTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");

    private sealed class Env(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static LocalMediaStore Store(string root) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:LocalPath"] = root }).Build(), new Env(root));

    [Fact]
    public async Task The_same_demo_file_saved_by_many_starts_at_once_ends_up_once_and_nobody_fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "karnama-media-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = Store(root);
            const string stable = "0123456789abcdef0123456789abcdef";
            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
                store.SaveImageAsync(new MemoryStream(TinyPng), TinyPng.Length, "site", CancellationToken.None, stable))));

            results.Should().OnlyContain(r => r.IsSuccess && r.Value == $"site/{stable}.png");
            Directory.GetFiles(Path.Combine(root, "site")).Should().ContainSingle("no half-written or temporary copies are left");
            store.OpenPublic($"site/{stable}.png").Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Normal_uploads_still_get_random_names()
    {
        var root = Path.Combine(Path.GetTempPath(), "karnama-media-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = Store(root);
            var a = await store.SaveImageAsync(new MemoryStream(TinyPng), TinyPng.Length, "brand", CancellationToken.None);
            var b = await store.SaveImageAsync(new MemoryStream(TinyPng), TinyPng.Length, "brand", CancellationToken.None);
            a.Value.Should().NotBe(b.Value);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
