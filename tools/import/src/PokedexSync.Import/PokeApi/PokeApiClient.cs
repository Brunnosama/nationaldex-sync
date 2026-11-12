using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace PokedexSync.Import.PokeApi;

/// <summary>Thrown when a PokeAPI response no longer matches the fields the import relies on.</summary>
public sealed class PokeApiFormatException(string resource, string detail, Exception inner)
    : Exception($"PokeAPI response format changed for '{resource}': {detail}", inner)
{
    public string Resource { get; } = resource;
}

/// <summary>
/// Read-only PokeAPI client for the development import. Requests are made one at a time with a
/// minimum gap between them, and every response body is cached on disk, so re-running the
/// import (or a dry run) does not hit PokeAPI again.
/// </summary>
public sealed class PokeApiClient : IDisposable
{
    public const string BaseUrl = "https://pokeapi.co/api/v2/";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectNullableAnnotations = true,
    };

    private const int MaxAttempts = 5;

    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly TimeSpan _minGap;
    private readonly TextWriter _log;
    private readonly Stopwatch _sinceLastRequest = new();

    public int NetworkRequests { get; private set; }
    public int CacheHits { get; private set; }

    public PokeApiClient(string cacheDir, TimeSpan minGap, TextWriter log, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(BaseUrl);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("pokedex-sync-import/1.0 (+https://github.com/Brunnosama/nationaldex-sync)");
        _cacheDir = cacheDir;
        _minGap = minGap;
        _log = log;
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Fetches a resource by path ("pokemon-species/25") or by a full PokeAPI URL.</summary>
    public async Task<T> GetAsync<T>(string pathOrUrl, CancellationToken ct = default)
    {
        var path = ToRelativePath(pathOrUrl);
        var body = await GetBodyAsync(path, ct);
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                ?? throw new JsonException("Response body was null.");
        }
        catch (JsonException ex)
        {
            throw new PokeApiFormatException(path, ex.Message, ex);
        }
    }

    public Task<ResourceList> ListAsync(string resource, CancellationToken ct = default) =>
        GetAsync<ResourceList>($"{resource}?limit=100000", ct);

    public static string ToRelativePath(string pathOrUrl)
    {
        var path = pathOrUrl.StartsWith(BaseUrl, StringComparison.Ordinal) ? pathOrUrl[BaseUrl.Length..] : pathOrUrl;
        return path.Trim('/');
    }

    private async Task<string> GetBodyAsync(string path, CancellationToken ct)
    {
        var cacheFile = Path.Combine(_cacheDir, CacheFileName(path));
        if (File.Exists(cacheFile))
        {
            CacheHits++;
            return await File.ReadAllTextAsync(cacheFile, ct);
        }

        var body = await FetchWithRetryAsync(path, ct);
        await File.WriteAllTextAsync(cacheFile, body, ct);
        return body;
    }

    private async Task<string> FetchWithRetryAsync(string path, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await WaitForGapAsync(ct);
            NetworkRequests++;
            if (NetworkRequests % 250 == 0)
                _log.WriteLine($"  … {NetworkRequests} requests to PokeAPI so far");

            TimeSpan? retryAfter = null;
            try
            {
                using var response = await _http.GetAsync(path, ct);
                _sinceLastRequest.Restart();

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new HttpRequestException($"PokeAPI returned 404 for '{path}'.", null, response.StatusCode);

                if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
                    throw new HttpRequestException($"PokeAPI returned {(int)response.StatusCode} for '{path}'.", null, response.StatusCode);

                retryAfter = response.Headers.RetryAfter?.Delta;
                if (attempt >= MaxAttempts)
                    throw new HttpRequestException($"PokeAPI returned {(int)response.StatusCode} for '{path}' after {attempt} attempts.", null, response.StatusCode);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is null && attempt < MaxAttempts)
            {
                // Network error: fall through to the back-off below.
                _sinceLastRequest.Restart();
            }

            var backoff = retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
            _log.WriteLine($"  retrying '{path}' in {backoff.TotalSeconds:0}s (attempt {attempt} of {MaxAttempts})");
            await Task.Delay(backoff, ct);
        }
    }

    private async Task WaitForGapAsync(CancellationToken ct)
    {
        if (!_sinceLastRequest.IsRunning)
            return;
        var wait = _minGap - _sinceLastRequest.Elapsed;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, ct);
    }

    private static string CacheFileName(string path)
    {
        var safe = path.Replace('/', '_').Replace('?', '_').Replace('=', '-').Replace('&', '_');
        return safe + ".json";
    }

    public void Dispose() => _http.Dispose();
}
