using System;
using System.IO;
using System.Text;
using System.Xml;
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

        public PluginSettings Clone()
        {
            return new PluginSettings
            {
                RenameEnabled = RenameEnabled,
                ScreenshotFolder = ScreenshotFolder,
                UseScreenshotFolderAsDestination = UseScreenshotFolderAsDestination,
                DestinationFolder = DestinationFolder,
                FileNameTemplate = FileNameTemplate,
                Language = Language,
                CheckUpdatesOnStartup = CheckUpdatesOnStartup,
                SkippedVersion = SkippedVersion,
                LastUpdateCheckUtc = LastUpdateCheckUtc
            };
        }

        internal byte[] SerializeUtf8()
        {
            using (var memory = new MemoryStream())
            {
                var settings = new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false),
                    Indent = true,
                    CloseOutput = false
                };
                using (var writer = XmlWriter.Create(memory, settings))
                    new XmlSerializer(typeof(PluginSettings)).Serialize(writer, this);
                return memory.ToArray();
            }
        }

        public void Save(string path)
        {
            SaveAtomic(path);
        }

        internal void SaveAtomic(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A settings path is required.", "path");
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var payload = SerializeUtf8();
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        public string EffectiveDestinationFolder
        {
            get { return UseScreenshotFolderAsDestination ? ScreenshotFolder : DestinationFolder; }
        }
    }
}
