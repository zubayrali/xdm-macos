using NUnit.Framework;
using XDM.Core.Util;

namespace XDM.Tests
{
    public class FileNameSanitizeTests
    {
        [Test]
        public void StripsSiteSuffix()
        {
            Assert.AreEqual("My Cool Video", FileHelper.SanitizeFileName("My Cool Video - YouTube"));
            Assert.AreEqual("Clip", FileHelper.SanitizeFileName("Clip | Vimeo"));
        }

        [Test]
        public void CollapsesInvalidCharsAndWhitespace()
        {
            // slashes are treated as path separators — last segment wins (URL-derived names)
            Assert.AreEqual("c", FileHelper.SanitizeFileName("a/b/c"));
            // invalid-char runs (e.g. ':') collapse; underscores collapse to a single space
            Assert.AreEqual("My Video", FileHelper.SanitizeFileName("My___Video"));
        }

        [Test]
        public void TrimsAndCaps()
        {
            Assert.AreEqual("hello", FileHelper.SanitizeFileName("  ...hello...  "));
            Assert.AreEqual(150, FileHelper.SanitizeFileName(new string('x', 400))!.Length);
        }

        [Test]
        public void EmptyBecomesDownload()
        {
            Assert.AreEqual("download", FileHelper.SanitizeFileName("///"));
        }
    }
}
