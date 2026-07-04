using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TraceLog;

namespace XDM.Core.Util
{
    // Fetches yt-dlp/ffmpeg into Config.AppDir on macOS. Both FindYDLBinary and
    // FindFFmpegBinary already look there first, so no resolver changes are needed.
    // Downloads via HttpClient carry no quarantine xattr, so the binaries run as-is.
    public static class ComponentDownloader
    {
#if NET5_0_OR_GREATER
        private const string YtDlpUrl =
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos";
        private static string FFmpegUrl =>
            "https://github.com/eugeneware/ffmpeg-static/releases/latest/download/ffmpeg-darwin-" +
            (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64");

        public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static void DownloadYtDlpInBackground(Action<bool> onComplete) =>
            DownloadInBackground(YtDlpUrl, "yt-dlp", onComplete);

        public static void DownloadFFmpegInBackground(Action<bool> onComplete) =>
            DownloadInBackground(FFmpegUrl, "ffmpeg", onComplete);

        private static void DownloadInBackground(string url, string name, Action<bool> onComplete)
        {
            Task.Run(() =>
            {
                var ok = false;
                try
                {
                    var target = Path.Combine(Config.AppDir, name);
                    var tmp = target + ".part";
                    Log.Debug($"[components] downloading {url} -> {target}");
                    using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                    using (var src = http.GetStreamAsync(url).GetAwaiter().GetResult())
                    using (var dst = File.Create(tmp))
                    {
                        src.CopyTo(dst);
                    }
                    File.SetUnixFileMode(tmp,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                    File.Move(tmp, target);
                    ok = true;
                    Log.Debug("[components] installed " + target);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[components] download failed: " + name);
                }
                onComplete(ok);
            });
        }
#else
        public static bool IsSupported => false;
        public static void DownloadYtDlpInBackground(Action<bool> onComplete) => onComplete(false);
        public static void DownloadFFmpegInBackground(Action<bool> onComplete) => onComplete(false);
#endif
    }
}
