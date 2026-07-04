# macOS Native Polish — GTK Head (Option 1)

Handoff/design doc for making the existing GTK UI *behave* like a Mac app,
without swapping the toolkit. No engine changes — `XDM.Core` is untouched
throughout. Everything here lives in `XDM.Gtk.UI` and `app/packaging/`.

## Goal & non-goals

**Goal:** the app feels native — top menu bar, no Homebrew prerequisite,
proper icon/dock, optional status-bar item. It stays visually GTK/Adwaita;
we're fixing *behavior* and *distribution*, not repainting widgets.

**Non-goals:** no Avalonia/SwiftUI head, no pixel-perfect Cocoa controls, no
changes to the download engine, IPC, or the browser flow. If a task needs to
touch `XDM.Core`, it's out of scope for this pass.

## Current state (2026-07)

- **Focus stealing: DONE.** `XDM.Gtk.UI/Utils/MacUtil.cs` calls
  `activateIgnoringOtherApps:` via the Obj-C runtime. Windows come to the
  front. Reuse this file's P/Invoke pattern for any further AppKit calls.
- **Menus are in-window popups.** `MainWindow.cs` builds GTK `Menu`s hung off
  a hamburger button (`btnMenu` → `mainMenu`) and context menus
  (`menuInProgress`, `menuFinished`). There is **no macOS top menu bar** —
  this is the #1 "not a Mac app" tell.
- **Packaging:** `app/packaging/make-macos-app` runs `dotnet publish` for
  `XDM.Gtk.UI` + `XDM.App.Host` and assembles `xdm.app`. `Info.plist` and
  `xdm.icns` already exist under `xdm.app/Contents/`. **GTK is NOT bundled** —
  the script prints `Runtime dependency: brew install gtk+3 adwaita-icon-theme`.
