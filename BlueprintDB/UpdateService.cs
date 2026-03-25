using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using Blueprint.App.Models;

namespace Blueprint.App;

public record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    string  TagName,
    string  ReleaseUrl,
    string  ReleaseNotes)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}

public static class UpdateService
{
    private const string ApiUrl  = "https://api.github.com/repos/slavkomatanovic/BlueprintDB/releases/latest";
    private const string SkipKey = "SkippedUpdateVersion";
    private const int    ChapterId = -1;

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static UpdateService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "BlueprintDB-UpdateChecker");
    }

    public static Version CurrentVersion
        => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    /// <summary>
    /// Calls the GitHub Releases API and returns an UpdateCheckResult if a newer version
    /// is available and the user has not chosen to skip it. Returns null on network failure,
    /// no update, or skipped version.
    /// </summary>
    public static async Task<UpdateCheckResult?> CheckForUpdateAsync()
    {
        try
        {
            var release = await _http.GetFromJsonAsync<GitHubRelease>(ApiUrl)
                          .ConfigureAwait(false);

            if (release is null) return null;

            var tagStr = release.tag_name?.TrimStart('v') ?? "";
            if (!Version.TryParse(tagStr, out var latestVersion)) return null;

            var result = new UpdateCheckResult(
                CurrentVersion,
                latestVersion,
                release.tag_name ?? "",
                release.html_url ?? "",
                release.body     ?? "");

            if (!result.IsUpdateAvailable) return null;

            if (GetSkippedVersion() == release.tag_name) return null;

            return result;
        }
        catch
        {
            return null;
        }
    }

    public static void SkipVersion(string tagName)
    {
        try
        {
            using var db = new BlueprintDbContext();
            var param = db.Parametris.FirstOrDefault(p =>
                p.Idpoglavlja == ChapterId && p.Nazivparametra == SkipKey);

            if (param is null)
                db.Parametris.Add(new Parametri
                {
                    Idpoglavlja    = ChapterId,
                    Nazivparametra = SkipKey,
                    Ocitano        = tagName
                });
            else
                param.Ocitano = tagName;

            db.SaveChanges();
        }
        catch { }
    }

    private static string? GetSkippedVersion()
    {
        try
        {
            using var db = new BlueprintDbContext();
            return db.Parametris.FirstOrDefault(p =>
                p.Idpoglavlja == ChapterId && p.Nazivparametra == SkipKey)?.Ocitano;
        }
        catch { return null; }
    }

    private record GitHubRelease(string? tag_name, string? html_url, string? body);
}
