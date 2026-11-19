using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace SSRename
{
    internal sealed class GitHubUpdateClient : IDisposable
    {
        private readonly HttpClient client;

        public GitHubUpdateClient() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }

        internal GitHubUpdateClient(HttpMessageHandler handler)
        {
            client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SSRename", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        public async Task<UpdateCheckResult> CheckAsync(SemVersion currentVersion, string skippedVersion, CancellationToken cancellationToken)
        {
            var result = new UpdateCheckResult { CheckedAtUtc = DateTime.UtcNow };
            if (!UpdateSource.IsConfigured) { result.Status = UpdateCheckStatus.NotConfigured; return result; }
            try
            {
                var latest = await ReadLatestTagAsync(cancellationToken).ConfigureAwait(false);
                result.Release = new ReleaseInfo { TagName = "v" + latest, Name = "SSRename v" + latest, Version = ParseVersion(latest) };
                if (result.Release.Version.CompareTo(currentVersion) <= 0)
                {
                    result.Status = UpdateCheckStatus.UpToDate;
                    return result;
                }
                if (!string.IsNullOrWhiteSpace(skippedVersion) && string.Equals(latest, skippedVersion, StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = UpdateCheckStatus.Skipped;
                    return result;
                }

                var endpoint = new Uri("https://api.github.com/repos/" + Uri.EscapeDataString(UpdateSource.RepositoryOwner) + "/" +
                    Uri.EscapeDataString(UpdateSource.RepositoryName) + "/releases/tags/v" + Uri.EscapeDataString(latest));
                using (var response = await SendFollowingApiRedirectsAsync(endpoint, cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                    {
                        result.Release = CreateRateLimitedRelease(latest);
                        result.Status = UpdateCheckStatus.AvailableRateLimited;
                        return result;
                    }
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    var release = GitHubReleaseParser.ParseRelease(json);
                    ValidateRelease(release, latest);
                    result.Release = release;
                    result.Status = UpdateCheckStatus.Available;
                    return result;
                }
            }
            catch (RateLimitedException ex) { result.Status = UpdateCheckStatus.RateLimited; result.Error = ex.Message; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { result.Status = UpdateCheckStatus.Failed; result.Error = ex.GetBaseException().Message; }
            return result;
        }

        private async Task<string> ReadLatestTagAsync(CancellationToken cancellationToken)
        {
            var expectedPrefix = "/" + UpdateSource.RepositoryOwner + "/" + UpdateSource.RepositoryName + "/releases/tag/";
            var uri = new Uri("https://github.com/" + UpdateSource.RepositoryOwner + "/" + UpdateSource.RepositoryName + "/releases/latest");
            for (var redirects = 0; redirects <= 4; redirects++)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Head, uri))
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429) throw new RateLimitedException("GitHub has temporarily limited release checks.");
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                    {
                        if (redirects == 4 || response.Headers.Location == null) throw new InvalidOperationException("GitHub latest-release redirect is invalid.");
                        var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
                        ValidateLatestRedirect(next, expectedPrefix);
                        uri = next;
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) || !uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Latest release resolved outside the expected repository.");
                    var tag = Uri.UnescapeDataString(uri.AbsolutePath.Substring(expectedPrefix.Length));
                    if (tag.Length == 0 || tag.IndexOf('/') >= 0 || tag.IndexOf('\\') >= 0 || tag.IndexOf('%') >= 0)
                        throw new FormatException("GitHub latest-release tag is invalid.");
                    if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag.Substring(1);
                    SemVersion version;
                    if (!SemVersion.TryParse(tag, out version)) throw new FormatException("GitHub latest-release tag is not a stable SemVer version.");
                    return version.ToString();
                }
            }
            throw new InvalidOperationException("Too many GitHub release redirects.");
        }

        private static void ValidateLatestRedirect(Uri uri, string expectedPrefix)
        {
            if (uri == null || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) || !uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("GitHub latest-release redirected outside the expected repository.");
        }

        private async Task<HttpResponseMessage> SendFollowingApiRedirectsAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            var uri = endpoint;
            for (var redirects = 0; redirects <= 3; redirects++)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                    if ((int)response.StatusCode < 300 || (int)response.StatusCode >= 400) return response;
                    if (redirects == 3 || response.Headers.Location == null) { response.Dispose(); throw new InvalidOperationException("GitHub API redirect is invalid."); }
                    var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
                    response.Dispose();
                    var basePath = "/repos/" + UpdateSource.RepositoryOwner + "/" + UpdateSource.RepositoryName + "/releases/tags/";
                    if (!string.Equals(next.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(next.Host, "api.github.com", StringComparison.OrdinalIgnoreCase) ||
                        !next.AbsolutePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("GitHub API redirected outside the expected repository.");
                    uri = next;
                }
            }
            throw new InvalidOperationException("Too many GitHub API redirects.");
        }

        private static void ValidateRelease(ReleaseInfo release, string latest)
        {
            if (release == null || release.Version == null || !string.Equals(release.Version.ToString(), latest, StringComparison.Ordinal) || release.Draft || release.Prerelease)
                throw new FormatException("GitHub release metadata does not match the verified stable tag.");
            var prefix = "/" + UpdateSource.RepositoryOwner + "/" + UpdateSource.RepositoryName + "/releases/download/v" + latest + "/";
            foreach (var asset in release.Assets)
            {
                Uri uri;
                if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) || !uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("GitHub release asset is not from the verified repository and tag.");
            }
            if (!release.Assets.Any(asset => string.Equals(asset.Name, "SSRename-" + latest + ".zip", StringComparison.OrdinalIgnoreCase)) ||
                !release.Assets.Any(asset => string.Equals(asset.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)))
                throw new FormatException("The stable release is missing the expected package or SHA-256 manifest.");
        }

        private static ReleaseInfo CreateRateLimitedRelease(string version)
        {
            var root = "https://github.com/" + UpdateSource.RepositoryOwner + "/" + UpdateSource.RepositoryName + "/releases/download/v" + version + "/";
            return new ReleaseInfo
            {
                TagName = "v" + version, Name = "SSRename v" + version, Version = ParseVersion(version),
                Body = string.Empty,
                Assets = new[]
                {
                    new ReleaseAssetInfo { Name = "SSRename-" + version + ".zip", DownloadUrl = root + "SSRename-" + version + ".zip", Size = 50L * 1024L * 1024L },
                    new ReleaseAssetInfo { Name = "SHA256SUMS.txt", DownloadUrl = root + "SHA256SUMS.txt", Size = 1024L * 1024L }
                }
            };
        }

        private static SemVersion ParseVersion(string value)
        {
            SemVersion version;
            if (!SemVersion.TryParse(value, out version)) throw new FormatException("Invalid stable version tag.");
            return version;
        }

        public void Dispose() { client.Dispose(); }

        private sealed class RateLimitedException : Exception { public RateLimitedException(string message) : base(message) { } }
    }
}
