using System;
using Xunit;
using WebHdfs.Extensions.FileProviders;

namespace WebHdfs.Extensions.FileProviders.UnitTest
{
    public class UnitTest_FileInfo : IClassFixture<SettingsFixture>
    {
        private readonly Settings settings;
        private readonly Uri nameNodeUri;

        public UnitTest_FileInfo(SettingsFixture settingsFixture)
        {
            this.settings = settingsFixture.Settings;
            this.nameNodeUri = this.settings.IsConfigured ? new Uri(this.settings.NameNodeUri) : new Uri("http://localhost");
        }

        [Fact]
        public void TestDirectory()
        {
            if (!this.settings.IsConfigured)
            {
                return;
            }

            var directoryFileInfo = new WebHdfsFileInfo(this.nameNodeUri, this.settings.DirectoryPath);
            Assert.True(directoryFileInfo.Exists);
            Assert.Equal(0, directoryFileInfo.Length);
            Assert.True(directoryFileInfo.LastModified > DateTimeOffset.Parse("2010/1/1"));
            Assert.True(directoryFileInfo.IsDirectory);
        }

        [Fact]
        public void TestFile()
        {
            if (!this.settings.IsConfigured)
            {
                return;
            }

            var fileFileInfo = new WebHdfsFileInfo(this.nameNodeUri, this.settings.FilePath);
            Assert.True(fileFileInfo.Exists);
            Assert.True(fileFileInfo.Length > 0);
            Assert.True(fileFileInfo.LastModified > DateTimeOffset.Parse("2010/1/1"));
            Assert.False(fileFileInfo.IsDirectory);
        }

        [Fact]
        public void TestNull()
        {
            if (!this.settings.IsConfigured)
            {
                return;
            }

            var notExistingFileInfo = new WebHdfsFileInfo(this.nameNodeUri, this.settings.NotExistingPath);
            Assert.False(notExistingFileInfo.Exists);
            Assert.Equal(0L, notExistingFileInfo.Length);
            Assert.False(notExistingFileInfo.IsDirectory);
        }
    }
}
