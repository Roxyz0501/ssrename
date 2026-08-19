using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace SSRename
{
    internal sealed class GitHubUpdateClient : IDisposable
    {
        private readonly HttpClient client;

        public GitHubUpdateClient()
        {
            client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SSRename", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        public async Task<UpdateCheckResult> CheckAsync(SemVersion currentVersion, string skippedVersion, CancellationToken cancellationToken)
        {
            var result = new UpdateCheckResult { CheckedAtUtc = DateTime.UtcNow };
            if (!UpdateSource.IsConfigured)
            {
                result.Status = UpdateCheckStatus.NotConfigured;
                return result;
            }
            try
            {
                var endpoint = new Uri("https://api.github.com/repos/" +
                    Uri.EscapeDataString(UpdateSource.RepositoryOwner) + "/" +
                    Uri.EscapeDataString(UpdateSource.RepositoryName) + "/releases?per_page=20");
                var json = await client.GetStringAsync(endpoint).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var release = GitHubReleaseParser.ParseLatestStable(json);
                result.Release = release;
                if (release == null || release.Version.CompareTo(currentVersion) <= 0)
                    result.Status = UpdateCheckStatus.UpToDate;
                else if (!string.IsNullOrWhiteSpace(skippedVersion) &&
                    string.Equals(release.Version.ToString(), skippedVersion, StringComparison.OrdinalIgnoreCase))
                    result.Status = UpdateCheckStatus.Skipped;
                else
                    result.Status = UpdateCheckStatus.Available;
            }
            catch (Exception ex)
            {
                result.Status = UpdateCheckStatus.Failed;
                result.Error = ex.GetBaseException().Message;
            }
            return result;
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
