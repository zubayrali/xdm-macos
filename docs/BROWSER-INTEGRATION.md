# Browser Integration — how it works and which extension to use

Verified on macOS during the 2026-07 revival. TL;DR: **use the bundled extension**
(`app/XDM/chrome-extension`). It needs no native-messaging host — it talks to XDM
directly over HTTP — and its protocol matches this app build exactly.

## Features ported from the fork (2026-07)

The bundled extension gained these, borrowed from the `Xtreme-Download-Manager` fork
and adapted to the bundled extension's 8597 protocol:

- **"Download all links"** context menu — injects `contentscript.js` to collect every
  `<a href>` plus `<video>/<audio>/<source>` src on the page, then POSTs them to XDM's
  `/link` batch endpoint, which opens a single Download Selection window (checkbox list)
  rather than one dialog per link. Needs the `scripting` permission (added to manifest).
- **Keyboard toggle** `Cmd/Ctrl+Shift+E` to enable/disable monitoring (`commands` key).
- **Persisted disable state** — the on/off choice is stored in `chrome.storage.local`
  so it survives service-worker restarts (previously in-memory only).
- **Robust context-menu creation** — `contextMenus.removeAll()` before re-creating, so
  waking the service worker no longer throws "duplicate id" errors.

Not ported (deliberately): the fork's configurable `xdmHost` (bundled hardcodes the
matching 8597) and its separate `/video` protocol (bundled already uses `/media`).

## Embedded-video (LMS) detection → yt-dlp  (2026-07)

LMS lessons (Tutor LMS, Moodle, etc.) usually embed a **YouTube/Vimeo** player rather
than serving the raw file, so XDM's network-capture path sees nothing to grab. The
extension now detects those embeds and routes them to XDM's existing yt-dlp Video
Downloader.

- `embed-detect.js` scans each page for **YouTube/Vimeo `<iframe>` embeds**, **plyr
  players** (`data-plyr-embed-id` — Tutor LMS uses these), and `og:video`/`twitter:player`
  meta, and normalizes each to a canonical watch URL (`youtube.com/watch?v=…`,
  `vimeo.com/…`).
- **Two triggers** (both wired): it runs automatically as a content script on every page
  (auto-detect), and there's a **"Grab embedded video (XDM)"** context-menu action to
  force a scan.
- Detected URLs are POSTed to the new **`/ydl`** endpoint
  (`IpcHttpMessageProcessor.OnEmbedMessage`), which opens the **Video Downloader**
  (`VideoDownloaderUIController.Run(url, autoSearch:true)`) and auto-runs yt-dlp so the
  format list appears without a manual click. Pick a quality → download.
- Dedupe: the service worker tracks already-sent embed URLs per session so the window
  doesn't reopen on every SPA navigation.

This is **legitimate embed extraction only** (public YouTube/Vimeo via yt-dlp). It does
**not** touch DRM (Widevine/FairPlay/vDocipher) — no CDM/key handling exists or is planned.

## How the integration actually works

There is **no native messaging** in the current extension. The browser extension is a
plain MV3 service worker that does `fetch()` calls to a small HTTP server XDM runs on
`http://127.0.0.1:8597` (`XDM.Core/BrowserMonitoring/IpcHttpMessageProcessor.cs`,
started at app boot via `BrowserMonitor.Run()`).

```
Browser extension (service worker)                XDM desktop app
  ── GET  /sync   every ~60s (chrome.alarms) ──►   returns config JSON
                                                    {enabled, fileExts, videoList, …}
  ── POST /download  {url,file,headers,cookie} ─►   AddDownload() → download engine
  ── POST /media     (video captures) ──────────►   VideoTracker (video list)
  ── POST /link      (download-all-links) ──────►   AddBatchLinks()
```

Consequences:
- **XDM must be running** for the extension to connect (it polls `/sync`; on failure it
  shows "disconnected"). There is no auto-launch on macOS — the old `xdm-app-host`
  native host (Windows `Mutex`-based) is not wired for the bundled extension, whose
  `launchApp()` is empty. Keep XDM open, or add a login LaunchAgent (future work).
- No manifest registration, no `NativeMessagingHosts` file needed. Just load the
  unpacked extension and keep XDM running.

The download path was verified end-to-end on macOS: POST `/download` → segmented
download (HTTP range) → assembled file → **sha256 matches source**.

## The three extension repos compared

| Repo | Name / ver | Talks to | Verdict |
|---|---|---|---|
| `app/XDM/chrome-extension` (bundled) | XDM Integration Module **v3.3** | `8597` + `/download`,`/media`,`/sync`,`/link` | **Use this.** Protocol-matched to this app, newest, valid MV3, no native messaging. |
| `xdm-helper-chrome/chrome/chrome-extension` (official upstream) | XDM Integration Module v3.1 | same `8597` protocol | Same lineage as bundled, older. The bundled copy supersedes it. |
| `Xtreme-Download-Manager` (fork) | Xtreme Download Manager **v3.1** | **`9614`** + `/download`,**`/video`**,`/sync` | Cleaner code + nice extras, but targets the *legacy* XDM protocol (port 9614, `/video`). Won't work as-is. |

The fork's genuine improvements: a **configurable host** (`xdmHost` in
`chrome.storage.local`), a **"download all links"** content script, persisted
monitoring-disabled state, and a GitHub release pipeline that builds a signed `.crx`.
But its default port (9614) and `/video` path match the app's *commented-out* legacy
handler, not the live one.

To make the fork work with this app you'd need both:
1. Set the fork's `xdmHost` to `http://127.0.0.1:8597` (via its popup/storage), and
2. The app to accept `/video` — **already done**: a `/video`→`/media` alias was added to
   `IpcHttpMessageProcessor.HandleRequest`. `/download` and `/link` already match.

Recommendation: ship the **bundled** extension now (it's proven). If you want the fork's
"download all links" + configurable host later, port those two features onto the bundled
extension rather than switching protocols.

## How to load the bundled extension (Chrome / Brave / Edge / Vivaldi)

1. Start XDM (`open /Applications/xdm.app`) and leave it running.
2. Browser → `chrome://extensions` → enable **Developer mode**.
3. **Load unpacked** → select `app/XDM/chrome-extension`.
4. Within ~60s the extension icon should show connected (it polls `/sync`). Click a
   download link — XDM takes over. (Extension ID will differ from the hard-coded one in
   `xdm_chrome.native_host.json`; that only matters for native messaging, which is unused.)

Firefox: use `app/XDM/firefox-amo` (same protocol) via `about:debugging` → Load Temporary
Add-on.

## Known gaps (browser side)

- **No auto-launch on macOS.** Add a `~/Library/LaunchAgents` plist or run XDM at login.
- **Handler re-throws on malformed messages.** `HandleRequest` catches, logs, then
  re-`throw`s, which resets the HTTP connection (the extension then briefly reports
  "disconnected"). A non-video URL POSTed to `/media`/`/video` triggers this. Low
  priority, but making `HandleRequest` swallow-and-200 would make the extension link more
  robust.
- Video **capture** (`/media`) registers a video in XDM's list; the actual video download
  is still driven from XDM's GUI (format selection), not auto-started.
