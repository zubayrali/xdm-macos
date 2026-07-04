using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace YDLWrapper
{
    // yt-dlp emits numeric fields (width, height, filesize, abr, …) as JSON numbers,
    // but the model types them as string. Newtonsoft throws on number->string, which
    // silently failed the whole parse (0 formats). Coerce any scalar token to string.
    internal class TolerantStringConverter : JsonConverter
    {
        public override bool CanConvert(System.Type objectType) => objectType == typeof(string);

        public override object? ReadJson(JsonReader reader, System.Type objectType,
            object? existingValue, JsonSerializer serializer)
        {
            if (reader.Value == null) return null;
            return System.Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            => writer.WriteValue((string?)value);
    }

    public static class YDLOutputParser
    {
        public static List<YDLVideoEntry> Parse(string ydlJsonOutputFile)
        {
            var res = new List<YDLVideoEntry>();

            try
            {
                var pl = Deserialize<YDLPlaylist>(ydlJsonOutputFile);

                if (pl.Entries != null && pl.Entries.Length > 0)
                {
                    foreach (var formatList in pl.Entries)
                    {
                        var items = ProcessFormatList(formatList);
                        res.Add(new YDLVideoEntry
                        {
                            Title = formatList.Title,
                            Formats = items
                        });
                    }
                    return res;
                }
            }
            catch (System.Exception ex) { TraceLog.Log.Debug("[YDLParse] " + ex.Message); }

            try
            {
                var pl2 = Deserialize<YDLFormatList>(ydlJsonOutputFile);

                if (pl2.Formats != null && pl2.Formats.Length > 0)
                {
                    res.Add(new YDLVideoEntry
                    {
                        Title = pl2.Title,
                        Formats = ProcessFormatList(pl2)
                    });
                }
            }
            catch (System.Exception ex) { TraceLog.Log.Debug("[YDLParse] " + ex.Message); }

            try
            {
                var format = Deserialize<YDLFormat>(ydlJsonOutputFile);
                if (format.Url != null)
                {
                    var formatList = new YDLFormatList { Title = format.Title, Formats = new YDLFormat[] { format } };
                    res.Add(new YDLVideoEntry
                    {
                        Title = format.Title,
                        Formats = ProcessFormatList(formatList)
                    });
                    //res.Add(new YDLVideoEntry
                    //{
                    //    Title = format.Title,
                    //    Formats = new List<YDLVideoFormatEntry>
                    //    {
                    //        new YDLVideoFormatEntry
                    //        {
                    //            VideoUrl = format.Url,
                    //            Title = format.Title,
                    //            YDLEntryType = YDLEntryType.Http,
                    //            VideoFormat = format.Format
                    //        }
                    //    }
                    //});
                }
            }
            catch (System.Exception ex) { TraceLog.Log.Debug("[YDLParse] " + ex.Message); }

            return res;
        }

        private static List<YDLVideoFormatEntry> ProcessFormatList(YDLFormatList formatList)
        {
            var list = new List<YDLVideoFormatEntry>();
            var videoOnlyList = new List<YDLFormat>();
            var audioOnlyList = new List<YDLFormat>();

            foreach (var format in formatList.Formats)
            {
                // yt-dlp codec semantics: "none" = stream definitely absent,
                // null/empty = unknown. Vimeo HLS audio comes as vcodec="none" +
                // acodec=null; folding "none" and null together used to misfile it
                // as a combined A/V entry (and leave video-only streams unpaired).
                var audioAbsent = IsNone(format.Acodec);
                var videoAbsent = IsNone(format.Vcodec);
                if (videoAbsent && audioAbsent)
                {
                    continue; // no playable stream (storyboards etc.)
                }
                if (videoAbsent)
                {
                    audioOnlyList.Add(format);
                    continue;
                }
                if (audioAbsent)
                {
                    videoOnlyList.Add(format);
                    continue;
                }
                var acodec = GetStringValue(format.Acodec);
                var vcodec = GetStringValue(format.Vcodec);
                if ((vcodec == null && acodec == null) ||
                    (vcodec != null && acodec != null))
                {
                    list.Add(new YDLVideoFormatEntry
                    {
                        VideoUrl = format.Url,
                        VideoFragments = format.Fragments,
                        Title = formatList.Title,
                        YDLEntryType = GetEntryType(format),
                        VideoFormat = format.Format,
                        FileExt = format.Ext,
                        VideoCodec = format.Vcodec,
                        AudioCodec = format.Acodec,
                        Abr = format.Abr,
                        Width = format.Width,
                        Height = format.Height,
                        FragmentBaseUrl = format.Fragment_Base_Url
                    });
                }
                else if (vcodec != null)
                {
                    videoOnlyList.Add(format);
                }
                else if (acodec != null)
                {
                    audioOnlyList.Add(format);
                }
            }

            foreach (var video in videoOnlyList)
            {
                var videotype = GetEntryType(video);
                foreach (var audio in audioOnlyList)
                {
                    var audioType = GetEntryType(audio);
                    if (videotype == audioType)
                    {
                        list.Add(new YDLVideoFormatEntry
                        {
                            AudioFormat = audio.Format,
                            VideoFormat = video.Format,
                            AudioFragments = audio.Fragments,
                            VideoFragments = video.Fragments,
                            AudioUrl = audio.Url,
                            VideoUrl = video.Url,
                            Title = formatList.Title,
                            YDLEntryType = videotype,
                            FileExt = "MKV",
                            VideoCodec = video.Vcodec,
                            AudioCodec = audio.Acodec,
                            Abr = audio.Abr,
                            Width = video.Width,
                            Height = video.Height,
                            FragmentBaseUrl = video.Fragment_Base_Url
                        });
                    }
                }
            }

            // Always offer audio-only entries too (after the video ones, so a video
            // stays the default pick) — lecture/podcast users often want just audio.
            {
                foreach (var audio in audioOnlyList)
                {
                    var audioType = GetEntryType(audio);
                    list.Add(new YDLVideoFormatEntry
                    {
                        AudioFormat = audio.Format,
                        AudioFragments = audio.Fragments,
                        AudioUrl = audio.Url,
                        Title = formatList.Title,
                        YDLEntryType = audioType,
                        FileExt = audio.Ext,
                        AudioCodec = audio.Acodec,
                        Abr = audio.Abr,
                        FragmentBaseUrl = audio.Fragment_Base_Url
                    });
                }
            }

            return list;
        }

        private static YDLEntryType GetEntryType(YDLFormat format)
        {
            if (HasFragments(format)) return YDLEntryType.MpegDash;
            var protocol = format.Protocol?.ToLowerInvariant() ?? string.Empty;
            if (protocol.Contains("dash")) return YDLEntryType.Dash;
            if (protocol.Contains("m3u")) return YDLEntryType.Hls;
            var container = format.Container?.ToLowerInvariant() ?? string.Empty;
            if (container.Contains("dash")) return YDLEntryType.Dash;
            if (container.Contains("m3u")) return YDLEntryType.Hls;
            return YDLEntryType.Http;
        }

        private static bool HasFragments(YDLFormat format)
        {
            return format.Fragments != null && format.Fragments.Count > 0;
        }

        // explicit "none" from yt-dlp — the stream is definitely absent (unlike null = unknown)
        private static bool IsNone(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return "none".Equals(text) || "'none'".Equals(text) || "\"none\"".Equals(text);
        }

        private static string? GetStringValue(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if ("none".Equals(text)) return null;
            if ("'none'".Equals(text)) return null;
            if ("\"none\"".Equals(text)) return null;
            return text;
        }

        private static T? Deserialize<T>(string file)
        {
            return JsonConvert.DeserializeObject<T>(
                File.ReadAllText(file),
                new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    Converters = { new TolerantStringConverter() }
                });
        }
    }
}
