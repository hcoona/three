using System;
using Xunit;

namespace WebHdfs.Extensions.FileProviders.UnitTest
{
    public class UnitTest_FileInfo : IClassFixture<SettingsFixture>
    {
        private readonly Settings settings;
        private readonly Uri nameNodeUri;

        public UnitTest_FileInfo(SettingsFixture settingsFixture)
        {
            this.settings = settingsFixture.Settings;
            this.nameNodeUri = new Uri(this.settings.NameNodeUri);
        }

        [Fact]
        public void TestDirectory()
        {
            var directoryFileInfo = new WebHdfsFileInfo(nameNodeUri, settings.DirectoryPath);
            Assert.True(directoryFileInfo.Exists);
            Assert.Equal(0, directoryFileInfo.Length);
            Assert.True(directoryFileInfo.LastModified > DateTimeOffset.Parse("2010/1/1"));
            Assert.True(directoryFileInfo.IsDirectory);
        }

        [Fact]
        public void TestFile()
        {
            var fileFileInfo = new WebHdfsFileInfo(nameNodeUri, settings.FilePath);
            Assert.True(fileFileInfo.Exists);
            Assert.True(fileFileInfo.Length > 0);
            Assert.True(fileFileInfo.LastModified > DateTimeOffset.Parse("2010/1/1"));
            Assert.False(fileFileInfo.IsDirectory);
        }

        [Fact]
        public void TestNull()
        {
            var notExistingFileInfo = new WebHdfsFileInfo(nameNodeUri, settings.NotExistingPath);
            Assert.False(notExistingFileInfo.Exists);
            Assert.Equal(0L, notExistingFileInfo.Length);
            Assert.False(notExistingFileInfo.IsDirectory);
        }
    }
}
