using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

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
                if (!int.TryParse(Required(values, "pid"), out pid) || pid <= 0) throw new ArgumentException("Invalid PID.");
                var source = Path.GetFullPath(Required(values, "source"));
                var target = Path.GetFullPath(Required(values, "target"));
                var backup = Path.GetFullPath(Required(values, "backup"));
                var hash = Required(values, "sha256");
                ValidatePaths(source, target, backup);
                if (!global::SSRename.HashVerifier.VerifySha256(source, hash)) throw new InvalidDataException("Staged DLL hash mismatch.");

                try { Process.GetProcessById(pid).WaitForExit(); }
                catch (ArgumentException) { }

                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.Copy(target, backup, true);
                try
                {
                    File.Copy(source, target, true);
                }
                catch
                {
                    if (File.Exists(backup)) File.Copy(backup, target, true);
                    throw;
                }
                File.AppendAllText(logPath, DateTime.UtcNow.ToString("O") + " update installed; backup=" + backup + Environment.NewLine);
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                    File.AppendAllText(logPath, DateTime.UtcNow.ToString("O") + " update failed: " + ex + Environment.NewLine);
                }
                catch { }
                return 1;
            }
        }

        private static IDictionary<string, string> Parse(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index + 1 < args.Length; index += 2)
            {
                if (!args[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Invalid argument.");
                result[args[index].Substring(2)] = args[index + 1];
            }
            return result;
        }

        private static string Required(IDictionary<string, string> values, string key)
        {
            string value;
            if (!values.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Missing argument: " + key);
            return value;
        }

        private static void ValidatePaths(string source, string target, string backup)
        {
            var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SSRename")) + Path.DirectorySeparatorChar;
            if (!source.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(source), "SSRename.dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
                throw new InvalidDataException("Unsafe staged DLL path.");
            if (!string.Equals(Path.GetFileName(target), "SSRename.dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
                throw new InvalidDataException("Unsafe target DLL path.");
            if (!string.Equals(Path.GetDirectoryName(target), Path.GetDirectoryName(backup), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(backup), "SSRename.dll.bak", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unsafe backup path.");
        }
    }
}
