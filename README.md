# XDM for macOS (Apple Silicon)

A revived, macOS-native fork of [Xtreme Download Manager](https://github.com/subhra74/xdm) —
the C#/.NET download manager — brought back to life for Apple Silicon Macs.

Upstream's last macOS build never ran on modern Macs. This fork does:

- **Self-contained `.app`** — GTK runtime is bundled; no Homebrew required to run it.
- **Behaves like a Mac app** — real top menu bar with ⌘-shortcuts, dock-click restores
  the window, retina icons, optional start-at-login.
- **Browser integration** — a Chrome/Chromium extension hands downloads and streaming
  media to XDM.
- **Video downloads** — sniffs media from pages and detects embedded YouTube/Vimeo
  players (including LMS/plyr embeds), resolved through yt-dlp with a format picker;
  video+audio streams are merged with ffmpeg into `.mp4` (or `.mkv` when the codecs
  need it).
- **Classic XDM engine** — segmented multi-connection downloads, resume, queues,
  scheduler.

## Install

1. Download `xdm-macos-arm64.zip` from [Releases](https://github.com/zubayrali/xdm-macos/releases),
   unzip, and drag `xdm.app` into `/Applications`.
2. First launch: **right-click → Open → Open**. The app is ad-hoc signed (no Apple
   Developer certificate), so plain double-click is blocked by Gatekeeper the first time.
   Alternatively: `xattr -dr com.apple.quarantine /Applications/xdm.app`
3. Optional: for video downloads XDM needs `yt-dlp` and `ffmpeg`. When they're missing,
   XDM offers to download them automatically (into `~/.xdm-app-data`). If you prefer
   Homebrew: `brew install yt-dlp ffmpeg` works too.

## Browser extension

1. Download `xdm-chrome-extension.zip` from the release, unzip it somewhere permanent.
2. Open `chrome://extensions` (or the equivalent in Edge/Brave/Vivaldi), enable
   **Developer mode**, click **Load unpacked**, and pick the unzipped folder.
3. Make sure XDM is running and **Browser monitoring** is on in the extension popup.

Downloads and detected videos then show up in XDM and in the extension popup.
Firefox is not supported yet (the bundled Firefox extension predates this fork).

## Building from source

```sh
brew install dotnet@8 gtk+3 adwaita-icon-theme gtk-mac-integration dylibbundler
/opt/homebrew/opt/dotnet@8/bin/dotnet build app/XDM/XDM.Gtk.UI/XDM.Gtk.UI.csproj -c Release
./app/packaging/make-macos-app
cp -R app/packaging/xdm.app /Applications/   # cp -R, not -r (symlinks!)
```

Only `XDM.Gtk.UI`, `XDM.App.Host`, and `XDM.Tests` build on macOS — the
WPF/WinForms projects are Windows-only. See `docs/CODEBASE.md` for the codebase
guide and `docs/macos-native-polish.md` for how the macOS packaging works.

## Scope & credits

This fork targets **macOS on Apple Silicon** (Intel may work via the same build, untested).
Windows and Linux users should use [upstream XDM](https://github.com/subhra74/xdm).

All credit for XDM itself goes to [@subhra74](https://github.com/subhra74).
Licensed under [GPL-2.0](LICENSE), same as upstream.

Downloading videos may be restricted by the terms of service of the site hosting
them and by copyright law — download only content you have the right to save.
DRM-protected content is out of scope and unsupported.
