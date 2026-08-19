using System;
using System.IO;
using System.Xml.Serialization;

namespace SSRename
{
    [Serializable]
    public sealed class PluginSettings
    {
        public bool RenameEnabled { get; set; } = true;
        public string ScreenshotFolder { get; set; } = GetDefaultScreenshotFolder();
        public bool UseScreenshotFolderAsDestination { get; set; } = true;
        public string DestinationFolder { get; set; } = GetDefaultScreenshotFolder();
        public string FileNameTemplate { get; set; } = "ffxiv_{timestamp}_{character}@{world}_{place}_{weather}_{et}";
        public string Language { get; set; }
        public bool CheckUpdatesOnStartup { get; set; } = true;
        public string SkippedVersion { get; set; }
        public string LastUpdateCheckUtc { get; set; }

        [XmlIgnore]
        public bool LanguageWasMissing { get; private set; }

        public static string GetDefaultScreenshotFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "My Games", "FINAL FANTASY XIV - A Realm Reborn", "screenshots");
        }

        public static PluginSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    var created = new PluginSettings();
                    created.LanguageWasMissing = true;
                    return created;
                }
                using (var stream = File.OpenRead(path))
                {
                    var settings = (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(stream);
                    if (string.IsNullOrWhiteSpace(settings.FileNameTemplate) ||
                        settings.FileNameTemplate == "ffxiv_{character}@{world}_{place}_{timestamp}_{weather}_{et}")
                        settings.FileNameTemplate = "ffxiv_{timestamp}_{character}@{world}_{place}_{weather}_{et}";
                    settings.LanguageWasMissing = string.IsNullOrWhiteSpace(settings.Language);
                    return settings;
                }
            }
            catch
            {
                var fallback = new PluginSettings();
                fallback.LanguageWasMissing = true;
                return fallback;
            }
        }

        public void Save(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            using (var stream = File.Create(path))
                new XmlSerializer(typeof(PluginSettings)).Serialize(stream, this);
        }

        public string EffectiveDestinationFolder
        {
            get { return UseScreenshotFolderAsDestination ? ScreenshotFolder : DestinationFolder; }
        }
    }
}
