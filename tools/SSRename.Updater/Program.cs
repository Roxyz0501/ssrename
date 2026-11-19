using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace SSRename.Updater
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var logPath = Path.Combine(Path.GetTempPath(), "SSRename", "updater.log");
            try
            {
                var values = Parse(args);
                int pid;
                if (!int.TryParse(Required(values, "pid"), out pid) || pid <= 0) throw new ArgumentException("Invalid process ID.");
                var source = Path.GetFullPath(Required(values, "source"));
                var target = NormalizeTarget(Required(values, "target"));
                var expectedHash = Required(values, "sha256");
                var originalHash = Required(values, "original");
                var version = Required(values, "version");
                long processStartTicks;
                if (!long.TryParse(Required(values, "pid-start"), out processStartTicks) || processStartTicks <= 0) throw new ArgumentException("Invalid process start time.");
                ValidateIdentity(version);
                ValidatePaths(source, target);
                if (!global::SSRename.HashVerifier.VerifySha256(source, expectedHash)) throw new InvalidDataException("Staged DLL hash mismatch.");
                ValidateDllVersion(source, version);
                WaitForExit(pid, processStartTicks);
                if (!global::SSRename.HashVerifier.VerifySha256(target, originalHash)) throw new IOException("Installed DLL changed after the update was requested.");
                ReplaceWithBackup(source, target, expectedHash, version);
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.AppendAllText(logPath, DateTime.UtcNow.ToString("O") + " update installed; target=" + target + Environment.NewLine);
                return 0;
            }
            catch (Exception ex)
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)); File.AppendAllText(logPath, DateTime.UtcNow.ToString("O") + " update failed: " + ex + Environment.NewLine); }
                catch { }
                return 1;
            }
        }

        internal static void ReplaceWithBackup(string source, string target, string expectedHash, string version)
        {
            ReplaceWithBackup(source, target, expectedHash, version, (incoming, destination) => File.Replace(incoming, destination, null, true));
        }

        internal static void ReplaceWithBackup(string source, string target, string expectedHash, string version, Action<string, string> installCopy)
        {
            var directory = Path.GetDirectoryName(target);
            var backup = target + ".backup-" + version + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            if (File.Exists(backup)) backup += "-" + Guid.NewGuid().ToString("N");
            var incoming = target + ".update-new";
            if (File.Exists(incoming)) File.Delete(incoming);
            File.Copy(source, incoming, false);
            try
            {
                if (!global::SSRename.HashVerifier.VerifySha256(incoming, expectedHash)) throw new InvalidDataException("Copied DLL hash mismatch.");
                File.Copy(target, backup, false);
                try
                {
                    installCopy(incoming, target);
                    if (!global::SSRename.HashVerifier.VerifySha256(target, expectedHash)) throw new InvalidDataException("Installed DLL hash mismatch.");
                }
                catch
                {
                    if (File.Exists(backup)) File.Copy(backup, target, true);
                    throw;
                }
                File.WriteAllText(Path.Combine(directory, "SSRename.update-result.txt"), "Updated to " + version + " at " + DateTime.UtcNow.ToString("O") + Environment.NewLine + "Backup: " + backup + Environment.NewLine);
            }
            finally { if (File.Exists(incoming)) File.Delete(incoming); }
        }

        private static void ValidateIdentity(string expectedVersion)
        {
            var identity = Assembly.GetExecutingAssembly().GetName();
            if (!string.Equals(identity.Name, "SSRename.Updater", StringComparison.Ordinal)) throw new InvalidDataException("Unexpected updater identity.");
            var actualVersion = identity.Version.Major + "." + identity.Version.Minor + "." + identity.Version.Build;
            if (!string.Equals(actualVersion, expectedVersion, StringComparison.Ordinal)) throw new InvalidDataException("Updater version mismatch.");
            var company = (AssemblyCompanyAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyCompanyAttribute));
            if (company == null || !string.Equals(company.Company, "Roxyz0501", StringComparison.Ordinal)) throw new InvalidDataException("Unexpected updater company.");
        }

        private static void ValidateDllVersion(string source, string expectedVersion)
        {
            var version = AssemblyName.GetAssemblyName(source).Version;
            var actual = version.Major + "." + version.Minor + "." + version.Build;
            if (!string.Equals(actual, expectedVersion, StringComparison.Ordinal)) throw new InvalidDataException("Staged DLL version mismatch.");
        }

        private static string NormalizeTarget(string value)
        {
            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.IsFile) value = uri.LocalPath;
            return Path.GetFullPath(value);
        }

        private static void ValidatePaths(string source, string target)
        {
            var stageRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SSRename", "updates")) + Path.DirectorySeparatorChar;
            if (!source.StartsWith(stageRoot, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetFileName(source), "SSRename.dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
                throw new InvalidDataException("Unsafe staged DLL path.");
            if (!string.Equals(Path.GetFileName(target), "SSRename.dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
                throw new InvalidDataException("Unsafe target DLL path.");
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Source and target must differ.");
        }

        private static void WaitForExit(int processId, long expectedStartTicks)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (process.StartTime.ToUniversalTime().Ticks != expectedStartTicks) return;
                    process.WaitForExit();
                }
            }
            catch (ArgumentException) { }
        }

        private static Dictionary<string, string> Parse(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (args == null || args.Length == 0 || args.Length % 2 != 0) throw new ArgumentException("Invalid updater arguments.");
            for (var i = 0; i < args.Length; i += 2)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal) || result.ContainsKey(args[i])) throw new ArgumentException("Invalid updater argument.");
                result[args[i].Substring(2)] = args[i + 1];
            }
            return result;
        }

        private static string Required(IDictionary<string, string> values, string key)
        {
            string value;
            if (!values.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Missing argument: " + key);
            return value;
        }
    }
}
