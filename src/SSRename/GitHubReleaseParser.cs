using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace SSRename
{
    internal static class GitHubReleaseParser
    {
        public static ReleaseInfo ParseLatestStable(string json)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json) as object[];
            if (root == null) throw new FormatException("GitHub release response is not an array.");
            ReleaseInfo best = null;
            foreach (var item in root)
            {
                var data = item as IDictionary<string, object>;
                if (data == null) continue;
                var release = Parse(data);
                if (release.Draft || release.Prerelease || release.Version == null) continue;
                if (best == null || release.Version.CompareTo(best.Version) > 0) best = release;
            }
            return best;
        }

        private static ReleaseInfo Parse(IDictionary<string, object> data)
        {
            var release = new ReleaseInfo
            {
                TagName = StringValue(data, "tag_name"),
                Name = StringValue(data, "name"),
                Body = StringValue(data, "body"),
                Draft = BoolValue(data, "draft"),
                Prerelease = BoolValue(data, "prerelease")
            };
            SemVersion version;
            if (SemVersion.TryParse(release.TagName, out version)) release.Version = version;
            object assetsValue;
            var assets = data.TryGetValue("assets", out assetsValue) ? assetsValue as object[] : null;
            if (assets != null)
            {
                foreach (var item in assets)
                {
                    var asset = item as IDictionary<string, object>;
                    if (asset == null) continue;
                    release.Assets.Add(new ReleaseAssetInfo
                    {
                        Name = StringValue(asset, "name"),
                        DownloadUrl = StringValue(asset, "browser_download_url"),
                        Size = LongValue(asset, "size")
                    });
                }
            }
            return release;
        }

        private static string StringValue(IDictionary<string, object> values, string key)
        {
            object value; return values.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : string.Empty;
        }
        private static bool BoolValue(IDictionary<string, object> values, string key)
        {
            object value; return values.TryGetValue(key, out value) && value != null && Convert.ToBoolean(value);
        }
        private static long LongValue(IDictionary<string, object> values, string key)
        {
            object value; return values.TryGetValue(key, out value) && value != null ? Convert.ToInt64(value) : 0;
        }
    }
}
