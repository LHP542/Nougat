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
/// Dünnschichtiger Wrapper um die REST-API der eigenen Forge.
///
/// <para><b>Seit dem 28.10.2026 git.flexfamily.de statt GitHub.</b> Lars:
/// die Repos der Arbeitsgruppe sollen nicht mehr von GitHub gezogen werden.
/// Forgejos API ist in den hier genutzten Endpunkten feldgleich —
/// <c>/orgs/{name}/repos</c>, <c>/users/{name}/repos</c> und
/// <c>git/trees/{ref}?recursive=1</c> liefern dieselben Felder, geprüft am
/// 28.10.2026 gegen LHP542/Nougat (127 Einträge, <c>truncated: false</c>).</para>
///
/// <para><b>Zwei Unterschiede stecken im Detail:</b> Forgejo paginiert mit
/// <c>limit</c> statt <c>per_page</c>, und Rohdateien liegen unter
/// <c>&lt;forge&gt;/&lt;owner&gt;/&lt;repo&gt;/raw/branch/&lt;zweig&gt;/&lt;pfad&gt;</c>
/// — mit <c>branch/</c> dazwischen, sonst 404.</para>
///
/// <para>Der Klassenname bleibt: Er steht in neun Dateien, und ein
/// Umbenennen würde den Umbau größer machen als die Sache ist.</para>
///
/// Named HttpClient "github" liefert User-Agent, System-Proxy und optional das Token.
/// </summary>
public sealed class GithubRepoService
{
    public const string HttpClientName = "github";

    /// <summary>Wurzel der Forgejo-API. <c>git.flexfamily.de</c> und
    /// <c>git.flexfamily.de</c> zeigen auf dieselbe Instanz; hier steht der
    /// Name, den Lars genannt hat.</summary>
    private const string ApiRoot = "https://git.flexfamily.de/api/v1";

    /// <summary>Wurzel für Rohdateien und Seiten (ohne <c>/api/v1</c>).</summary>
    private const string RawRoot = "https://git.flexfamily.de";

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
        // Achtung, die beiden Endpunkte kannten bei GitHub verschiedene
        // type-Werte: /orgs/ akzeptierte all|public|private|forks|sources|
        // member, /users/ dagegen all|owner|member; ein "type=owner" an /orgs/
        // quittierte GitHub mit 422. Forgejo ist hier gleichgültig und
        // ignoriert den Parameter — er bleibt stehen, damit die Methode auch
        // gegen GitHub arbeiten könnte, solange beides parallel läuft.
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
            // Forgejo paginiert mit `limit`, GitHub mit `per_page`. Beide
            // ignorieren den jeweils fremden Parameter, deshalb stehen hier
            // beide — so arbeitet dieselbe Schleife gegen beide Seiten.
            var url = $"{endpoint}?limit=100&per_page=100&type={type}&page={page}";
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
                throw new GithubApiException($"API-Fehler {(int)resp.StatusCode}: {body}");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var pageItems = await JsonSerializer.DeserializeAsync<List<RepoInfo>>(stream, _json, ct).ConfigureAwait(false);
            if (pageItems is null || pageItems.Count == 0) break;

            result.AddRange(pageItems);
            if (pageItems.Count < 100) break;
        }

        _logger.LogInformation("Forge: {Count} Repos für {Account} geladen ({Endpoint})",
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
        // Forgejos Entsprechung zu raw.githubusercontent.com. Das `branch/`
        // gehört dazu: Ohne es antwortet die Forge mit 404.
        var url = $"{RawRoot}/{ownerRepo}/raw/branch/{branch}/{path}";
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
