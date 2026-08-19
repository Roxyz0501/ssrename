using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SSRename
{
    internal sealed class SemVersion : IComparable<SemVersion>
    {
        private static readonly Regex Pattern = new Regex(
            @"^[vV]?(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }
        public string Prerelease { get; private set; }

        public static bool TryParse(string value, out SemVersion version)
        {
            version = null;
            var match = Pattern.Match(value == null ? string.Empty : value.Trim());
            int major, minor, patch;
            if (!match.Success ||
                !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major) ||
                !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor) ||
                !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch))
                return false;
            version = new SemVersion { Major = major, Minor = minor, Patch = patch, Prerelease = match.Groups["pre"].Value };
            return true;
        }

        public int CompareTo(SemVersion other)
        {
            if (other == null) return 1;
            var result = Major.CompareTo(other.Major);
            if (result == 0) result = Minor.CompareTo(other.Minor);
            if (result == 0) result = Patch.CompareTo(other.Patch);
            if (result != 0) return result;
            if (string.IsNullOrEmpty(Prerelease)) return string.IsNullOrEmpty(other.Prerelease) ? 0 : 1;
            if (string.IsNullOrEmpty(other.Prerelease)) return -1;
            var left = Prerelease.Split('.');
            var right = other.Prerelease.Split('.');
            for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
            {
                if (index >= left.Length) return -1;
                if (index >= right.Length) return 1;
                int leftNumber, rightNumber;
                var leftNumeric = int.TryParse(left[index], out leftNumber);
                var rightNumeric = int.TryParse(right[index], out rightNumber);
                if (leftNumeric && rightNumeric) result = leftNumber.CompareTo(rightNumber);
                else if (leftNumeric != rightNumeric) result = leftNumeric ? -1 : 1;
                else result = string.Compare(left[index], right[index], StringComparison.Ordinal);
                if (result != 0) return result;
            }
            return 0;
        }

        public override string ToString()
        {
            return Major + "." + Minor + "." + Patch + (string.IsNullOrEmpty(Prerelease) ? string.Empty : "-" + Prerelease);
        }
    }
}
