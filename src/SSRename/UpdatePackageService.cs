using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SSRename
{
    internal sealed class UpdatePackageService
    {
        private const long MaximumPackageBytes = 50L * 1024L * 1024L;
        private const long MaximumManifestBytes = 1024L * 1024L;

        public async Task<string> PrepareAsync(ReleaseInfo release, CancellationToken cancellationToken)
        {
            if (release == null || release.Version == null) throw new InvalidOperationException("Release information is missing.");
            var package = release.Assets.FirstOrDefault(asset =>
                asset.Name.StartsWith("SSRename-", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (package == null) throw new InvalidDataException("The SSRename release ZIP asset was not found.");
            if (package.Size <= 0 || package.Size > MaximumPackageBytes) throw new InvalidDataException("The release package size is invalid.");
            var manifest = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));

            var root = Path.Combine(Path.GetTempPath(), "SSRename", "updates", release.Version + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var packagePath = Path.Combine(root, package.Name);
            try
            {
            await DownloadAsync(package.DownloadUrl, packagePath, MaximumPackageBytes, cancellationToken).ConfigureAwait(false);

            string expectedHash = null;
            if (manifest != null)
            {
                var manifestPath = Path.Combine(root, "SHA256SUMS.txt");
                await DownloadAsync(manifest.DownloadUrl, manifestPath, MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
                expectedHash = HashVerifier.FindHashForAsset(File.ReadAllText(manifestPath, Encoding.UTF8), package.Name);
                if (string.IsNullOrWhiteSpace(expectedHash)) throw new InvalidDataException("The package hash is missing from SHA256SUMS.txt.");
            }

            var extracted = Path.Combine(root, "validated");
            var stagedDll = UpdatePackageValidator.ValidateAndExtract(packagePath, extracted, release.Version, expectedHash);
            cancellationToken.ThrowIfCancellationRequested();
            return stagedDll;
            }
            catch
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
                throw;
            }
        }

        private static async Task DownloadAsync(string url, string destination, long maximumBytes, CancellationToken cancellationToken)
        {
            Uri current;
            if (!Uri.TryCreate(url, UriKind.Absolute, out current) || !IsAllowedGitHubUri(current))
                throw new InvalidDataException("The asset URL is not an allowed GitHub HTTPS URL.");
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SSRename/1.0");
                for (var redirect = 0; redirect < 6; redirect++)
                {
                    using (var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                        {
                            var location = response.Headers.Location;
                            if (location == null) throw new HttpRequestException("GitHub redirect has no destination.");
                            current = location.IsAbsoluteUri ? location : new Uri(current, location);
                            if (!IsAllowedGitHubUri(current)) throw new InvalidDataException("GitHub redirected to an untrusted host.");
                            continue;
                        }
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > maximumBytes)
                            throw new InvalidDataException("Downloaded asset is too large.");
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[81920];
                            long total = 0;
                            int read;
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                            {
                                total += read;
                                if (total > maximumBytes) throw new InvalidDataException("Downloaded asset is too large.");
                                await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        return;
                    }
                }
            }
            throw new HttpRequestException("Too many GitHub redirects.");
        }

        internal static bool IsAllowedGitHubUri(Uri uri)
        {
            if (uri == null || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
            var host = uri.Host.ToLowerInvariant();
            return host == "github.com" || host == "api.github.com" || host == "release-assets.githubusercontent.com" ||
                host == "objects.githubusercontent.com" || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
        }

    }
}
