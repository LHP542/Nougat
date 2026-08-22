using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nougat.Models;

namespace Nougat.Services;

public class GithubApiException : Exception
{
    public GithubApiException(string message) : base(message) { }
    public GithubApiException(string message, Exception inner) : base(message, inner) { }
}

public sealed class GithubRateLimitedException : GithubApiException
{
    public GithubRateLimitedException(string message) : base(message) { }
}

/// <summary>
/// Dünnschichtiger Wrapper um die GitHub REST-API.
/// Named HttpClient "github" liefert User-Agent, System-Proxy und optional das Bearer-Token.
/// </summary>
public sealed class GithubRepoService
{
    public const string HttpClientName = "github";
    private const string ApiRoot = "https://api.github.com";
    private const string RawRoot = "https://raw.githubusercontent.com";

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _factory;
    private readonly ILogger<GithubRepoService> _logger;

    public GithubRepoService(IHttpClientFactory factory, ILogger<GithubRepoService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>
    /// Listet die Repos eines Kontos. <paramref name="account"/> darf ein
    /// Benutzer <b>oder</b> eine Organisation sein.
    ///
    /// <para>Der Unterschied steckt im Endpunkt: <c>/orgs/{name}/repos</c>
    /// liefert bei einer Organisation auch die <b>privaten</b> Repos, auf die
    /// das Token Zugriff hat; <c>/users/{name}/repos</c> gibt dort nur die
    /// öffentlichen zurück. Für ein Offline-Bundle aus dienstlichen — also
    /// meist privaten — Repos ist das der Unterschied zwischen „alles" und
    /// „fast nichts". Deshalb erst <c>/orgs/</c> versuchen und nur bei 404 auf
    /// <c>/users/</c> zurückfallen.</para>
    /// </summary>
    public async Task<List<RepoInfo>> ListUserReposAsync(string account, CancellationToken ct = default)
    {
        // Achtung, die beiden Endpunkte kennen verschiedene type-Werte:
        // /orgs/ akzeptiert all|public|private|forks|sources|member,
        // /users/ dagegen all|owner|member. Ein "type=owner" an /orgs/
        // quittiert GitHub mit 422 — deshalb steht die Query beim Endpunkt und
        // nicht in der gemeinsamen Schleife.
        var fromOrg = await ListFromAsync($"{ApiRoot}/orgs/{account}/repos", "all", account, ct)
            .ConfigureAwait(false);
        if (fromOrg is not null) return fromOrg;

        return await ListFromAsync($"{ApiRoot}/users/{account}/repos", "owner", account, ct)
            .ConfigureAwait(false) ?? [];
    }

    /// <summary>Liest eine Repo-Liste seitenweise. <c>null</c> = Konto unter
    /// diesem Endpunkt unbekannt (404); der Aufrufer versucht dann den anderen.</summary>
    private async Task<List<RepoInfo>?> ListFromAsync(string endpoint, string type,
        string account, CancellationToken ct)
    {
        var result = new List<RepoInfo>();
        var client = _factory.CreateClient(HttpClientName);

        for (var page = 1; ; page++)
        {
            var url = $"{endpoint}?per_page=100&type={type}&page={page}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            LogRateLimit(resp);

            if ((int)resp.StatusCode == 403 && resp.Headers.TryGetValues("x-ratelimit-remaining", out var vals))
            {
                foreach (var v in vals)
                    if (v == "0")
                        throw new GithubRateLimitedException(
                            "GitHub-API-Rate-Limit erreicht. PAT in den Einstellungen setzen erhöht das Limit auf 5000/h.");
            }

            // 404 heißt nur „unter diesem Endpunkt nicht bekannt" — dann ist
            // das Konto eben kein Org bzw. kein User. Kein Fehler, der dem
            // Anwender gezeigt werden müsste.
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new GithubApiException($"GitHub-API-Fehler {(int)resp.StatusCode}: {body}");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var pageItems = await JsonSerializer.DeserializeAsync<List<RepoInfo>>(stream, _json, ct).ConfigureAwait(false);
            if (pageItems is null || pageItems.Count == 0) break;

            result.AddRange(pageItems);
            if (pageItems.Count < 100) break;
        }

        _logger.LogInformation("GitHub: {Count} Repos für {Account} geladen ({Endpoint})",
            result.Count, account, endpoint);
        return result;
    }

    /// <summary>Liefert alle Pfade im Default-Branch (rekursiv, Bloburl).</summary>
    public async Task<List<string>> ListRepoTreeAsync(string ownerRepo, string branch, CancellationToken ct = default)
    {
        var client = _factory.CreateClient(HttpClientName);
        var url = $"{ApiRoot}/repos/{ownerRepo}/git/trees/{branch}?recursive=1";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        LogRateLimit(resp);

        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Repo-Tree nicht gefunden: {Repo}@{Branch}", ownerRepo, branch);
            return [];
        }
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new GithubApiException($"Repo-Tree {(int)resp.StatusCode}: {body}");
        }

        var payload = await resp.Content.ReadFromJsonAsync<TreeResponse>(_json, ct).ConfigureAwait(false)
                     ?? throw new GithubApiException("Leere Tree-Antwort");
        var paths = new List<string>(payload.Tree.Count);
        foreach (var entry in payload.Tree)
            if (entry.Type == "blob")
                paths.Add(entry.Path);
        return paths;
    }

    /// <summary>Lädt eine Datei aus dem Repo als Text (Raw-URL, kein API-Rate-Limit).</summary>
    public async Task<string> GetRawFileAsync(string ownerRepo, string branch, string path, CancellationToken ct = default)
    {
        var client = _factory.CreateClient(HttpClientName);
        var url = $"{RawRoot}/{ownerRepo}/{branch}/{path}";
        using var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new GithubApiException($"Raw-Fetch {(int)resp.StatusCode}: {url}");
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private void LogRateLimit(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("x-ratelimit-remaining", out var rem) &&
            resp.Headers.TryGetValues("x-ratelimit-limit", out var lim))
        {
            _logger.LogTrace("GitHub-RateLimit: {Rem}/{Lim}", string.Join(",", rem), string.Join(",", lim));
        }
    }

    private sealed class TreeResponse
    {
        public List<TreeEntry> Tree { get; set; } = [];
    }

    private sealed class TreeEntry
    {
        public string Path { get; set; } = "";
        public string Type { get; set; } = "";
    }
}
