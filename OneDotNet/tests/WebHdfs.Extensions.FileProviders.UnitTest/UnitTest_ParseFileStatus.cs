using Xunit;

namespace WebHdfs.Extensions.FileProviders.UnitTest
{
    public class UnitTest_ParseFileStatus
    {
        [Fact]
        public void TestParseDirectoryFileStatus()
        {
            var fileStatus = WebHdfsFileStatus.ParseJson(@"
{""FileStatus"":{""accessTime"":0,""blockSize"":0,""childrenNum"":67,""fileId"":16439,""group"":""supergroup"",""length"":0,""modificationTime"":1506476570392,""owner"":""hadoop"",""pathSuffix"":"""",""permission"":""755"",""replication"":0,""storagePolicy"":0,""type"":""DIRECTORY""}}
");
            Assert.Equal(0, fileStatus.Length);
            Assert.Equal(1506476570392, fileStatus.ModificationTime);
            Assert.Equal(WebHdfsFileType.DIRECTORY, fileStatus.Type);
        }

        [Fact]
        public void TestParseFileFileStatus()
        {
            var fileStatus = WebHdfsFileStatus.ParseJson(@"
{""FileStatus"":{""accessTime"":1506564003395,""blockSize"":268435456,""childrenNum"":0,""fileId"":27525118,""group"":""supergroup"",""length"":1516,""modificationTime"":1501666036000,""owner"":""hadoop"",""pathSuffix"":"""",""permission"":""644"",""replication"":4,""storagePolicy"":0,""type"":""FILE""}}
");
            Assert.Equal(1516, fileStatus.Length);
            Assert.Equal(1501666036000, fileStatus.ModificationTime);
            Assert.Equal(WebHdfsFileType.FILE, fileStatus.Type);
        }
    }
}
