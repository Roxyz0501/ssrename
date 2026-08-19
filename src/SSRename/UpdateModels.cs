using System;
using System.Collections.Generic;

namespace SSRename
{
    internal sealed class ReleaseAssetInfo
    {
        public string Name { get; set; }
        public string DownloadUrl { get; set; }
        public long Size { get; set; }
    }

    internal sealed class ReleaseInfo
    {
        public string TagName { get; set; }
        public string Name { get; set; }
        public string Body { get; set; }
        public bool Draft { get; set; }
        public bool Prerelease { get; set; }
        public IList<ReleaseAssetInfo> Assets { get; set; } = new List<ReleaseAssetInfo>();
        public SemVersion Version { get; set; }
    }

    internal enum UpdateCheckStatus
    {
        NotConfigured,
        UpToDate,
        Available,
        Skipped,
        Failed
    }

    internal sealed class UpdateCheckResult
    {
        public UpdateCheckStatus Status { get; set; }
        public ReleaseInfo Release { get; set; }
        public string Error { get; set; }
        public DateTime CheckedAtUtc { get; set; }
    }

    internal sealed class UpdateUiState
    {
        public bool Checking { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string LastChecked { get; set; }
        public string Notes { get; set; }
        public string MessageKey { get; set; }
        public object[] MessageArgs { get; set; }
        public bool IsError { get; set; }
        public bool CanInstall { get; set; }
        public bool CanSkip { get; set; }
        public ReleaseInfo Release { get; set; }
    }
}