- Install is `cp -r app/packaging/xdm.app /Applications/`.
- Config/data dir: `~/.xdm-app-data/Data` (writable); log at `.../log.txt`
  (TRACE is defined in the csproj so `Log.Debug` works — don't remove it).

## Tasks, ordered by impact ÷ effort

### 1. macOS top menu bar  — *highest impact*

The visible giveaway. GTK menus render inside the window; a Mac app puts them
in the system bar (Apple menu → App name, plus File/Edit/etc.).

**Approach:** `gtk-mac-integration` (the `GtkosxApplication` API). It reparents
a GTK `MenuBar` into the native NSMenu and wires the standard
App→About/Preferences/Quit slots.

- The library ships with Homebrew GTK as `libgtkmacintegration`. There is **no
  GtkSharp binding** for it, so add a small P/Invoke wrapper next to
  `MacUtil.cs` — same DllImport style already proven there.
  - Key entry points: `gtkosx_application_get_type` / `..._sharedApplication`,
    `gtkosx_application_set_menu_bar(app, GtkMenuShell*)`,
    `gtkosx_application_insert_app_menu_item`,
    `gtkosx_application_set_window_menu`, `gtkosx_application_ready`.
  - You'll need the `GtkMenuShell*` native handle from a GtkSharp `MenuBar` —
    `widget.Handle`.
- Build a real `Gtk.MenuBar` (hidden in-window via `set_menu_bar`, so it only
  shows in the system bar). Populate from the same actions the hamburger menu
  already calls — reuse the existing handlers, don't duplicate logic.
- Standard slots to fill: **About XDM**, **Preferences…** (⌘,), **Quit** (⌘Q),
  plus a **File** menu (New Download ⌘N, New Video…), **Edit** (Cut/Copy/Paste
  for the dialogs' text fields), **Window** (Minimize ⌘M).

**Acceptance:** menus appear in the macOS bar; ⌘Q quits, ⌘, opens Settings,
About shows the existing About dialog. Hamburger menu can stay (harmless) or be
hidden on macOS — leave it for now to avoid regressions.

**Risk:** wrong native handle / calling before the GTK app is realized → menu
doesn't attach. Call `gtkosx_application_ready` after the main window is shown.
Guard every call with `RuntimeInformation.IsOSPlatform(OSPlatform.OSX)` like
`MacUtil` does, so Linux builds are unaffected.

### 2. Bundle GTK into the .app  — *makes it distributable*

Right now a fresh Mac needs `brew install gtk+3 adwaita-icon-theme` or the app
won't launch. To ship to anyone, the `.app` must be self-contained.

**Approach (in `make-macos-app`):**
- Copy the GTK dylibs + their deps into `xdm.app/Contents/Resources/lib` (or
  `.../Frameworks`). Use `dylibbundler` (`brew install dylibbundler`) — it
  recursively copies dependencies and rewrites install-name paths to
  `@executable_path/…` / `@rpath`. Point it at the GTK loader stack that
  `xdm-app-host` dlopens.
- Bundle the **GDK-Pixbuf loaders** and **GIO modules** and set
  `GDK_PIXBUF_MODULE_FILE`, `GTK_PATH`, `GTK_DATA_PREFIX`, `XDG_DATA_DIRS`,
  `DYLD_LIBRARY_PATH` via a small launcher shell script set as the
  `CFBundleExecutable`, or via env in the host process before GTK init.
- Include the **Adwaita icon theme** + a compiled `icon-theme.cache`, and
  GTK's `gdk-pixbuf` `loaders.cache` (regenerate with
  `gdk-pixbuf-query-loaders` pointing at the bundled paths).
- Run `glib-compile-schemas` output into the bundle so settings-backed widgets
  don't warn.

**Acceptance:** on a Mac with GTK Homebrew *uninstalled* (or a clean user), the
`.app` launches, dialogs render, icons show. Verify with
`otool -L xdm.app/Contents/MacOS/<binary>` that GTK paths are `@rpath`, not
`/opt/homebrew/...`.

**Risk:** highest-effort task. The pixbuf/GIO module caches are the usual
breakage — if icons/images are blank, the loaders cache is pointing at absolute
Homebrew paths. Test by renaming `/opt/homebrew/opt/gtk+3` and relaunching.

### 3. Icon / dock / About polish  — *low effort, visible*

- `xdm.icns` exists but verify it has all sizes (`iconutil` from a
  `.iconset`; needs 16→1024 @1x/@2x). A crisp dock icon is cheap.
- `Info.plist`: set `CFBundleName`, `CFBundleDisplayName` ("XDM"),
  `CFBundleShortVersionString`, `LSMinimumSystemVersion`, `NSHighResolutionCapable=true`,
  and a `CFBundleIdentifier` you control (e.g. `com.zubayrali.xdm`).
- About dialog already exists (`Dialogs/About/AboutDialog.cs`) — just make sure
  the menu-bar "About XDM" routes to it.

**Acceptance:** sharp dock icon, correct app name in the menu bar and About.

### 4. Menu-bar status item (tray)  — *optional, nice-to-have*

A macOS status-bar (top-right) item for quick "New Download / Show Window /
Monitoring on-off / Quit". GTK's `StatusIcon` is deprecated and unreliable on
quartz; prefer a small NSStatusItem via P/Invoke (same Obj-C runtime pattern as
`MacUtil`) or defer until after 1–3 land.

**Acceptance:** icon in the top-right bar with a working menu. Skip if it fights
the toolkit — not worth a rabbit hole.

## Build / test loop (unchanged)

```
/opt/homebrew/opt/dotnet@8/bin/dotnet build app/XDM/XDM.Gtk.UI/XDM.Gtk.UI.csproj -c Release
./app/packaging/make-macos-app
pkill -f xdm-app-host; cp -R app/packaging/xdm.app /Applications/; open -a /Applications/xdm.app
```

`pkill` before reinstall matters — `open -a` focuses a stale running instance
instead of loading the new binary (learned the hard way). Use `cp -R`, never
`cp -r`: lowercase -r follows symlinks, materializing the Frameworks dylib
aliases as duplicate files → two copies of one lib load → glib type-init
deadlocks at startup (also learned the hard way).

## Suggested order

Do **1** first (biggest perceived win, self-contained, low regression risk),
then **3** (cheap), then **2** (the distribution unlock, most fiddly), and **4**
only if wanted. Each is independently shippable; none touch the engine.

## ponytail notes

- Reuse the existing `MacUtil` P/Invoke style for all AppKit/GtkosxApplication
  calls — no new interop library, no new dependency.
- Reuse existing menu action handlers for the menu bar; don't fork the logic.
- Task 2 is the only one that earns real tooling (`dylibbundler`); everything
  else is a few dozen lines guarded by an `IsOSPlatform(OSX)` check.
