using System;
using System.Diagnostics;

namespace SSRename
{
    internal static class ExternalLinkService
    {
        public const string SupportUrl = "https://ko-fi.com/roxyz0501";

        public static bool TryOpenSupportPage(out string error)
        {
            return TryOpenSupportPage(Localization.English, delegate(ProcessStartInfo startInfo)
            {
                return Process.Start(startInfo);
            }, out error);
        }

        public static bool TryOpenSupportPage(string language, out string error)
        {
            return TryOpenSupportPage(language, delegate(ProcessStartInfo startInfo) { return Process.Start(startInfo); }, out error);
        }

        internal static bool TryOpenSupportPage(Func<ProcessStartInfo, Process> launcher, out string error)
        {
            return TryOpenSupportPage(Localization.English, launcher, out error);
        }

        internal static bool TryOpenSupportPage(string language, Func<ProcessStartInfo, Process> launcher, out string error)
        {
            error = null;
            if (launcher == null)
            {
                error = Localization.T(language, "support_launcher_unavailable");
                return false;
            }

            try
            {
                var uri = new Uri(SupportUrl, UriKind.Absolute);
                if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(uri.Host, "ko-fi.com", StringComparison.OrdinalIgnoreCase))
                {
                    error = Localization.T(language, "support_unsafe_url");
                    return false;
                }

                launcher(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                error = Localization.T(language, "support_failed", ex.GetBaseException().Message);
                return false;
            }
        }
    }
}
