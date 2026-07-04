using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TraceLog;

namespace XDM.Core.Util
{
    public static class FileHelper
    {
        public static readonly Regex RxFileWithinQuote = new Regex("\\\"(.*)\\\"");
        // Common suffixes video sites append to the page <title>, e.g. "My Video - YouTube".
        private static readonly Regex RxSiteSuffix = new Regex(
            @"\s*[-|–—•·]\s*(YouTube|Vimeo|Dailymotion|Twitch|Facebook|Wistia|Twitter|X|TikTok|Reddit|Bilibili)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Tab titles usually end with the site's own name ("Video Title - Some Site").
        // Drop that trailing segment, but only when it shares a token with the tab
        // URL's hostname — so "Lecture 5 - Introduction" is left alone.
        // ponytail: single trailing segment only; good enough for tab titles
        public static string? CleanTabTitle(string? title, string? tabUrl)
        {
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(tabUrl)) return title;
            var parts = Regex.Split(title, @"\s+[-|–—•·]\s+");
            if (parts.Length < 2) return title;
            string host;
            try { host = new Uri(tabUrl).Host.ToLowerInvariant(); } catch { return title; }
            var last = parts[parts.Length - 1];
            var siteish = Regex.Split(last.ToLowerInvariant(), "[^a-z0-9]+")
                .Any(t => t.Length >= 4 && host.Contains(t));
            if (!siteish) return title;
            var head = title.Substring(0, title.Length - last.Length);
            head = Regex.Replace(head, @"[\s\-|–—•·]+$", string.Empty).Trim();
            return head.Length >= 3 ? head : title;
        }

        public static string? SanitizeFileName(string fileName)
        {
            if (fileName == null) return fileName;
            // Take the last path segment regardless of separator style.
            var file = fileName.Split('/').Last().Split('\\').Last();
            // Drop a trailing " - SiteName" that streaming sites add to the tab title.
            file = RxSiteSuffix.Replace(file, string.Empty);
            // Replace filesystem-invalid characters with spaces, then collapse runs of
            // whitespace/underscores so titles don't become "My___Video".
            file = string.Join(" ", file.Split(Path.GetInvalidFileNameChars()));
            file = Regex.Replace(file, @"[\s_]+", " ").Trim();
            // Trim leading/trailing dots and spaces that break some filesystems.
            file = file.Trim('.', ' ');
            if (string.IsNullOrEmpty(file)) return "download";
            // Cap length so very long titles don't exceed filesystem limits (leave room for extension).
            if (file.Length > 150) file = file.Substring(0, 150).Trim();
            return file;
        }

        public static string GetDownloadFolderByFileName(string file)
        {
            try
            {
                var ext = Path.GetExtension(file)?.ToUpperInvariant();
                foreach (var category in Config.Instance.Categories)
                {
                    if (ext != null && category.FileExtensions.Contains(ext))
                    {
                        return category.DefaultFolder;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error");
            }
            return Config.Instance.DefaultDownloadFolder;
        }



        public static bool AddFileExtension(string name, string contentType, out string nameWithExt)
        {
            name = SanitizeFileName(name);
            if (name.EndsWith("."))
            {
                name = name.TrimEnd('.');
            }
            if (string.IsNullOrEmpty(contentType))
            {
                nameWithExt = name;
                return false;
            }
            if (contentType == "text/html")
            {
                nameWithExt = name + ".html";
                return true;
            }
            else
            {
                try
                {
                    var ext = MimeTypes.Get(contentType.ToLowerInvariant());
                    if (!string.IsNullOrEmpty(ext))
                    {
                        var prevExt = Path.GetExtension(name);
                        var nameWithoutExt = Path.GetFileNameWithoutExtension(name);
                        if (!("." + ext).Equals(prevExt, StringComparison.InvariantCultureIgnoreCase))
                        {
                            nameWithExt = nameWithoutExt + "." + ext;
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Error in AddFileExtension");
                }

                nameWithExt = name;
                return true;
            }
        }

        public static string GetFileName(Uri uri, string contentType = null)
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrEmpty(name))
            {
                name = uri.Host.Replace('.', '_');
            }
            name = SanitizeFileName(name);
            if (string.IsNullOrEmpty(contentType))
            {
                return name;
            }

            if (contentType == "text/html")
            {
                return Path.ChangeExtension(name, ".html");
            }
            else
            {
                if (!Path.HasExtension(name))
                {
                    var ext = MimeTypes.Get(contentType.ToLowerInvariant());
                    if (!string.IsNullOrEmpty(ext))
                    {
                        name += "." + ext;
                    }
                }
                return name;
            }
        }

        public static string GetUniqueFileName(string file, string folder)
        {
            var path = Path.Combine(folder, file);
            var name = Path.GetFileNameWithoutExtension(file);
            var ext = Path.GetExtension(file);
            var count = 0;
            while (File.Exists(path))
            {
                count++;
                path = Path.Combine(folder, name + "_" + count + ext);
            }
            return count == 0 ? file : name + "_" + count + ext;
        }

        public static string GetFileNameFromQuote(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            var matcher = RxFileWithinQuote.Match(text);
            if (matcher.Success)
            {
                return matcher.Groups[1].Value;
            }
            return null;
        }

        public static string QuoteFilePathIfNeeded(string file)
        {
            if (file.Contains(" "))
            {
                return Environment.OSVersion.Platform == PlatformID.Win32NT ? $"\"{file}\"" : $"\"{file}\"";
            }
            return file;
        }
    }
}
