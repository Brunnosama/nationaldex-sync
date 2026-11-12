using System.Net;
using PokedexSync.Import.PokeApi;

namespace PokedexSync.Import.Tests;

public sealed class PokeApiClientTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "pokedex-sync-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    private PokeApiClient Client(StubHandler handler) => new(_cacheDir, TimeSpan.Zero, TextWriter.Null, handler);

    private const string Region = """{"id":1,"name":"kanto","names":[{"name":"Kanto","language":{"name":"en","url":"x"}}]}""";

    [Fact]
    public async Task Second_request_for_the_same_resource_is_served_from_the_cache()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, Region));
        using var client = Client(handler);

        await client.GetAsync<Region>("region/1");
        var region = await client.GetAsync<Region>($"{PokeApiClient.BaseUrl}region/1/");

        Assert.Equal("kanto", region.Name);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, client.CacheHits);
    }

    [Fact]
    public async Task Missing_field_stops_the_import_with_a_format_error_naming_the_resource()
    {
        using var client = Client(new StubHandler(_ => (HttpStatusCode.OK, """{"id":1,"name":"kanto"}""")));

        var ex = await Assert.ThrowsAsync<PokeApiFormatException>(() => client.GetAsync<Region>("region/1"));

        Assert.Equal("region/1", ex.Resource);
        Assert.Contains("names", ex.Message);
    }

    [Fact]
    public async Task Field_with_a_changed_type_stops_the_import_with_a_format_error()
    {
        using var client = Client(new StubHandler(_ => (HttpStatusCode.OK, """{"id":"one","name":"kanto","names":[]}""")));

        await Assert.ThrowsAsync<PokeApiFormatException>(() => client.GetAsync<Region>("region/1"));
    }

    [Fact]
    public async Task Server_errors_are_retried()
    {
        var handler = new StubHandler(call => call == 1 ? (HttpStatusCode.ServiceUnavailable, "") : (HttpStatusCode.OK, Region));
        using var client = Client(handler);

        var region = await client.GetAsync<Region>("region/1");

        Assert.Equal("kanto", region.Name);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Not_found_is_not_retried()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.NotFound, ""));
        using var client = Client(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync<Region>("region/999"));
        Assert.Equal(1, handler.Calls);
    }

    private sealed class StubHandler(Func<int, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = respond(++Calls);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
