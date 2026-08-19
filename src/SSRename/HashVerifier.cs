using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SSRename
{
    internal static class HashVerifier
    {
        public static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var algorithm = SHA256.Create())
            {
                var hash = algorithm.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var value in hash) builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }

        public static bool VerifySha256(string path, string expected)
        {
            return !string.IsNullOrWhiteSpace(expected) &&
                string.Equals(ComputeSha256(path), expected.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static string FindHashForAsset(string manifest, string assetName)
        {
            if (string.IsNullOrWhiteSpace(manifest) || string.IsNullOrWhiteSpace(assetName)) return null;
            foreach (var raw in manifest.Replace("\r", string.Empty).Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length < 64) continue;
                var hash = line.Substring(0, 64);
                var name = line.Substring(64).TrimStart(' ', '*');
                if (string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)) return hash;
            }
            return null;
        }
    }
}
