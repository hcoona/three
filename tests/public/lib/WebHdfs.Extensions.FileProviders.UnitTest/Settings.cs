namespace WebHdfs.Extensions.FileProviders.UnitTest
{
    internal sealed class Settings
    {
        public string NameNodeUri { get; set; }

        public string FilePath { get; set; }

        public string DirectoryPath { get; set; }

        public string NotExistingPath { get; set; }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(NameNodeUri)
            && !string.IsNullOrWhiteSpace(FilePath)
            && !string.IsNullOrWhiteSpace(DirectoryPath)
            && !string.IsNullOrWhiteSpace(NotExistingPath);
    }
}
