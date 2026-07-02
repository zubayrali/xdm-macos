# XDM Codebase Guide

Orientation doc for anyone (human or agent) picking up this project. Written 2026-07-01
after reviving the repo for macOS development.

## What this is

Xtreme Download Manager (XDM) 8.x — a download accelerator + video downloader.
**It is a C#/.NET codebase, not Java.** The README's "maven" build instructions are
leftovers from the old XDM 7.x Java version and do not apply to anything in this repo.

## Repository layout

```
app/
  XDM/                      ← all the .NET code (solution: XDM_CoreFx.sln)
  packaging/                ← Linux (deb/rpm/arch) + macOS packaging scripts
  xdm-browser-monitor--depricated/  ← dead code, ignore
docs/                       ← the project website (GitHub Pages), not documentation
translation-generator/      ← tooling for the Lang files
```

## The .NET solution (`app/XDM/`)

### Shared projects (compiled into every UI head, no DLLs of their own)

| Project | What it is |
|---|---|
| `XDM.Core` | ~90% of the application. Download engine, everything below. |
| `XDM.Messaging` | JSON messages exchanged with the browser extension. |
| `XDM.Compatibility` | Polyfills so the code can also compile for ancient .NET Framework TFMs. |

These are MSBuild *shared projects* (`.projitems`/`.shproj`) — they have no build
output; each UI project `<Import>`s them and compiles the sources itself.
Consequence: adding a NuGet dependency used by Core means adding it to **every**
project that imports Core (Gtk.UI, Wpf.UI, Tests).

Inside `XDM.Core/`:
- `Downloader/` — segmented/multi-connection download engine (the heart of XDM)
- `Clients/` — HTTP(S)/FTP protocol clients
- `MediaParser/` — HLS/MPEG-DASH manifest parsing for video grabbing
- `BrowserMonitoring/` — receives sniffed requests from the browser extension
- `DataAccess/` — SQLite persistence of the download list (see "SQLite" below)
- `HttpServer/`, `Ipc/` — local socket server the browser native-host talks to
- `YDLWrapper/` — shells out to yt-dlp/youtube-dl
- `UI/` — UI-agnostic interfaces (`IApplication`, peer objects) the heads implement
- `Util/`, `IO/`, `Collections/`, `TraceLog/` (logging), `Translations/`, `Updater/`
- `Interop.WinHttp/` — Windows-only proxy/auth interop (guarded at runtime)

### Executable heads

| Project | TFM | Platform | Notes |
|---|---|---|---|
| `XDM.Gtk.UI` | net8.0 | **Linux + macOS** | Entry: `Program.cs`. Produces `xdm-app`. UI defined in `glade/*.glade` files, GtkSharp NuGet package. |
| `XDM.Wpf.UI` | net4.7.2 | Windows only | Main Windows UI. Cannot build on mac/linux. |
| `XDM.App.Host` | net4.7.2 (win) / net8.0 (elsewhere) | all | **Browser native-messaging host** (`xdm-app-host`). The browser extension launches this; it forwards messages to the running XDM app. Deployed in a `XDM.App.Host/` dir next to `xdm-app` (see `packaging/packaging.txt`). |
| `XDM.Tests` | net8.0 | all | NUnit unit tests (JSON parsing, DB round-trip). |
| `XDM.SystemTests` + `MockServer` | net5.0 | — | Stale; retarget before using. |
| `XDM.WinForms.IntegrationUI`, `XDM.Msix.*`, `XDM.Win.Installer`, `XDM.Linux.Installer`, `NativeMessagingHost` | various | Windows/installer glue | Not needed for day-to-day development. |

### Browser integration

`chrome-extension/` is the WebExtension source (also used for Firefox via
`firefox-amo/`). It sniffs downloads/video and talks to `xdm-app-host` over native
messaging, which relays to the app over a local socket. The extension is copied into
the app's output (`chrome-extension/` next to the binary) so XDM can install it.

## Building (macOS / Linux)

Do **not** build the whole solution on non-Windows — the WPF/WinForms/Msix projects
target .NET Framework and will fail. Build individual projects:

```bash
# macOS: brew install dotnet@8 gtk+3 adwaita-icon-theme
export DOTNET=/opt/homebrew/opt/dotnet@8/bin/dotnet   # keg-only, not on PATH

cd app/XDM
$DOTNET build XDM.Gtk.UI/XDM.Gtk.UI.csproj -c Release
$DOTNET build XDM.App.Host/XDM.App.Host.csproj -c Release
$DOTNET test  XDM.Tests/XDM.Tests.csproj
```

To run from a build output: GTK is loaded via dlopen at runtime, so on macOS export
`DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib` and `XDG_DATA_DIRS=/opt/homebrew/share`
before starting `xdm-app` (the packaged launcher does this for you).

## Packaging

- **macOS**: `app/packaging/make-macos-app [osx-arm64|osx-x64]` → self-contained
  `xdm.app` + `xdm-<ver>-<rid>.tar.xz`. Ad-hoc signed. Runtime dependency:
  Homebrew gtk+3. Install = copy to `/Applications`.
- **Linux**: `make-deb-pkg`, `make-rpm-pkg`, `make-arch-pkg` — expect published
  binaries in `./binary-source` first (see comments in each script).
- **Windows**: `.github/workflows/xdm-wpf-build.yml` + `XDM.Win.Installer`/Msix.

