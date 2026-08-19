using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SSRename
{
    internal static class FileNameService
    {
        private static readonly Regex TimestampPattern = new Regex(
            @"(?<stamp>\d{8}_\d{6}(?:_\d{3})?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string BuildFileName(string sourcePath, string template, ScreenshotContext context, string language = null)
        {
            var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
            var match = TimestampPattern.Match(sourceName ?? string.Empty);
            var timestamp = match.Success
                ? match.Groups["stamp"].Value
                : File.GetLastWriteTime(sourcePath).ToString("yyyyMMdd_HHmmss_fff");
            var pattern = string.IsNullOrWhiteSpace(template)
                ? "ffxiv_{timestamp}_{character}@{world}_{place}_{weather}_{et}"
                : template.Trim();
            var name = pattern
                .Replace("{character}", CleanToken(context.CharacterName, Localization.T(language, "unknown_character")))
                .Replace("{world}", CleanToken(context.WorldName, Localization.T(language, "unknown_world")))
                .Replace("{place}", CleanToken(context.PlaceName, Localization.T(language, "unknown_place")))
                .Replace("{timestamp}", CleanToken(timestamp, Localization.T(language, "unknown_time")))
                .Replace("{weather}", CleanToken(context.WeatherName, Localization.T(language, "unknown_weather")))
                .Replace("{et}", CleanToken(context.EorzeaTime, Localization.T(language, "unknown_et")));
            name = CleanToken(name, "ffxiv_screenshot");
            if (name.Length > 180)
                name = name.Substring(0, 180).TrimEnd(' ', '.');
            return name + Path.GetExtension(sourcePath);
        }

        public static string FindAvailablePath(string destinationFolder, string fileName, string sourcePath, string language = null)
        {
            var candidate = Path.Combine(destinationFolder, fileName);
            if (PathEquals(candidate, sourcePath) || !File.Exists(candidate))
                return candidate;
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            for (var index = 2; index < 10000; index++)
            {
                candidate = Path.Combine(destinationFolder, stem + "_" + index + extension);
                if (!File.Exists(candidate))
                    return candidate;
            }
            throw new IOException(Localization.T(language, "collision_limit"));
        }

        public static string CleanToken(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                value = fallback;
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var c in value.Trim())
                builder.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);
            var result = Regex.Replace(builder.ToString(), @"_+", "_").Trim(' ', '.');
            return string.IsNullOrWhiteSpace(result) ? fallback : result;
        }

        private static bool PathEquals(string left, string right)
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
    }
}
