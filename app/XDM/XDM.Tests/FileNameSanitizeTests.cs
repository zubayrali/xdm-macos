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

        [Test]
        public void CleanTabTitleDropsSiteNameMatchingHost()
        {
            Assert.AreEqual("ibn ata allah hikam", FileHelper.CleanTabTitle(
                "ibn ata allah hikam - Madina Institute islamic uni courses",
                "https://madinainstitute.com/tv/hikm-of-ibn-ataillah-part4"));
            // multi-dash titles: only the trailing site segment goes, "Part4" stays
            Assert.AreEqual("Hikm of Ibn Ata'illah – Part4", FileHelper.CleanTabTitle(
                "Hikm of Ibn Ata'illah – Part4 - Madina Institute",
                "https://madinainstitute.com/tv/x"));
        }

        [Test]
        public void CleanTabTitleKeepsUnrelatedSegments()
        {
            // last segment is real content, not the site name
            Assert.AreEqual("Lecture 5 - Introduction", FileHelper.CleanTabTitle(
                "Lecture 5 - Introduction", "https://example.com/course"));
            // no separators / no tab url — untouched
            Assert.AreEqual("Plain Title", FileHelper.CleanTabTitle("Plain Title", "https://example.com"));
            Assert.AreEqual("A - B", FileHelper.CleanTabTitle("A - B", null));
        }
    }
}
