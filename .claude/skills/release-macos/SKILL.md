---
name: release-macos
description: Build, package, and publish a macOS release of XDM to the zubayrali/xdm-macos GitHub repo. Takes the version tag as argument (e.g. v1.0.1).
---

# Release XDM for macOS

Publish a GitHub release of the macOS fork. Argument: the version tag
(e.g. `v1.0.1`). If missing, look at `gh release list --repo zubayrali/xdm-macos`
and propose the next patch version to the user before proceeding.

## Steps

1. **Preflight** — all must pass before anything is built:
   - Working tree clean (`git status --short` empty) and `master` pushed to the
     `macos` remote (`git log macos/master..master` empty). If not, stop and ask.
   - Tests pass: `/opt/homebrew/opt/dotnet@8/bin/dotnet test app/XDM/XDM.Tests/XDM.Tests.csproj`
   - The tag doesn't already exist on the remote.

2. **Build & package**:
   ```sh
   /opt/homebrew/opt/dotnet@8/bin/dotnet build app/XDM/XDM.Gtk.UI/XDM.Gtk.UI.csproj -c Release
   ./app/packaging/make-macos-app
   ```
   Both must exit 0. Never build the whole solution — the WPF/WinForms projects
   are Windows-only and will fail on macOS.

3. **Create artifacts** in a fresh `/tmp/xdm-release/`:
   ```sh
   ditto -c -k --keepParent app/packaging/xdm.app /tmp/xdm-release/xdm-macos-arm64.zip
   (cd app/XDM && zip -qr /tmp/xdm-release/xdm-chrome-extension.zip chrome-extension -x "*.DS_Store")
   ```
   **Must use `ditto`, not `zip`, for the .app** — Contents/Frameworks relies on
   dylib alias symlinks; if they get materialized as duplicate files, glib
   deadlocks at startup. Verify after zipping: extract to a temp dir with
   `ditto -x -k` and check `ls -la .../Contents/Frameworks | grep '\->'` shows
   symlinks. Also smoke-test the exact build being shipped:
   `pkill -f xdm-app; cp -R app/packaging/xdm.app /Applications/; open -a /Applications/xdm.app`
   then confirm the process is alive and `curl -s http://127.0.0.1:8597/sync`
   returns 200 (`pkill` first — `open -a` refocuses a stale instance).

4. **Release notes** — write `/tmp/xdm-release/notes.md`:
   - Summarize `git log <last-tag>..HEAD --oneline` (first release: highlights of
     the fork) grouped as Highlights / Fixes, in user-facing language — name the
     symptom fixed, not the internal class.
   - Always end with the install block:
     ```
     ## Install
     1. Unzip xdm-macos-arm64.zip, drag xdm.app to /Applications.
     2. First launch: right-click → Open → Open (the app is ad-hoc signed).
     3. Browser extension: unzip xdm-chrome-extension.zip, then chrome://extensions
        → Developer mode → Load unpacked → pick the folder.

     Credit for XDM itself goes to @subhra74 (https://github.com/subhra74/xdm). GPL-2.0.
     ```

5. **Publish**:
   ```sh
   gh release create <tag> /tmp/xdm-release/xdm-macos-arm64.zip /tmp/xdm-release/xdm-chrome-extension.zip \
     --repo zubayrali/xdm-macos --title "XDM for macOS <version> (Apple Silicon)" \
     --notes-file /tmp/xdm-release/notes.md
   ```

6. **Confirm** — show the release URL from the command output and
   `gh release view <tag> --repo zubayrali/xdm-macos` asset list.
