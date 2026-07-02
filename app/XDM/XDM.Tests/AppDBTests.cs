using System;
using System.IO;
using NUnit.Framework;
using XDM.Core;
using XDM.Core.DataAccess;

namespace XDM.Tests
{
    // Round-trip test for the download database. Also proves the
    // Microsoft.Data.Sqlite native library loads on this platform
    // (the old System.Data.SQLite had no osx-arm64 binary).
    public class AppDBTests
    {
        private string dbFile = null!;

        [SetUp]
        public void Setup()
        {
            dbFile = Path.Combine(Path.GetTempPath(), $"xdm-test-{Guid.NewGuid():N}.db");
        }

        [TearDown]
        public void Cleanup()
        {
            if (File.Exists(dbFile)) File.Delete(dbFile);
        }

        [Test]
        public void DownloadRoundTrip()
        {
            Assert.IsTrue(AppDB.Instance.Init(dbFile), "DB init failed");

            var item = new InProgressDownloadItem
            {
                Id = "test-id-1",
                Name = "file.zip",
                DateAdded = DateTime.Now,
                Size = 1024,
                Progress = 42,
                DownloadType = "Http",
                TargetDir = "/tmp",
                PrimaryUrl = "https://example.com/file.zip",
                // Authentication left null on purpose: exercises the DBNull parameter path
            };
            Assert.IsTrue(AppDB.Instance.Downloads.AddNewDownload(item), "insert failed");

            Assert.IsTrue(AppDB.Instance.Downloads.LoadDownloads(out var inProgress, out var finished), "load failed");
            Assert.AreEqual(1, inProgress.Count);
            Assert.AreEqual(0, finished.Count);
            Assert.AreEqual("file.zip", inProgress[0].Name);
            Assert.AreEqual(1024, inProgress[0].Size);

            Assert.IsTrue(AppDB.Instance.Downloads.MarkAsFinished("test-id-1", 2048, "file.zip", "/tmp"));
            var fetched = AppDB.Instance.Downloads.GetDownloadById("test-id-1");
            Assert.IsNotNull(fetched);
            Assert.AreEqual(2048, fetched!.Size);
        }
    }
}
