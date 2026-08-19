namespace SSRename
{
    internal static class UpdateSource
    {
        public const string RepositoryOwner = "Roxyz0501";
        public const string RepositoryName = "ssrename";

        public static bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(RepositoryOwner) && !string.IsNullOrWhiteSpace(RepositoryName); }
        }
    }
}
