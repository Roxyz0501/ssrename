using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace SSRename
{
    internal static class UpdatePackageValidator
    {
        private static readonly HashSet<string> AllowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "SSRename.dll", "SSRename.pdb", "SSRename.Updater.exe", "README.md", "THIRD_PARTY_DATA.md" };

        public static string ValidateAndExtract(string zipPath, string destination, SemVersion expectedVersion, string expectedHash)
        {
            if (!string.IsNullOrWhiteSpace(expectedHash) && !HashVerifier.VerifySha256(zipPath, expectedHash))
                throw new InvalidDataException("SHA-256 mismatch.");
            Directory.CreateDirectory(destination);
            var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var dllPath = Path.Combine(root, "SSRename.dll");
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    if (!AllowedFiles.Contains(entry.FullName) || !IsSafeEntryPath(root, entry.FullName))
                        throw new InvalidDataException("Unexpected or unsafe package entry: " + entry.FullName);
                    var target = Path.Combine(root, entry.FullName);
                    entry.ExtractToFile(target, true);
                }
            }
            if (!File.Exists(dllPath)) throw new InvalidDataException("SSRename.dll is missing from the update package.");
            var assembly = AssemblyName.GetAssemblyName(dllPath);
            if (!string.Equals(assembly.Name, "SSRename", StringComparison.Ordinal))
                throw new InvalidDataException("The package does not contain SSRename.");
            if (expectedVersion != null)
            {
                SemVersion actual;
                var versionText = assembly.Version.Major + "." + assembly.Version.Minor + "." + assembly.Version.Build;
                if (!SemVersion.TryParse(versionText, out actual) || actual.CompareTo(expectedVersion) != 0)
                    throw new InvalidDataException("Package version does not match the release tag.");
            }
            return dllPath;
        }

        internal static bool IsSafeEntryPath(string destinationRoot, string entryName)
        {
            if (string.IsNullOrWhiteSpace(destinationRoot) || string.IsNullOrWhiteSpace(entryName) ||
                Path.IsPathRooted(entryName) || entryName.IndexOf('/') >= 0 || entryName.IndexOf('\\') >= 0)
                return false;
            var root = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(root, entryName));
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }
}
