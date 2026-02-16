using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using saas.Infrastructure;
using Xunit;

namespace saas.Tests.Infrastructure;

public class DistributedCacheServiceTests
{
    private readonly DistributedCacheService _service;

    public DistributedCacheServiceTests()
    {
        var memoryCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        _service = new DistributedCacheService(memoryCache);
    }

    [Fact]
    public async Task SetAndGetAsync_RoundTrip()
    {
        var item = new TestItem("hello", 42);
        await _service.SetAsync("key1", item);

        var result = await _service.GetAsync<TestItem>("key1");

        Assert.NotNull(result);
        Assert.Equal("hello", result!.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenKeyMissing()
    {
        var result = await _service.GetAsync<TestItem>("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_RemovesKey()
    {
        await _service.SetAsync("key-to-delete", new TestItem("temp", 1));
        await _service.RemoveAsync("key-to-delete");

        var result = await _service.GetAsync<TestItem>("key-to-delete");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_WithExpiration_SetsValue()
    {
        await _service.SetAsync("expiring", new TestItem("exp", 99), TimeSpan.FromMinutes(10));

        var result = await _service.GetAsync<TestItem>("expiring");
        Assert.NotNull(result);
        Assert.Equal("exp", result!.Name);
    }

    [Fact]
    public async Task SetAsync_OverwritesExistingKey()
    {
        await _service.SetAsync("overwrite", new TestItem("first", 1));
        await _service.SetAsync("overwrite", new TestItem("second", 2));

        var result = await _service.GetAsync<TestItem>("overwrite");
        Assert.NotNull(result);
        Assert.Equal("second", result!.Name);
        Assert.Equal(2, result.Value);
    }

    private record TestItem(string Name, int Value);
}
