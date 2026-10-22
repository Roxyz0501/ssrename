using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace SSRename.Tests
{
    internal static class Program
    {
        private static int failures;

        private static int Main()
        {
            Run("CSV quoted fields", TestCsv);
            Run("game data and weather", TestGameData);
            Run("file name format", TestFileName);
            Run("collision suffix", TestCollision);
            Run("watcher integration", TestWatcher);
            Run("support link launch and safe failure", TestSupportLink);
            Run("OS language mapping and fallback", TestLocalization);
            Run("SemVer comparison", TestSemVersion);
            Run("GitHub stable release parsing", TestReleaseParsing);
            Run("SHA-256 verification", TestHashVerification);
            Run("ZIP destination validation", TestZipPaths);
            Run("corrupt update asset rejection", TestCorruptAsset);
            Run("legacy Config language initialization", TestLegacyConfig);
            Run("settings no-op and fixed cadence", TestSettingsCadence);
            Run("settings final flush and reload", TestSettingsFinalFlush);
            Run("settings failed save preserves file and dirty state", TestSettingsFailureRetry);
            Run("watcher stop waits and preserves source", TestWatcherStop);
            Console.WriteLine(failures == 0 ? "All SSRename tests passed." : failures + " test(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        private static void TestCsv()
        {
            var fields = GameDataService.ParseCsvLine("1,\"場所,詳細\",\"a\"\"b\"");
            Equal(3, fields.Count);
            Equal("場所,詳細", fields[1]);
            Equal("a\"b", fields[2]);
        }

        private static void TestGameData()
        {
            var data = new GameDataService();
            True(!string.IsNullOrWhiteSpace(data.GetPlaceName(128)), "territory 128 has no place name");
            var weather = data.GetWeatherName(128, new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc));
            True(weather != "天気不明" && weather != "天気なし", "weather was not resolved: " + weather);
            Equal("ET0000", GameDataService.GetEorzeaTime(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        }

        private static void TestFileName()
        {
            var context = Context();
            context.PlaceName = "場所:東";
            var value = FileNameService.BuildFileName(
                @"C:\screenshots\ffxiv_20260731_142530_123.png",
                "ffxiv_{timestamp}_{character}@{world}_{place}_{weather}_{et}", context);
            Equal("ffxiv_20260731_142530_123_Taro Yamada@Chocobo_場所_東_快晴_ET1842.png", value);
        }

        private static void TestCollision()
        {
            var root = NewTemporaryRoot();
            try
            {
                var source = Path.Combine(root, "source.png");
                var existing = Path.Combine(root, "target.png");
                File.WriteAllText(source, "source");
                File.WriteAllText(existing, "existing");
                Equal(Path.Combine(root, "target_2.png"), FileNameService.FindAvailablePath(root, "target.png", source));
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestWatcher()
        {
            var root = NewTemporaryRoot();
            var sourceFolder = Path.Combine(root, "source");
            var destinationFolder = Path.Combine(root, "destination");
            Directory.CreateDirectory(sourceFolder);
            var completed = new ManualResetEvent(false);
            string movedTo = null;
            try
            {
                using (var watcher = new ScreenshotWatcher())
                {
                    watcher.FileRenamed += delegate(string oldPath, string newPath) { movedTo = newPath; completed.Set(); };
                    watcher.Restart(new PluginSettings
                    {
                        RenameEnabled = true,
                        ScreenshotFolder = sourceFolder,
                        UseScreenshotFolderAsDestination = false,
                        DestinationFolder = destinationFolder,
                        FileNameTemplate = "ffxiv_{timestamp}_{character}@{world}_{place}_{weather}_{et}"
                    }, Context);
                    var original = Path.Combine(sourceFolder, "ffxiv_20260731_142530_123.png");
                    File.WriteAllBytes(original, new byte[] { 1, 2, 3, 4 });
                    watcher.NotifyCreatedForTests(original);
                    True(completed.WaitOne(TimeSpan.FromSeconds(8)), "watcher timed out");
                    True(!File.Exists(original), "source was not moved");
                    True(File.Exists(movedTo), "destination does not exist");
                }
            }
            finally { completed.Dispose(); Directory.Delete(root, true); }
        }

        private static void TestSupportLink()
        {
            var calls = 0;
            ProcessStartInfo captured = null;
            string error;
            var opened = ExternalLinkService.TryOpenSupportPage(delegate(ProcessStartInfo startInfo)
            {
                calls++;
                captured = startInfo;
                return null;
            }, out error);
            True(opened, "support link was rejected: " + error);
            Equal(1, calls);
            Equal(ExternalLinkService.SupportUrl, captured.FileName);
            True(captured.UseShellExecute, "support link does not use the default browser");

            var failed = ExternalLinkService.TryOpenSupportPage(delegate(ProcessStartInfo startInfo)
            {
                throw new InvalidOperationException("browser unavailable");
            }, out error);
            True(!failed, "launcher failure was reported as success");
            True(!string.IsNullOrWhiteSpace(error) && error.Contains("browser unavailable"), "failure message was not preserved");
        }

        private static void TestLocalization()
        {
            Equal(Localization.Japanese, Localization.FromCulture(new CultureInfo("ja-JP")));
            Equal(Localization.ChineseSimplified, Localization.FromCulture(new CultureInfo("zh-TW")));
            Equal(Localization.Korean, Localization.FromCulture(new CultureInfo("ko-KR")));
            Equal(Localization.English, Localization.FromCulture(new CultureInfo("fr-FR")));
            Equal("Update", Localization.T("unsupported", "update_button"));
            Equal("支援", Localization.T(Localization.Japanese, "tab_support"));
            Equal("Support", Localization.T(Localization.English, "tab_support"));
            Equal("支持", Localization.T(Localization.ChineseSimplified, "tab_support"));
            Equal("후원", Localization.T(Localization.Korean, "tab_support"));
        }

        private static void TestSemVersion()
        {
            SemVersion one, newer, prerelease;
            True(SemVersion.TryParse("v1.2.3", out one), "v-prefixed SemVer failed");
            True(SemVersion.TryParse("1.3.0", out newer), "SemVer failed");
            True(SemVersion.TryParse("1.3.0-beta.1", out prerelease), "prerelease failed");
            True(newer.CompareTo(one) > 0, "new version was not greater");
            True(prerelease.CompareTo(newer) < 0, "prerelease was not lower than stable");
            SemVersion invalid; True(!SemVersion.TryParse("release-one", out invalid), "invalid tag was accepted");
        }

        private static void TestReleaseParsing()
        {
            var json = "[" +
                "{\"tag_name\":\"v2.0.0-beta.1\",\"name\":\"beta\",\"body\":\"x\",\"draft\":false,\"prerelease\":true,\"assets\":[]}," +
                "{\"tag_name\":\"v1.2.0\",\"name\":\"stable\",\"body\":\"notes\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"SSRename-1.2.0.zip\",\"browser_download_url\":\"https://github.com/owner/repo/releases/download/v1.2.0/SSRename-1.2.0.zip\",\"size\":123}]}," +
                "{\"tag_name\":\"v9.0.0\",\"name\":\"draft\",\"body\":\"x\",\"draft\":true,\"prerelease\":false,\"assets\":[]}]";
            var release = GitHubReleaseParser.ParseLatestStable(json);
            Equal("1.2.0", release.Version.ToString());
            Equal(1, release.Assets.Count);
            Equal("notes", release.Body);
            try { GitHubReleaseParser.ParseLatestStable("{bad json}"); throw new InvalidOperationException("invalid JSON was accepted"); }
            catch (Exception ex) { True(!(ex is InvalidOperationException) || ex.Message != "invalid JSON was accepted", "invalid JSON was accepted"); }
        }

        private static void TestHashVerification()
        {
            var root = NewTemporaryRoot();
            try
            {
                var path = Path.Combine(root, "asset.zip"); File.WriteAllText(path, "SSRename");
                var hash = HashVerifier.ComputeSha256(path);
                True(HashVerifier.VerifySha256(path, hash.ToUpperInvariant()), "valid hash failed");
                True(!HashVerifier.VerifySha256(path, new string('0', 64)), "bad hash passed");
                Equal(hash, HashVerifier.FindHashForAsset(hash + "  asset.zip\n", "asset.zip"));
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestZipPaths()
        {
            var root = NewTemporaryRoot();
            try
            {
                True(UpdatePackageValidator.IsSafeEntryPath(root, "SSRename.dll"), "valid entry rejected");
                True(!UpdatePackageValidator.IsSafeEntryPath(root, "../SSRename.dll"), "traversal accepted");
                True(!UpdatePackageValidator.IsSafeEntryPath(root, "folder/SSRename.dll"), "nested entry accepted");
                True(UpdatePackageService.IsAllowedGitHubUri(new Uri("https://github.com/owner/repo/releases/download/v1/a.zip")), "GitHub URL rejected");
                True(!UpdatePackageService.IsAllowedGitHubUri(new Uri("http://github.com/a.zip")), "HTTP accepted");
                True(!UpdatePackageService.IsAllowedGitHubUri(new Uri("https://example.com/a.zip")), "arbitrary host accepted");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestCorruptAsset()
        {
            var root = NewTemporaryRoot();
            try
            {
                var path = Path.Combine(root, "broken.zip"); File.WriteAllText(path, "not a zip");
                try
                {
                    UpdatePackageValidator.ValidateAndExtract(path, Path.Combine(root, "out"), null, new string('0', 64));
                    throw new InvalidOperationException("corrupt asset was accepted");
                }
                catch (InvalidDataException) { }
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestLegacyConfig()
        {
            var root = NewTemporaryRoot();
            try
            {
                var path = Path.Combine(root, "SSRename.config.xml");
                var first = PluginSettings.Load(path);
                True(first.LanguageWasMissing, "new Config was not marked for OS language detection");
                True(first.CheckUpdatesOnStartup, "update check default is not enabled");
                first.Language = Localization.Korean;
                first.Save(path);
                var second = PluginSettings.Load(path);
                True(!second.LanguageWasMissing, "saved language was treated as missing");
                Equal(Localization.Korean, second.Language);
                File.WriteAllText(path, "<?xml version=\"1.0\"?><PluginSettings><RenameEnabled>true</RenameEnabled></PluginSettings>");
                var legacy = PluginSettings.Load(path);
                True(legacy.LanguageWasMissing, "legacy Config did not trigger one-time language detection");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestSettingsCadence()
        {
            Equal(TimeSpan.FromSeconds(60), SettingsSaveCoordinator.DefaultInterval);
            var baseline = new PluginSettings { Language = Localization.English };
            var current = baseline.Clone();
            var writes = 0;
            using (var noOp = new SettingsSaveCoordinator(baseline, delegate { return current.Clone(); }, delegate { Interlocked.Increment(ref writes); }, TimeSpan.FromMilliseconds(100)))
            {
                noOp.Start();
                Thread.Sleep(250);
                Equal(0, writes);
            }
            using (var coordinator = new SettingsSaveCoordinator(baseline, delegate { return current.Clone(); }, delegate { Interlocked.Increment(ref writes); }, TimeSpan.FromMilliseconds(600)))
            {
                coordinator.Start();
                Thread.Sleep(100);
                current.FileNameTemplate = "edit-one";
                Thread.Sleep(70);
                current.FileNameTemplate = "edit-two";
                Thread.Sleep(150);
                Equal(0, writes);
                True(SpinWait.SpinUntil(delegate { return Volatile.Read(ref writes) == 1; }, 500), "first fixed cadence save did not happen");
                Equal("edit-two", current.FileNameTemplate);
                Thread.Sleep(300);
                Equal(1, writes);
            }
        }

        private static void TestSettingsFinalFlush()
        {
            var root = NewTemporaryRoot();
            try
            {
                var path = Path.Combine(root, "config.xml");
                var baseline = new PluginSettings { Language = Localization.English };
                baseline.Save(path);
                var current = baseline.Clone();
                using (var coordinator = SettingsSaveCoordinator.ForFile(path, baseline, delegate { return current.Clone(); }, TimeSpan.FromSeconds(30)))
                {
                    coordinator.Start();
                    current.Language = Localization.Japanese;
                    True(coordinator.StopAndFlush(), "final save failed");
                }
                Equal(Localization.Japanese, PluginSettings.Load(path).Language);
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestSettingsFailureRetry()
        {
            var root = NewTemporaryRoot();
            try
            {
                var path = Path.Combine(root, "config.xml");
                var baseline = new PluginSettings { Language = Localization.English };
                baseline.Save(path);
                var original = File.ReadAllBytes(path);
                var current = baseline.Clone(); current.Language = Localization.Korean;
                var failed = 0;
                using (var coordinator = SettingsSaveCoordinator.ForFile(path, baseline, delegate { return current.Clone(); }, TimeSpan.FromMilliseconds(200)))
                {
                    coordinator.SaveFailed += delegate { Interlocked.Increment(ref failed); };
                    coordinator.Start();
                    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        True(SpinWait.SpinUntil(delegate { return Volatile.Read(ref failed) > 0; }, 1000), "locked save did not fail");
                        var afterFailure = File.ReadAllBytes(path);
                        True(BytesEqual(original, afterFailure), "failed save damaged existing config");
                    }
                    True(SpinWait.SpinUntil(delegate { return Localization.Korean == PluginSettings.Load(path).Language; }, 1500), "dirty save was not retried after failure");
                }
                True(failed > 0, "dirty save failure was not retained/retried");
                Equal(Localization.Korean, PluginSettings.Load(path).Language);
                Equal(0, Directory.GetFiles(root, "*.tmp").Length);
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestWatcherStop()
        {
            var root = NewTemporaryRoot();
            var sourceFolder = Path.Combine(root, "source"); var destination = Path.Combine(root, "destination"); Directory.CreateDirectory(sourceFolder);
            var entered = new ManualResetEvent(false); var release = new ManualResetEvent(false);
            try
            {
                using (var watcher = new ScreenshotWatcher(delegate(string path, TimeSpan timeout, CancellationToken token)
                {
                    entered.Set(); release.WaitOne(); return true;
                }))
                {
                    watcher.Restart(new PluginSettings { RenameEnabled = true, ScreenshotFolder = sourceFolder, UseScreenshotFolderAsDestination = false, DestinationFolder = destination, Language = Localization.English }, Context);
                    var original = Path.Combine(sourceFolder, "shot.png"); File.WriteAllBytes(original, new byte[] { 1, 2, 3 });
                    watcher.NotifyCreatedForTests(original);
                    True(entered.WaitOne(3000), "watch job did not start");
                    var stopping = System.Threading.Tasks.Task.Run((Action)watcher.Stop);
                    Thread.Sleep(100);
                    True(!stopping.IsCompleted, "Stop returned before in-flight processing completed");
                    release.Set();
                    True(stopping.Wait(3000), "Stop did not complete after job release");
                    True(File.Exists(original), "cancelled job moved the original image");
                    Equal(0, watcher.ActiveJobCount);
                }
            }
            finally { release.Set(); entered.Dispose(); release.Dispose(); Directory.Delete(root, true); }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private static ScreenshotContext Context()
        {
            return new ScreenshotContext
            {
                CharacterName = "Taro Yamada",
                WorldName = "Chocobo",
                PlaceName = "リムサ・ロミンサ：上甲板層",
                WeatherName = "快晴",
                EorzeaTime = "ET1842",
                CapturedAtUtc = DateTime.UtcNow
            };
        }

        private static string NewTemporaryRoot()
        {
            var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-temp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void Run(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!object.Equals(expected, actual))
                throw new InvalidOperationException("expected [" + expected + "] but was [" + actual + "]");
        }

        private static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
