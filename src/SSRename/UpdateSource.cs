namespace SSRename
{
    internal static class UpdateSource
    {
        // Set these only after the real public repository has been created and verified.
        public const string RepositoryOwner = "";
        public const string RepositoryName = "";

        public static bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(RepositoryOwner) && !string.IsNullOrWhiteSpace(RepositoryName); }
        }
    }
}
