using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using TraceLog;
using Translations;
using XDM.Core;
using XDM.Core.Downloader;
using XDM.Core.Downloader.Adaptive.Dash;
using XDM.Core.Downloader.Adaptive.Hls;
using XDM.Core.Downloader.Progressive.DualHttp;
using XDM.Core.Downloader.Progressive.SingleHttp;
using XDM.Core.UI;
using XDM.Core.Util;
using YDLWrapper;

namespace XDM.Core.UI
{
    public class VideoDownloaderUIController
    {
        private YDLProcess? ydl;
        private List<YDLVideoEntry> videoItemList;
        private List<int> videoQualities;
        private IVideoDownloadView view;

        public VideoDownloaderUIController(IVideoDownloadView view)
        {
            this.view = view;

            var browsers = new Dictionary<string, string>
            {
                ["Google Chrome"] = "chrome",
                ["Microsoft Edge"] = "edge",
                ["Mozilla Firefox"] = "firefox",
                ["Brave"] = "brave",
                ["Opera"] = "opera",
                ["Chromium"] = "chromium",
                ["Safari"] = "safari",
                ["Vivaldi"] = "vivaldi"
            };

            this.view.AllowedBrowsers = browsers.Keys.ToList();

            view.SearchClicked += (_, _) => StartSearch();

            view.CancelClicked += (_, _) =>
            {
                CancelOperation();
                view.SwitchToInitialPage();
            };

            view.WindowClosed += (_, _) =>
            {
                CancelOperation();
            };

            view.BrowseClicked += (_, _) =>
            {
                var folder = view.SelectFolder();
                if (!string.IsNullOrEmpty(folder))
                {
                    view.DownloadLocation = folder;
                    Config.Instance.UserSelectedDownloadFolder = folder;
                    Helpers.UpdateRecentFolderList(folder);
                }
            };

            view.DownloadClicked += View_DownloadClicked;
            view.DownloadLaterClicked += View_DownloadLaterClicked;
            view.QueueSchedulerClicked += (s, e) =>
            {
                ApplicationContext.Application.ShowQueueWindow(s);
            };
        }

        private void View_DownloadLaterClicked(object? sender, DownloadLaterEventArgs e)
        {
            DownloadSelectedItems(false, e.QueueId);
        }

        private void View_DownloadClicked(object? sender, EventArgs e)
        {
            DownloadSelectedItems(true, null);
        }

        // Extracted so it can be triggered both by the Search button and by an
        // auto-search (e.g. an embedded-video URL pushed in from the browser extension).
        private void StartSearch()
        {
            var browsers = new Dictionary<string, string>
            {
                ["Google Chrome"] = "chrome",
                ["Microsoft Edge"] = "edge",
                ["Mozilla Firefox"] = "firefox",
                ["Brave"] = "brave",
                ["Opera"] = "opera",
                ["Chromium"] = "chromium",
                ["Safari"] = "safari",
                ["Vivaldi"] = "vivaldi"
            };
            var url = view.Url;
            string? browser = null;
            if (!string.IsNullOrEmpty(view.SelectedBrowser))
            {
                browsers.TryGetValue(view.SelectedBrowser!, out browser);
            }
            Log.Debug("[ydl] StartSearch url=" + url + " valid=" + Helpers.IsUriValid(url));
            if (Helpers.IsUriValid(url))
            {
                view.SwitchToProcessingPage();
                ProcessVideo(url, browser, result => ApplicationContext.Application.RunOnUiThread(() =>
                {
                    Log.Debug("[ydl] yt-dlp result count=" + (result?.Count.ToString() ?? "null"));
                    if (result != null && result.Count > 0)
                    {
                        view.SwitchToFinalPage();
                        SetVideoResultList(result);
                        // Also surface the resolved video(s) in the browser extension popup
                        // (the desktop window above still opens). Both surfaces, one resolve.
                        PublishToBrowserPopup(result);
                    }
                    else
                    {
                        view.SwitchToErrorPage();
                    }
                }));
            }
            else
            {
                ApplicationContext.Application.ShowMessageBox(view, TextResource.GetText("MSG_INVALID_URL"));
            }
        }

        public void Run()
        {
            Run(null, false);
        }

        // initialUrl/autoSearch: used by the extension's embedded-video (LMS) detection —
        // pre-fills the URL and kicks off yt-dlp resolution without a manual click.
        public void Run(string? initialUrl, bool autoSearch)
        {
            var url = initialUrl;
            if (string.IsNullOrEmpty(url))
            {
                url = ApplicationContext.Application.GetUrlFromClipboard();
            }
            if (url != null && Helpers.IsUriValid(url))
            {
                view.Url = url;
            }
            view.DownloadLocation = Helpers.GetVideoDownloadFolder();
            view.ShowWindow();
            if (autoSearch && !string.IsNullOrEmpty(view.Url) && Helpers.IsUriValid(view.Url))
            {
                StartSearch();
            }
        }