## Changes made during the 2026-07 revival (and why)

1. **SQLite provider swapped: `System.Data.SQLite` → `Microsoft.Data.Sqlite`**
   (all of `XDM.Core/DataAccess/`). System.Data.SQLite ships **no osx-arm64 native
   library** (checked up to 1.0.119) — this is why upstream's mac build dies on Apple
   Silicon. Microsoft.Data.Sqlite bundles natives for every platform via SQLitePCLRaw.
   API deltas handled: connection string is `Data Source=...` (not `URI=file:...`),
   no `CreateFile` (auto-created), `BackupDatabase` has a different signature,
   commands inside a transaction must have the transaction assigned, and null
   parameter values must be `DBNull.Value` (see `SetParam` in `DownloadList.cs`).
2. **Retargeted net6.0 → net8.0** (Gtk.UI, Tests): net6 is EOL and brew ships dotnet@8.
3. **`XDM.App.Host` TFM is now OS-conditional** (net4.7.2 on Windows, net8.0 elsewhere)
   so it can build/publish on mac/linux.
4. **Removed dead `D:\gtksharp\...` `<Reference>` HintPaths** from Gtk.UI — the
   GtkSharp NuGet package provides those assemblies.
5. **Fixed `XDM.Tests`**: the JSON test read a file from the original author's
   Windows desktop; now uses inline sample JSON with assertions. Added
   `AppDBTests.cs` DB round-trip test (also proves the native SQLite lib loads).
6. **Added `app/packaging/make-macos-app`** (see Packaging above). Publishes with
   `-p:PublishTrimmed=false` because trimming breaks GtkSharp's reflection.
7. **Glade loading fixed for macOS** (`Utils/GladeCompat.cs` + all 25 dialogs).
   GtkSharp's `Builder.AddFromFile(path)` throws `GException: Invalid byte sequence
   in conversion input` on macOS for *every* path — `GLib.Marshaller.StringToFilenamePtr`
   mis-marshals the filename regardless of locale/`G_FILENAME_ENCODING`. This is why
   **every window loaded from a .glade file failed silently** (Settings, New Download,
   Video, Properties, Batch, About, …). Fix: `AddGladeFile` extension reads the file in
   managed code and calls `AddFromString(xml)`, which marshals cleanly. A repro harness
   confirmed the SettingsDialog goes from throwing-before-construction to
   constructed + shown after the change.
8. **Fixed Windows-style `Lang\index.txt` paths** in `Program.cs` and `LanguageDialog.cs`
   → `Path.Combine("Lang","index.txt")`. The `\` was a literal filename char on macOS,
   so language index loading silently no-op'd.
9. **macOS launcher injects Homebrew `PATH`** so ffmpeg/yt-dlp resolve when launched from
   Finder (minimal PATH otherwise). In `make-macos-app`.
10. **`/video` endpoint alias** added to `IpcHttpMessageProcessor` so extension forks that
   POST video to `/video` (not `/media`) work. See BROWSER-INTEGRATION.md.

### The pattern to watch for (for whoever continues this)

The two macOS-breaking bugs above share a root cause: **code written assuming Windows
semantics that fails quietly, not loudly, on Unix.** Backslash path separators and the
`AddFromFile` marshalling bug both produced *silent* failures (empty menus, dead
windows) rather than crashes. When triaging "X doesn't work on Mac," first check:
(a) any `@"...\..."` verbatim path (grep already surfaced the remaining ones — they're
all inside Windows-only `#if`/registry/`ProgramFiles` code, so they're fine); (b) any
`AddFromFile` — should now all be `AddGladeFile`; (c) `PlatformHelper`/`BrowserLauncher`
Windows registry calls, which are guarded but worth confirming at runtime.

## Known gaps / next steps

- `xdm.app` depends on Homebrew GTK at runtime. Bundling GTK into the .app (jhbuild
  or gtk-osx) would make it truly standalone — big job, do only if distribution matters.
- Verified working on macOS (2026-07): glade dialogs open; **core segmented download**
  end-to-end (POST `/download` → range download → assembled file, sha256 matches);
  ffmpeg + yt-dlp discovery and use (HLS mux, `yt-dlp -J` format probe); browser HTTP
  protocol on port 8597. See **[BROWSER-INTEGRATION.md](BROWSER-INTEGRATION.md)** for the
  extension architecture and which of the three repos to use (short answer: the bundled
  `app/XDM/chrome-extension`).
- Still GUI-driven / not auto-tested: video *download* trigger (format selection UI),
  the tray icon, and auto-start (`PlatformHelper.EnableAutoStart` is Windows/Linux only —
  on macOS it no-ops; there is no login LaunchAgent yet).
- **Homebrew PATH:** apps launched from Finder get a minimal `PATH` without
  `/opt/homebrew/bin`, so XDM couldn't find ffmpeg/yt-dlp. The macOS launcher
  (`make-macos-app`) now prepends the Homebrew bin dirs. Runtime deps:
  `brew install ffmpeg yt-dlp`.
- FFmpeg/yt-dlp are external tools XDM looks for at runtime (`brew install ffmpeg yt-dlp`).
- `MpdParser.cs` has real nullability warnings worth a pass.
- Windows CI (`xdm-wpf-build.yml`) should be re-run after the SQLite swap — Wpf.UI
  now references Microsoft.Data.Sqlite 8.0.10 (netstandard2.0 build on net4.7.2).
