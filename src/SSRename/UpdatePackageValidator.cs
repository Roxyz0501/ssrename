using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;

namespace SSRename
{
    internal static class UpdatePackageValidator
    {
        internal const int MaximumEntries = 5;
        internal const long MaximumEntryBytes = 10L * 1024L * 1024L;
        internal const long MaximumTotalExpandedBytes = 25L * 1024L * 1024L;

        internal sealed class EntryMetadata
        {
            public string Name;
            public long Length;
        }

        private static readonly HashSet<string> AllowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "SSRename.dll", "SSRename.pdb", "SSRename.Updater.exe", "README.md", "THIRD_PARTY_DATA.md" };

        public static string ValidateAndExtract(string zipPath, string destination, SemVersion expectedVersion, string expectedHash, CancellationToken cancellationToken)
        {
            if (!IsValidSha256(expectedHash))
                throw new InvalidDataException("A valid SHA-256 manifest entry is required.");
            if (!HashVerifier.VerifySha256(zipPath, expectedHash))
                throw new InvalidDataException("SHA-256 mismatch.");
            cancellationToken.ThrowIfCancellationRequested();

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.Entries.Count > MaximumEntries)
                    throw new InvalidDataException("The package contains too many entries.");
                ValidateEntryMetadata(EnumerateMetadata(archive));

                if (Directory.Exists(destination))
                {
                    if (Directory.GetFileSystemEntries(destination).Length != 0)
                        throw new InvalidDataException("The extraction directory must be empty.");
                }
                else Directory.CreateDirectory(destination);

                var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                long totalWritten = 0;
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = Path.Combine(root, entry.FullName);
                    long entryWritten = 0;
                    using (var input = entry.Open())
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (read > MaximumEntryBytes - entryWritten || read > MaximumTotalExpandedBytes - totalWritten)
                                throw new InvalidDataException("The expanded package exceeds its configured limits.");
                            output.Write(buffer, 0, read);
                            entryWritten += read;
                            totalWritten += read;
                        }
                    }
                    if (entryWritten != entry.Length)
                        throw new InvalidDataException("An archive entry size did not match its metadata.");
                }
            }

            var dllPath = Path.Combine(Path.GetFullPath(destination), "SSRename.dll");
            if (!File.Exists(dllPath)) throw new InvalidDataException("SSRename.dll is missing from the update package.");
            cancellationToken.ThrowIfCancellationRequested();
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

        private static IEnumerable<EntryMetadata> EnumerateMetadata(ZipArchive archive)
        {
            foreach (var entry in archive.Entries)
                yield return new EntryMetadata { Name = entry.FullName, Length = entry.Length };
        }

        internal static void ValidateEntryMetadata(IEnumerable<EntryMetadata> entries)
        {
            if (entries == null) throw new ArgumentNullException("entries");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var count = 0;
            long total = 0;
            var hasDll = false;
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                    throw new InvalidDataException("Directory and empty archive entries are not allowed.");
                count++;
                if (count > MaximumEntries)
                    throw new InvalidDataException("The package contains too many entries.");
                if (!names.Add(entry.Name))
                    throw new InvalidDataException("Duplicate archive entry names are not allowed.");
                if (!AllowedFiles.Contains(entry.Name) || entry.Name.IndexOf('/') >= 0 || entry.Name.IndexOf('\\') >= 0)
                    throw new InvalidDataException("Unexpected or unsafe package entry: " + entry.Name);
                if (entry.Length < 0 || entry.Length > MaximumEntryBytes)
                    throw new InvalidDataException("An archive entry exceeds the per-file expansion limit.");
                if (entry.Length > MaximumTotalExpandedBytes - total)
                    throw new InvalidDataException("The package exceeds the total expansion limit.");
                total += entry.Length;
                if (string.Equals(entry.Name, "SSRename.dll", StringComparison.OrdinalIgnoreCase)) hasDll = true;
            }
            if (!hasDll) throw new InvalidDataException("SSRename.dll is missing from the update package.");
        }

        internal static bool IsValidSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64) return false;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
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
