using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SSRename
{
    internal sealed class UpdatePackageService
    {
        private const long MaximumPackageBytes = 50L * 1024L * 1024L;
        private const long MaximumManifestBytes = 1024L * 1024L;
        internal const int MaximumRetainedStages = 3;
        internal static readonly TimeSpan StageRetention = TimeSpan.FromDays(14);
        private static readonly SemaphoreSlim stagingLock = new SemaphoreSlim(1, 1);

        public async Task<string> PrepareAsync(ReleaseInfo release, CancellationToken cancellationToken)
        {
            if (release == null || release.Version == null) throw new InvalidOperationException("Release information is missing.");
            var package = release.Assets.FirstOrDefault(asset =>
                asset.Name.StartsWith("SSRename-", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (package == null) throw new InvalidDataException("The SSRename release ZIP asset was not found.");
            if (!string.Equals(package.Name, "SSRename-" + release.Version + ".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The release package name does not match its version.");
            if (package.Size <= 0 || package.Size > MaximumPackageBytes) throw new InvalidDataException("The release package size is invalid.");
            var manifest = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
            if (manifest == null || manifest.Size <= 0 || manifest.Size > MaximumManifestBytes)
                throw new InvalidDataException("A valid SHA256SUMS.txt release asset is required.");

            await stagingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            var updatesRoot = Path.Combine(Path.GetTempPath(), "SSRename", "updates");
            string attemptRoot = null;
            try
            {
                Directory.CreateDirectory(updatesRoot);
                if (IsReparsePoint(updatesRoot)) throw new InvalidDataException("The update staging root cannot be a reparse point.");
                CleanupStagingDirectories(updatesRoot, null, DateTime.UtcNow);
                attemptRoot = Path.Combine(updatesRoot, ".staging-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(attemptRoot);

                var manifestPath = Path.Combine(attemptRoot, "SHA256SUMS.txt");
                await DownloadAsync(manifest.DownloadUrl, manifestPath, MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
                var expectedHash = GetRequiredPackageHash(File.ReadAllText(manifestPath, Encoding.UTF8), package.Name);

                var stageKey = CreateStageKey(release.Version, expectedHash);
                var stageDirectory = Path.Combine(updatesRoot, stageKey);
                var stagedDllPath = Path.Combine(stageDirectory, "SSRename.dll");
                if (IsReusableStage(stageDirectory, release.Version))
                {
                    Directory.SetLastWriteTimeUtc(stageDirectory, DateTime.UtcNow);
                    CleanupStagingDirectories(updatesRoot, stageKey, DateTime.UtcNow);
                    return stagedDllPath;
                }

                if (Directory.Exists(stageDirectory)) DeleteOwnedDirectory(stageDirectory);
                var packagePath = Path.Combine(attemptRoot, package.Name);
                await DownloadAsync(package.DownloadUrl, packagePath, MaximumPackageBytes, cancellationToken).ConfigureAwait(false);
                var extracted = Path.Combine(attemptRoot, "validated");
                var validatedDll = UpdatePackageValidator.ValidateAndExtract(packagePath, extracted, release.Version, expectedHash, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                Directory.CreateDirectory(stageDirectory);
                try
                {
                    CopyFileCancellable(validatedDll, stagedDllPath, cancellationToken);
                    File.WriteAllText(Path.Combine(stageDirectory, "SSRename.dll.sha256"), HashVerifier.ComputeSha256(stagedDllPath), Encoding.ASCII);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch
                {
                    if (Directory.Exists(stageDirectory)) DeleteOwnedDirectory(stageDirectory);
                    throw;
                }

                CleanupStagingDirectories(updatesRoot, stageKey, DateTime.UtcNow);
                return stagedDllPath;
            }
            catch
            {
                throw;
            }
            finally
            {
                if (attemptRoot != null && Directory.Exists(attemptRoot))
                    try { Directory.Delete(attemptRoot, true); } catch { }
                stagingLock.Release();
            }
        }

        internal static string CreateStageKey(SemVersion version, string packageHash)
        {
            if (version == null) throw new ArgumentNullException("version");
            if (!UpdatePackageValidator.IsValidSha256(packageHash)) throw new InvalidDataException("A valid package hash is required for staging.");
            return version + "-" + packageHash.Substring(0, 12).ToLowerInvariant();
        }

        internal static string GetRequiredPackageHash(string manifest, string assetName)
        {
            if (string.IsNullOrWhiteSpace(manifest) || string.IsNullOrWhiteSpace(assetName))
                throw new InvalidDataException("The package hash is missing or invalid in SHA256SUMS.txt.");
            string hash = null;
            foreach (var rawLine in manifest.Replace("\r", string.Empty).Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length < 65) continue;
                var lineHash = line.Substring(0, 64);
                var lineAsset = line.Substring(64).TrimStart(' ', '*').Trim();
                if (!string.Equals(lineAsset, assetName, StringComparison.OrdinalIgnoreCase)) continue;
                if (hash != null) throw new InvalidDataException("SHA256SUMS.txt contains duplicate entries for the package.");
                hash = lineHash;
            }
            if (!UpdatePackageValidator.IsValidSha256(hash))
                throw new InvalidDataException("The package hash is missing or invalid in SHA256SUMS.txt.");
            return hash;
        }

        internal static bool IsReusableStage(string stageDirectory, SemVersion expectedVersion)
        {
            try
            {
                var dll = Path.Combine(stageDirectory, "SSRename.dll");
                var marker = Path.Combine(stageDirectory, "SSRename.dll.sha256");
                if (!File.Exists(dll) || !File.Exists(marker)) return false;
                if (IsReparsePoint(dll) || IsReparsePoint(marker) || IsReparsePoint(stageDirectory)) return false;
                var expectedDllHash = File.ReadAllText(marker, Encoding.ASCII).Trim();
                if (!UpdatePackageValidator.IsValidSha256(expectedDllHash) || !HashVerifier.VerifySha256(dll, expectedDllHash)) return false;
                var assembly = AssemblyName.GetAssemblyName(dll);
                if (!string.Equals(assembly.Name, "SSRename", StringComparison.Ordinal)) return false;
                SemVersion actual;
                var versionText = assembly.Version.Major + "." + assembly.Version.Minor + "." + assembly.Version.Build;
                return SemVersion.TryParse(versionText, out actual) && actual.CompareTo(expectedVersion) == 0;
            }
            catch { return false; }
        }

        internal static void CleanupStagingDirectories(string updatesRoot, string keepStageKey, DateTime utcNow)
        {
            if (!Directory.Exists(updatesRoot)) return;
            var stages = new List<DirectoryInfo>();
            foreach (var path in Directory.GetDirectories(updatesRoot))
            {
                var info = new DirectoryInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (IsStageDirectoryName(info.Name))
                {
                    if (utcNow - info.LastWriteTimeUtc > StageRetention && !string.Equals(info.Name, keepStageKey, StringComparison.OrdinalIgnoreCase))
                        DeleteOwnedDirectory(info.FullName);
                    else stages.Add(info);
                }
                else if (info.Name.StartsWith(".staging-", StringComparison.OrdinalIgnoreCase) && utcNow - info.CreationTimeUtc > TimeSpan.FromDays(1))
                    DeleteOwnedDirectory(info.FullName);
            }

            stages.RemoveAll(info => !Directory.Exists(info.FullName));
            stages.Sort(delegate(DirectoryInfo left, DirectoryInfo right) { return right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc); });
            var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(keepStageKey) && stages.Exists(info => string.Equals(info.Name, keepStageKey, StringComparison.OrdinalIgnoreCase)))
                retained.Add(keepStageKey);
            foreach (var stage in stages)
            {
                if (retained.Count >= MaximumRetainedStages) break;
                retained.Add(stage.Name);
            }
            foreach (var stage in stages)
                if (!retained.Contains(stage.Name)) DeleteOwnedDirectory(stage.FullName);
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static void DeleteOwnedDirectory(string path)
        {
            if (!Directory.Exists(path)) return;
            if (IsReparsePoint(path)) throw new InvalidDataException("Refusing to remove a reparse-point staging directory.");
            Directory.Delete(path, true);
        }

        private static bool IsStageDirectoryName(string name)
        {
            var separator = name.LastIndexOf('-');
            if (separator <= 0 || name.Length - separator - 1 != 12) return false;
            SemVersion version;
            if (!SemVersion.TryParse(name.Substring(0, separator), out version)) return false;
            for (var i = separator + 1; i < name.Length; i++)
            {
                var c = name[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        private static void CopyFileCancellable(string source, string destination, CancellationToken cancellationToken)
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                }
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
