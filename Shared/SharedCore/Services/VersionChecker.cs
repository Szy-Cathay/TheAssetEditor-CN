using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Core.ErrorHandling;

namespace Shared.Core.Services
{
    public sealed record UpdateRelease(
        string TagName,
        string Name,
        string Body,
        string HtmlUrl,
        DateTimeOffset? PublishedAt);

    public class VersionChecker
    {
        private static readonly ILogger s_logger = Logging.Create<VersionChecker>();

        private const string GiteeOwner = "szy-cathay";
        private const string GiteeRepository = "AssetEditor-CN-Downloads";
        private static readonly Uri s_releaseFeedUri = new(
            $"https://gitee.com/{GiteeOwner}/{GiteeRepository}/raw/master/update-feed.json");
        private static readonly Uri s_releasesApiUri = new(
            $"https://gitee.com/api/v5/repos/{GiteeOwner}/{GiteeRepository}/releases?per_page=100");
        private static readonly HttpClient s_httpClient = CreateHttpClient();

        public static async Task<List<UpdateRelease>?> GetNewerReleases()
        {
            var releases = await GetReleasesAsync(s_httpClient);
            if (releases == null || releases.Count == 0)
                return null;

            var currentVersion = GetCurrentVersion();
            var newerReleases = GetReleasesSinceCurrentVersion(releases, currentVersion);
            if (newerReleases.Count > 0)
                return newerReleases;

            return null;
        }

        internal static async Task<IReadOnlyList<UpdateRelease>?> GetReleasesAsync(
            HttpClient httpClient)
        {
            ArgumentNullException.ThrowIfNull(httpClient);

            try
            {
                using var response = await httpClient.GetAsync(s_releaseFeedUri);
                response.EnsureSuccessStatusCode();
                await using var responseStream = await response.Content.ReadAsStreamAsync();
                var release = await JsonSerializer.DeserializeAsync<GiteeRelease>(responseStream);
                if (release == null || !TryParseReleaseVersion(release.TagName, out _))
                    throw new InvalidDataException("The Gitee update feed has no valid release.");

                return MapReleases([release]);
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                or JsonException
                or InvalidDataException
                or TaskCanceledException
                or NotSupportedException)
            {
                s_logger.Information($"Unable to retrieve Gitee update feed: {exception.Message}");
            }

            try
            {
                using var response = await httpClient.GetAsync(s_releasesApiUri);
                response.EnsureSuccessStatusCode();
                await using var responseStream = await response.Content.ReadAsStreamAsync();
                var releases = await JsonSerializer.DeserializeAsync<List<GiteeRelease>>(
                    responseStream);
                return releases == null ? null : MapReleases(releases);
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                or JsonException
                or TaskCanceledException
                or NotSupportedException)
            {
                s_logger.Information($"Unable to retrieve latest release from Gitee: {exception.Message}");
                throw new InvalidOperationException(
                    "Unable to retrieve update information from Gitee.",
                    exception);
            }
        }

        private static IReadOnlyList<UpdateRelease> MapReleases(
            IEnumerable<GiteeRelease> releases)
        {
            return releases
                .Where(release => TryParseReleaseVersion(release.TagName, out _))
                .Select(release => new UpdateRelease(
                    release.TagName!,
                    string.IsNullOrWhiteSpace(release.Name) ? release.TagName! : release.Name,
                    release.Body ?? string.Empty,
                    $"https://gitee.com/{GiteeOwner}/{GiteeRepository}/releases/tag/{Uri.EscapeDataString(release.TagName!)}",
                    release.CreatedAt))
                .OrderByDescending(release => ParseReleaseVersion(release.TagName))
                .ToList();
        }

        private static List<UpdateRelease> GetReleasesSinceCurrentVersion(
            IReadOnlyList<UpdateRelease> releases,
            Version currentVersion)
        {
            var newerReleases = new List<UpdateRelease>();
            foreach (var release in releases)
            {
                var releaseVersion = ParseReleaseVersion(release.TagName);
                if (releaseVersion > currentVersion)
                    newerReleases.Add(release);
                else
                    break;
            }
            return newerReleases;
        }

        public static Version GetCurrentVersion()
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            if (version == null)
                throw new Exception("Current version is unknown.");

            return NormalizeVersion(version);
        }

        public static string GetCurrentDisplayVersion()
        {
            var informationalVersion = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return FormatDisplayVersion(informationalVersion, GetCurrentVersion());
        }

        internal static string FormatDisplayVersion(string? informationalVersion, Version numericVersion)
        {
            var label = informationalVersion?.Split('+', 2)[0].Trim();
            return string.IsNullOrWhiteSpace(label) ? numericVersion.ToString() : label;
        }

        public static string GetReleaseDisplayVersion(UpdateRelease release)
        {
            var version = ParseReleaseVersion(release.TagName);
            var hotfixLabel = $"{version.Major}.{version.Minor}.{version.Build}-hotfix";
            if (version.Revision > 0
                && string.Equals(release.Name, hotfixLabel, StringComparison.OrdinalIgnoreCase))
                return release.Name;

            return version.ToString();
        }

        internal static Version NormalizeVersion(Version version)
        {
            if (version.Revision > 0)
                return version;

            return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
        }

        public static Version ParseReleaseVersion(string tagName)
        {
            var cleanedVersion = tagName.Trim().TrimStart('v', 'V');
            return new Version(cleanedVersion);
        }

        private static bool TryParseReleaseVersion(string? tagName, out Version? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tagName))
                return false;

            return Version.TryParse(tagName.Trim().TrimStart('v', 'V'), out version);
        }

        private static HttpClient CreateHttpClient()
        {
            var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AssetEditor.CN");
            return httpClient;
        }

        private sealed record GiteeRelease(
            [property: JsonPropertyName("tag_name")] string? TagName,
            [property: JsonPropertyName("name")] string? Name,
            [property: JsonPropertyName("body")] string? Body,
            [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);
    }
}