        private void SetVideoResultList(List<YDLVideoEntry> items)
        {
            if (items == null) return;

            this.videoItemList = items;

            var formatSet = new HashSet<int>();
            foreach (var item in items)
            {
                if (item.Formats != null)
                {
                    item.Formats.ForEach(item =>
                    {
                        if (!string.IsNullOrEmpty(item.Height))
                        {
                            if (Int32.TryParse(item.Height, out int height))
                            {
                                formatSet.Add(height);
                            }
                        }
                    });
                }
            }
            var formatsList = new List<int>(formatSet);
            formatsList.Sort();
            formatsList.Reverse();
            // 0 = audio-only sentinel (real heights are always positive); offered
            // whenever any item has an audio-only format
            if (items.Any(x => x.Formats != null && x.Formats.Any(IsAudioOnlyFormat)))
            {
                formatsList.Add(0);
            }
            this.videoQualities = formatsList;

            var videoList = this.videoItemList.Select(x => x.Title);
            var formatList = this.videoQualities.Select(n => n == 0 ? "Audio only" : $"{n}p");

            view.SetVideoResultList(videoList, formatList);

            if (formatsList.Count > 0)
            {
                view.SelectedFormat = 0;
            }
        }

        private void CancelOperation()
        {
            try
            {
                if (ydl != null)
                {
                    ydl.Cancel();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error cancelling ydl");
            }
        }

        private void ProcessVideo(string url, string? browser, Action<List<YDLVideoEntry>?> callback)
        {
            ydl = new YDLProcess
            {
                Uri = new Uri(url),
                BrowserName = browser
            };
            new Thread(() =>
            {
                try
                {
                    ydl.Start();
                    callback.Invoke(YDLOutputParser.Parse(ydl.JsonOutputFile));
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Error while running youtube-dl");
                    callback.Invoke(null);
                }
            }).Start();
        }

        private void DownloadSelectedItems(bool startImmediately, string? queueId)
        {
            if (string.IsNullOrEmpty(view.DownloadLocation))
            {
                ApplicationContext.Application!.ShowMessageBox(view, TextResource.GetText("MSG_CAT_FOLDER_MISSING"));
                return;
            }
            if (this.view.SelectedItemCount == 0)
            {
                ApplicationContext.Application!.ShowMessageBox(view, TextResource.GetText("BAT_SELECT_ITEMS"));
                return;
            }
            var quality = -1;
            if (view.SelectedFormat >= 0)
            {
                quality = this.videoQualities[view.SelectedFormat];
            }

            var selectedIndices = view.SelectedRows;
            foreach (var index in selectedIndices)
            {
                var entry = videoItemList[index];
                var fmt = FindMatchingFormatByQuality(entry, quality);
                if (fmt.HasValue)
                {
                    AddDownload(fmt.Value, startImmediately, queueId);
                }
            }
            view.CloseWindow();
        }

        private static bool IsAudioOnlyFormat(YDLVideoFormatEntry format)
        {
            return format.VideoUrl == null && format.VideoFragments == null &&
                (format.AudioUrl != null || format.AudioFragments != null);
        }

        private YDLVideoFormatEntry? FindMatchingFormatByQuality(YDLVideoEntry videoEntry, int quality = -1)
        {
            if (videoEntry.Formats.Count == 0) return null;
            if (quality == -1)
            {
                return videoEntry.Formats[0];
            }
            if (quality == 0) // "Audio only" selected
            {
                foreach (var format in videoEntry.Formats)
                {
                    if (IsAudioOnlyFormat(format))
                    {
                        return format;
                    }
                }
                return videoEntry.Formats[0];
            }
            //if we find an mp4 video with desired height/resolution return it
            var fmt = FindOnlyMatchingMp4(videoEntry, quality);
            if (fmt != null)
            {
                return fmt;
            }
            //if no mp4 is found look for other formats like mkv or webm
            foreach (var format in videoEntry.Formats)
            {
                if (!string.IsNullOrEmpty(format.Height) &&
                    Int32.TryParse(format.Height, out int height) &&
                    height == quality)
                {
                    return format;
                }
            }
            //so far no luck, try to find next best resoultion
            var max = -1;
            foreach (var format in videoEntry.Formats)
            {
                if (!string.IsNullOrEmpty(format.Height) &&
                    Int32.TryParse(format.Height, out int height) &&
                    height > 0 &&
                    quality > height)
                {
                    if (height > max)
                    {
                        max = height;
                        fmt = format;
                    }
                }
            }
            if (fmt != null)
            {
                return fmt;
            }
            //could not found anything as per criteria, return the first format
            return videoEntry.Formats[0];
        }

        private YDLVideoFormatEntry? FindOnlyMatchingMp4(YDLVideoEntry videoEntry, int quality)
        {
            if (videoEntry.Formats.Count == 0) return null;
            foreach (var format in videoEntry.Formats)
            {
                if (!string.IsNullOrEmpty(format.Height) &&
                    Int32.TryParse(format.Height, out int height) &&
                    height == quality &&
                    (format.FileExt?.ToLowerInvariant()?.EndsWith("mp4") ?? false))
                {
                    return format;
                }
            }
            return null;
        }

        private void AddDownload(YDLVideoFormatEntry videoEntry, bool startImmediately, string? queueId)
        {
            IRequestData? info = videoEntry.YDLEntryType switch
            {
                YDLEntryType.Http => new SingleSourceHTTPDownloadInfo
                {
                    Uri = videoEntry.VideoUrl
                },
                YDLEntryType.Dash => new DualSourceHTTPDownloadInfo
                {
                    Uri1 = videoEntry.VideoUrl,
                    Uri2 = videoEntry.AudioUrl
                },
                YDLEntryType.Hls => new MultiSourceHLSDownloadInfo
                {
                    VideoUri = videoEntry.VideoUrl,
                    AudioUri = videoEntry.AudioUrl
                },
                YDLEntryType.MpegDash => new MultiSourceDASHDownloadInfo
                {
                    VideoSegments = videoEntry.VideoFragments?.Select(x => new Uri(new Uri(videoEntry.FragmentBaseUrl), x.Path)).ToList(),
                    AudioSegments = videoEntry.AudioFragments?.Select(x => new Uri(new Uri(videoEntry.FragmentBaseUrl), x.Path)).ToList(),
                    AudioFormat = videoEntry.AudioFormat != null ? "." + videoEntry.AudioFormat : null,
                    VideoFormat = videoEntry.VideoFormat != null ? "." + videoEntry.VideoFormat : null,
                    Url = videoEntry.VideoUrl
                },
            };
            if (info != null)
            {
                ApplicationContext.CoreService!.StartDownload(
                        info,
                        videoEntry.Title + "." + videoEntry.FileExt,
                        FileNameFetchMode.None,
                        view.DownloadLocation,
                        startImmediately,
                        view.Authentication,
                        view.Proxy ?? Config.Instance.Proxy,
                        queueId,
                        false
                    );
            }
        }

        // Feed the yt-dlp-resolved videos into the CapturedVideoTracker so they appear as
        // rows in the browser extension popup too (BroadcastConfigChange -> /sync push).
        // We publish one row per video at its best resolution; clicking it in the popup
        // routes through the normal AddVideoDownload path (dialog or auto-start).
        private void PublishToBrowserPopup(List<YDLVideoEntry> results)
        {
            foreach (var entry in results)
            {
                if (entry.Formats == null || entry.Formats.Count == 0) continue;
                var fmt = BestFormat(entry);
                var file = (string.IsNullOrEmpty(entry.Title) ? "video" : entry.Title)
                    + (string.IsNullOrEmpty(fmt.FileExt) ? "" : "." + fmt.FileExt);
                var display = new StreamingVideoDisplayInfo
                {
                    Quality = string.IsNullOrEmpty(fmt.Height) ? "" : fmt.Height + "p",
                    CreationTime = DateTime.Now
                };
                switch (fmt.YDLEntryType)
                {
                    case YDLEntryType.Http:
                        ApplicationContext.VideoTracker.AddVideoNotification(display,
                            new SingleSourceHTTPDownloadInfo { File = file, Uri = fmt.VideoUrl });
                        break;
                    case YDLEntryType.Dash:
                        ApplicationContext.VideoTracker.AddVideoNotification(display,
                            new DualSourceHTTPDownloadInfo { File = file, Uri1 = fmt.VideoUrl, Uri2 = fmt.AudioUrl });
                        break;
                    case YDLEntryType.Hls:
                        ApplicationContext.VideoTracker.AddVideoNotification(display,
                            new MultiSourceHLSDownloadInfo { File = file, VideoUri = fmt.VideoUrl, AudioUri = fmt.AudioUrl });
                        break;
                    case YDLEntryType.MpegDash:
                        ApplicationContext.VideoTracker.AddVideoNotification(display,
                            new MultiSourceDASHDownloadInfo
                            {
                                File = file,
                                Url = fmt.VideoUrl,
                                VideoSegments = fmt.VideoFragments?.Select(x => new Uri(new Uri(fmt.FragmentBaseUrl), x.Path)).ToList(),
                                AudioSegments = fmt.AudioFragments?.Select(x => new Uri(new Uri(fmt.FragmentBaseUrl), x.Path)).ToList(),
                                AudioFormat = fmt.AudioFormat != null ? "." + fmt.AudioFormat : null,
                                VideoFormat = fmt.VideoFormat != null ? "." + fmt.VideoFormat : null,
                            });
                        break;
                }
            }
        }

        // Highest-resolution format, matching the desktop window's default (SelectedFormat=0).
        private static YDLVideoFormatEntry BestFormat(YDLVideoEntry entry)
        {
            var best = entry.Formats[0];
            var bestH = -1;
            foreach (var f in entry.Formats)
            {
                if (Int32.TryParse(f.Height, out int h) && h > bestH) { bestH = h; best = f; }
            }
            return best;
        }
    }
}
