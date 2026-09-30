# Windows review build

**Status: BUILT 2026-09-28 — and never once run.** `make win-build` produces a 379 MB
folder at `src/app/MergulhoVirtual/Builds/Windows/`, and the shipped binary has been
verified **statically** (PE32+, Mono backend, desktop layer present in the assembly).
**Nothing in it has ever been executed** — not on Windows, not under Wine. Steps 1–6 are
implemented; one blocker not anticipated by this plan had to be cleared — see
**Step 5 › App UI sample** — and exactly what is and is not proven is itemised in
**Verification status** below.

**For:** whoever implements this in the repo. You know Unity and C#; you have not
necessarily thought about what a desktop target does to a phone-shaped UI.

**Goal:** hand the designer a Windows `.exe` they can click through **at several device
sizes**, so the V2 screens can be reviewed interactively — and so the layouts can be
checked against aspect ratios other than the single 390 × 844 frame everything was
designed at. Windows is the only machine available to them.

**Read Step 0 first.** Aspect-ratio checking specifically needs no build at all; it works
today. The build is only needed for *interaction*.

This is a **throwaway review target**, not a supported platform. Nothing here should
change how the Android build behaves, and none of it belongs in a release.

**Repo state this plan assumes (2026-09-28).** Two changes landed just before it and
neither conflicts with anything below, but know they are there:

- The app is **portrait-locked** (`defaultScreenOrientation: 0`, landscape autorotate
  flags zeroed). Orientation is a mobile-only setting, so it does not affect the Windows
  window — but it does mean every device preset in Step 1b is portrait by design, not by
  omission. See [android-adaptivity.md](android-adaptivity.md).
- `AppPanelSettings.fallbackDpi` is now **440**, set in
  [AppUiBuilder.cs](../src/app/MergulhoVirtual/Assets/Editor/AppUiBuilder.cs), not in the
  asset. It only applies when `Screen.dpi` is 0/invalid, which does not happen on Windows,
  so Step 1's `ConstantPixelSize` override is still required. The wider point stands:
  **AppPanelSettings.asset is GENERATED** — edit the builder and re-run `make ui-setup`,
  never the asset.

---

## Feasibility summary

Unity cross-compiles Linux → Windows with the **Mono** scripting backend, so the build
itself is straightforward. Four things stand in the way; all are fixable, none is deep.

| # | Blocker | Effort |
|---|---|---|
| 1 | ~~Windows Build Support module is not installed~~ | **DONE 2026-09-28** — see Prerequisite |
| 2 | UI Toolkit panel scales by monitor DPI → wrong size, varies per machine; and the frame needs to be switchable per device | ~60 lines (Steps 1, 1b, 1c) |
| 3 | Player settings are landscape-fullscreen | 3 fields |
| 4 | GPS and the photo picker have no standalone branch | ~40 lines |

Firebase is **not** a blocker: `FirebaseCppApp-13_11_0.dll` and `FirebaseCppAppCheck.dll`
are already present under
[Assets/Firebase/Plugins/x86_64/](../src/app/MergulhoVirtual/Assets/Firebase/Plugins/x86_64/),
`StreamingAssets/google-services-desktop.json` already exists, and
`FIREBASE_APPCHECK_ENABLED` is already in the `Standalone` define list in
[ProjectSettings.asset](../src/app/MergulhoVirtual/ProjectSettings/ProjectSettings.asset#L849).

---

## Verification status — what is and is not proven

The distinction matters more than usual here, because a Unity player build **succeeds
whether or not the `#if UNITY_STANDALONE && !UNITY_EDITOR` code compiled into it**. "The
build is green" therefore proves almost nothing on its own. What follows separates commands
that were actually run from claims that were never tested.

### Proven — these commands were run

- `make ds-compile` → passed.
- `make win-build` → succeeded in **1m32s**, once the App UI blocker (Step 5) was cleared:

  ```
  WIN-BUILD-SUMMARY sizeMB=377.6 path=.../Builds/Windows/MergulhoVirtual.exe
  ```

- Output folder **379 MB**; `MergulhoVirtual.exe` is **667,648 bytes**.
- It is a genuine Windows binary, not a stub — `file` says:

  ```
  MergulhoVirtual.exe: PE32+ executable (GUI) x86-64
  UnityPlayer.dll:     PE32+ executable (DLL) x86-64
  ```

- `MergulhoVirtual_Data/Managed/Assembly-CSharp.dll` **exists** → the **Mono** backend was
  used. IL2CPP emits no `Managed/` folder at all, so that one path is the check that the
  Mono-only module constraint (Prerequisite) was honoured.
- The three sample JPEGs are present at
  `MergulhoVirtual_Data/StreamingAssets/SamplePhotos/`.
- **The important one — the desktop layer actually shipped.** Every line of Steps 1 / 1b /
  1c / 3 / 4 sits behind `#if UNITY_STANDALONE && !UNITY_EDITOR`. Had those guards resolved
  false for the *player*, the build would still have **succeeded**, and the designer would
  have silently received a full-screen stretched UI with no device picker and no per-preset
  insets. So the shipped assembly was scanned for the symbols instead of assumed:

  ```bash
  A=MergulhoVirtual_Data/Managed/Assembly-CSharp.dll
  { strings -a "$A"; strings -a -el "$A"; } | grep -E "DesktopReviewMode|V2 ref"
  ```

  `DesktopReviewMode`, `PickSampleInStandalone`, `PollHotkeys`, `SamplePhotos` and all five
  preset labels came back present. **`-el` is not optional**: .NET user strings live in the
  `#US` heap as UTF-16 and are invisible to a plain ASCII pass — and the non-ASCII `×` in
  the preset labels truncates the ASCII pass mid-label at `V2 ref 390`, which is itself
  still proof of presence.

### Not proven — the exe has never been launched

Stated plainly, because the section above is easy to misread as "it works": **nothing is
known about runtime behaviour.** Whether it starts at all, whether the letterbox renders,
whether F2/F3 actually switch presets, whether the sample picker returns a photo — every
one of those is untested.

Wine was not available on this machine — not installed, and `sudo` requires a password
here. The recipe for whoever tries:

```bash
sudo apt install --no-install-recommends wine64      # wine 9.0 is in the distro repos
cd src/app/MergulhoVirtual/Builds/Windows
DISPLAY=:1 wine MergulhoVirtual.exe -screen-fullscreen 0 -screen-width 900 -screen-height 1200 -force-glcore
```

`-force-glcore` because Wine's D3D11/12 path is the usual source of spurious Unity
failures; if it starts there, retry *without* the flag to see whether D3D also works.

The log lands at
`~/.wine/drive_c/users/$USER/AppData/LocalLow/TRN/Mergulho Virtual/Player.log` — `TRN` and
`Mergulho Virtual` are `companyName` / `productName`, both read from
[ProjectSettings.asset](../src/app/MergulhoVirtual/ProjectSettings/ProjectSettings.asset).

**Expected Wine noise, not real bugs:** `DirectML.dll` failing to initialise (an Inference
Engine backend, only reached on the AR route, which is dead on desktop), and the Wine audio
stack complaining on startup.

**And mind what a Wine result is worth.** A Wine **failure** is weak evidence that the build
is broken — Unity players fail under Wine for Wine-specific graphics reasons routinely. A
Wine **success** is good evidence. The only conclusive test is the designer's actual Windows
machine.

---

## Step 0 — Aspect-ratio checking needs no build (do this first)

[UiScreenshotHarness](../src/app/MergulhoVirtual/Assets/Editor/UiShots/UiScreenshotHarness.cs)
already parameterizes the whole frame. These env vars exist today:

| Var | Default | |
|---|---|---|
| `MV_SHOT_WIDTH` | 390 | frame width in dp |
| `MV_SHOT_HEIGHT` | 844 | frame height in dp |
| `MV_SHOT_TOP_INSET` | 47 | status bar / notch |
| `MV_SHOT_BOTTOM_INSET` | 34 | home indicator |
| `MV_SHOT_SCALE` | 2 | supersample, 1–4 |
| `MV_SHOT_DIR` | `.shots` | output directory |
| `MV_SHOT_FILTER` | — | subject id substring (`make ds-shots SHOT=home`) |
| `MV_SHOT_THEMES` | `light,dark` | |

So a device matrix is a shell loop, with the editor closed:

```bash
# 360×640 — small 16:9 Android, the real stress test. No notch, no home indicator.
MV_SHOT_WIDTH=360 MV_SHOT_HEIGHT=640 MV_SHOT_TOP_INSET=24 MV_SHOT_BOTTOM_INSET=0 \
  MV_SHOT_DIR=.shots/android-360x640 MV_SHOT_THEMES=light make ds-shots

# 360×800 — the most common modern budget Android (20:9). NARROWER than the 390 reference.
MV_SHOT_WIDTH=360 MV_SHOT_HEIGHT=800 MV_SHOT_TOP_INSET=24 MV_SHOT_BOTTOM_INSET=0 \
  MV_SHOT_DIR=.shots/android-360x800 MV_SHOT_THEMES=light make ds-shots

# 412×915 — Pixel 7/8.
MV_SHOT_WIDTH=412 MV_SHOT_HEIGHT=915 MV_SHOT_TOP_INSET=24 MV_SHOT_BOTTOM_INSET=0 \
  MV_SHOT_DIR=.shots/pixel-412x915 MV_SHOT_THEMES=light make ds-shots

# 430×932 — iPhone Pro Max.
MV_SHOT_WIDTH=430 MV_SHOT_HEIGHT=932 \
  MV_SHOT_DIR=.shots/iphone-430x932 MV_SHOT_THEMES=light make ds-shots
```

Each run writes a `manifest.json` recording the frame size, scale and insets, so the sets
stay self-describing and comparable side by side. For *layout* review this is better than
a resizable window — it is reproducible, diffable, and the designer can open four folders
next to each other.

**Costs:** each run re-renders glyphs and therefore churns the LFS-tracked font assets
under `Assets/DesignSystem/Fonts/` (CLAUDE.md, "Working in this codebase") — `git checkout`
them afterwards if the diff is noise. And `ds-shots` is the one Unity target that needs a
real X display (`DISPLAY=:1` by default).

### What to look at in the matrix

The screens were transcribed at 390 dp wide and never tested narrower. The likely
breakages, in order:

1. **Home's 2×2 feature grid** — the grid-cell class carries `flex-grow: 1; flex-basis: 0`,
   and CLAUDE.md already records one bug from exactly that class being reused in a column
   context (`.mv-feature-card--wide` in
   [HomeScreen.uss](../src/app/MergulhoVirtual/Assets/UI/Screens/HomeScreen.uss#L303)).
   At 360 dp the two columns lose 15 dp each.
2. **Praias' two stat columns** — same shape, same risk.
3. **`MvMediaPicker`'s thumbnail grid** and the **species chips** on Praia detalhe — both
   wrap, so narrow means an extra row, which means the submit button moves below the fold.
4. **Short frames (640 dp)** — anything that fit without scrolling at 844 now scrolls.
   That is usually fine (every screen is in an `AppScrollView`) but worth confirming the
   nav bar is not crowding content.

---

## Prerequisite — build module ✅ INSTALLED 2026-09-28

`WindowsStandaloneSupport` is present and Unity confirms the target:

```
$ ls ~/Unity/Hub/Editor/6000.3.14f1/Editor/Data/PlaybackEngines/
AndroidPlayer  LinuxStandaloneSupport  VisionOSPlayer  WindowsStandaloneSupport

BuildPipeline.IsBuildTargetSupported(Standalone, StandaloneWindows64) => True
(active build target still Android — verified without switching platforms)
```

**Mono only.** `Variations/` ships `win32|win64|win_arm64 × development|nondevelopment`
**`_mono`** and no `_il2cpp` — Windows IL2CPP requires a Windows host. The build script in
Step 5 must not request IL2CPP.

### How it was installed, and why not via Unity Hub

The Hub CLI does not work on this machine — worth knowing before anyone burns time on it:

- `unityhub --headless <cmd>` exits **0 with no output**. `/usr/bin/unityhub` does
  `exec unityhub-bin "$([[ $UNPRIVILEGED_USERNS_ENABLED == '' || ... == 0 ]] && echo '--no-sandbox')" "$@"`,
  and `/proc/sys/kernel/unprivileged_userns_clone` is `1` here — so the command
  substitution is empty and Electron receives an **empty string as argv[1]**, swallowing
  everything after it.
- `unityhub-bin --headless help` → `bad option: --headless` (exit 9).
- `unityhub-bin -- --headless help` → Node mode, `Cannot find module '--headless'`.
- Under a pty it starts and then hangs indefinitely with no output.

So the module was installed by hand from Unity's own CDN. **There is no Linux tarball for
this module** — `LinuxEditorTargetInstaller/UnitySetup-Windows-Mono-Support-for-Editor-6000.3.14f1.tar.xz`
returns **404**. Unity publishes it only as a macOS `.pkg`, and that is what the Hub itself
uses: this editor's own `modules.json` lists `MacEditorTargetInstaller/...pkg` for
`windows-mono` **and for `android`**, which is already installed and working here. So the
`.pkg` route is the supported configuration on a Linux host, not a workaround.

```bash
CS=d68c3f99a318   # changeset from ProjectSettings/ProjectVersion.txt
curl -L -o win-mono.pkg \
  "https://download.unity3d.com/download_unity/$CS/MacEditorTargetInstaller/UnitySetup-Windows-Mono-Support-for-Editor-6000.3.14f1.pkg"
# 381,796,106 bytes — matches modules.json `downloadSize` exactly.

# .pkg is a xar archive; TargetSupport.pkg.tmp/Payload is gzip'd cpio whose ROOT is
# already the contents of WindowsStandaloneSupport (./modules.asset, ./Tools, ./Bee,
# ./Variations, ./WinPlayerBuildProgram.exe) — same shape as LinuxStandaloneSupport.
bsdtar -xf win-mono.pkg -C pkg TargetSupport.pkg.tmp/Payload
DEST=~/Unity/Hub/Editor/6000.3.14f1/Editor/Data/PlaybackEngines/WindowsStandaloneSupport
mkdir -p "$DEST" && bsdtar -xf pkg/TargetSupport.pkg.tmp/Payload -C "$DEST"   # 969 MB
```

Then `modules.json` was edited to set `windows-mono`'s `selected: true` so the Hub UI
reflects reality (backup at `modules.json.bak`). The editor does not need that flag — it
discovers playback engines from the filesystem — but the Hub does.

**The Mac-installer-on-Linux worry turned out to be unfounded**, and it is worth recording
why so nobody re-raises it: the module contains **no mach-o binaries at all**. `Tools/`
holds exactly one file, `DevicePortalTool.exe`, which is a managed .NET assembly (and is
only used for Device Portal deployment, never for building). `WinPlayerBuildProgram.exe`
is likewise managed, and everything in `Variations/` is a Windows *target* payload, which
is host-independent by definition.

**Redo after a Unity upgrade.** Installing a new editor version will not carry this over,
and the changeset in the URL is version-specific — read the new one from
`ProjectVersion.txt`'s `m_EditorVersionWithRevision`.

---

## Step 1 — Fix the panel scale (the one that actually matters)

[AppPanelSettings.asset](../src/app/MergulhoVirtual/Assets/UI/AppPanelSettings.asset)
uses `m_ScaleMode: 1` (Constant Physical Size) at `m_ReferenceDpi: 160`. On a phone
that is exactly what gives **1 USS px = 1 dp = 1 Figma pt** — the calibration every
screen's spacing was transcribed against.

On Windows it is actively harmful. The panel scales by `Screen.dpi / 160`:

- a typical 1080p monitor reports ~96 dpi → everything renders at **0.6×**
- a HiDPI laptop reports something else → a **different** wrong size

So the designer would review a UI that is neither the spec nor reproducible between
their machine and yours. Do not ship the review build without fixing this.

**Fix:** force Constant Pixel Size and derive the scale from the window, so 1 USS px
maps to a known number of screen px instead of to the monitor's DPI. See
**Step 1b — resizable window** below for whether to lock the window at 390 × 844 or let
it resize; both go through the same helper.

Add a static helper — e.g. `Assets/Scripts/UI/UiToolkit/DesktopReviewMode.cs` — and call
it from `AppUiHost.Awake`, which is already the composition root. Do not add a new
GameObject for this.

```csharp
// DesktopReviewMode.cs  —  Assets/Scripts/UI/UiToolkit/
public static class DesktopReviewMode
{
    /// A device the designer can switch to. Frame size AND safe-area insets travel
    /// together — insets are a property of the device, not a constant (Step 1c).
    public readonly struct Preset
    {
        public readonly string Label;
        public readonly int W, H;
        public readonly float Top, Bottom;
        public Preset(string label, int w, int h, float top, float bottom)
            { Label = label; W = w; H = h; Top = top; Bottom = bottom; }
    }

    // Same 47/34 the shot harness uses for the reference frame, so the exe and
    // .shots/ agree. Android values are the OS status bar with no home indicator.
    public static readonly Preset[] Presets =
    {
        new("Android 360×640",  360, 640, 24f,  0f),  // small 16:9 — stress test
        new("Android 360×800",  360, 800, 24f,  0f),  // most common budget device
        new("V2 ref 390×844",   390, 844, 47f, 34f),  // what everything was designed at
        new("Pixel 412×915",    412, 915, 24f,  0f),
        new("iPhone 430×932",   430, 932, 47f, 34f),
    };

    static int current = 2;                 // start on the V2 reference frame
    public static Preset Current => Presets[current];

    // The !UNITY_EDITOR guard is load-bearing: UNITY_STANDALONE is also defined in
    // the Linux editor, and PanelSettings is a ScriptableObject — an Editor-time
    // write to it would persist into the asset and silently break the mobile
    // 160-dpi calibration for everyone.
    public static void Apply(PanelSettings panel, VisualElement documentRoot,
                             VisualElement routerRoot)
    {
#if UNITY_STANDALONE && !UNITY_EDITOR
        if (panel == null) return;
        panel.scaleMode = PanelScaleMode.ConstantPixelSize;

        // The frame is centred in whatever window the user drags out; the area
        // around it is bezel. Changing preset just restyles + refits.
        documentRoot.style.justifyContent = Justify.Center;
        documentRoot.style.alignItems     = Align.Center;
        routerRoot.style.flexGrow = 0f;                 // MdRouter.cs sets this to 1

        documentRoot.RegisterCallback<GeometryChangedEvent>(_ => Refit(panel, routerRoot));
        Select(current, panel, routerRoot);
#endif
    }

#if UNITY_STANDALONE && !UNITY_EDITOR
    public static void Select(int index, PanelSettings panel, VisualElement routerRoot)
    {
        current = ((index % Presets.Length) + Presets.Length) % Presets.Length;
        routerRoot.style.width  = Current.W;
        routerRoot.style.height = Current.H;
        Refit(panel, routerRoot);
        // Insets are per-device — push them through the same seam the harness uses.
        AppUiHost.Router?.SetEdgeInsets(Current.Top, 0f, 0f, Current.Bottom);
    }

    static void Refit(PanelSettings panel, VisualElement routerRoot)
    {
        float fit = Mathf.Min(Screen.width  / (float)Current.W,
                              Screen.height / (float)Current.H);
        // Snapped to quarter steps — see the hairline caveat in Step 1b.
        panel.scale = Mathf.Max(0.5f, Mathf.Floor(fit * 4f) / 4f);
    }
#endif
}
```

Call `Apply` from `AppUiHost.OnEnable`, after the visual tree is built — not `Awake`.
`UIDocument` tears the tree down on deactivate and rebuilds it, so a root captured in
`Awake` is stale by the time the panel renders.

**Alternative if runtime mutation feels wrong:** author a second
`AppPanelSettings-Desktop.asset` and have
[AppUiBuilder](../src/app/MergulhoVirtual/Assets/Editor/AppUiBuilder.cs) pick it for the
Standalone target. Cleaner in principle, more machinery, and it adds an asset that has
to stay in sync with the real one — which `TokenDisciplineTests` will not catch. The
runtime mutation is recommended for a throwaway target.

---

## Step 1b — The device picker (this is the actual point)

**The frame must be a variable, not a constant.** The whole reason for the Windows build
is to exercise aspect ratios other than 390 × 844, so pinning the letterbox to one size
would defeat it. `Select()` above is the mechanism; it needs a way to reach the designer.

### Two things called "adaptive", with opposite answers

| | Possible? | |
|---|---|---|
| **Same layout, different frame sizes** — reflowed at 360 / 390 / 412 / 430 dp wide | **Yes** | this is what you want, and it is what `Select()` does |
| **A genuine desktop layout** at desktop widths | **No** | no V2 desktop design exists, and **USS has no `@media`** — UI Toolkit has no stylesheet breakpoints at all. Responsive layout there is C# handling `GeometryChangedEvent` and toggling classes. Design work plus new code, not a build setting. |

So: let the window resize freely, but keep the **content** inside a selectable device
frame. Free-stretching the layout to 1600 px would turn the 2×2 feature grid into four
huge tiles and spread `MdNavigationBar` across the window — a layout that ships nowhere.

**Why this already works:** nothing hard-codes the frame. There is no `390` or `844`
anywhere in `Assets/UI/` or `Assets/Scripts/UI/`, and the only fixed `width:` rules in the
screen stylesheets are `24px` / `68px` / `36px` in
[HomeScreen.uss](../src/app/MergulhoVirtual/Assets/UI/Screens/HomeScreen.uss) — icon-scale,
not layout. Everything else is flexbox, so it genuinely reflows.

### Exposing the picker

Cheapest that works: a **hotkey** (`F1` / `F2` to cycle presets, or `1`–`5`) plus a small
always-on label in a bezel corner naming the current preset, so a screenshot the designer
sends back is self-identifying. No UI Toolkit work, no risk of the picker being mistaken
for app chrome.

If you want it clickable, build it into the **bezel area**, never inside the frame — an
`MdDropdown` floating in the letterbox margin. Do not put it on a screen; it would end up
in the design review.

Paint the bezel a flat neutral (`#2A2A2A`) so the frame edge is unambiguous.

**Known cosmetic gap:** the frame applies to the UI Toolkit panel only. The legacy uGUI
`ScreenUI` canvas — the splash image, and `Sobre` — still paints full-window behind it and
will spill past the frame edge. Both are Slice 6 deletions; not worth fixing.

### Caveat on fractional scale — the 1px hairlines

The design system renders every elevated surface as a **1px `outline-variant` border**
rather than a shadow (UI Toolkit has no `box-shadow`; CLAUDE.md, "UI Toolkit limitations
accepted"). At a non-integer scale those hairlines land on fractional device pixels and
render inconsistently — some 1 px, some 2 px, some a washed-out grey. Text is unaffected
(SDF). Hence the quarter-step snap in `Refit`.

Practical ceilings: a 1080p monitor fits roughly **1.0–1.25×** of an 844 dp-tall frame, and
only **1.5×** of a 640 dp one. A 1440p monitor gets a clean 1.5× throughout. Scale is
per-preset, so switching to a short device automatically gets more zoom.

---

## Step 1c — Insets belong to the preset, not to the platform

`Screen.safeArea` on Windows equals the whole screen, so `AppUiHost.ApplyEdgeInsets`
resolves to `0, 0, 0, 0`. On a real phone the content container pads for the status bar /
notch and `MdNavigationBar` absorbs the home-indicator strip as padding. Without them the
designer sees **content flush against the top edge and a nav bar 34 dp shorter than on
device**, and will report both as bugs.

**The repo already solved this**, for the same reason, in
[UiScreenshotHarness.cs:122](../src/app/MergulhoVirtual/Assets/Editor/UiShots/UiScreenshotHarness.cs#L122)
— batchmode has no meaningful safe area either, so the harness injects 47 dp / 34 dp via
`router.SetEdgeInsets`. The Windows build should go through the same seam.

**But do not hardcode 47/34 here.** Insets are a device property: a notched iPhone is
47/34, a typical Android is ~24 top and **0** bottom (gesture bar is an overlay, not a
reserved strip), a tablet is 0/0. Shipping one constant across all presets would make the
Android frames silently wrong — and "does the nav bar look right on Android" is one of the
questions this build exists to answer. Hence `Top`/`Bottom` on `Preset`.

This needs `AppUiHost` to expose its router (`public MdRouter Router => router;`) or to
route the call itself; `MdRouter.SetEdgeInsets` is already the public seam the harness uses.

---

## Step 2 — Standalone player settings

In [ProjectSettings.asset](../src/app/MergulhoVirtual/ProjectSettings/ProjectSettings.asset),
currently:

```
defaultScreenWidth: 1024      defaultScreenHeight: 768
fullscreenMode: 1             # FullScreenWindow
resizableWindow: 0
```

Set these from the build script (Step 5) rather than by hand, so the Android profile is
never touched:

```csharp
// Start large enough that the tallest preset (932 dp) fits at 1× with bezel to spare.
// Refit() clamps the scale to whatever the window actually allows.
PlayerSettings.defaultScreenWidth  = 700;
PlayerSettings.defaultScreenHeight = 1000;
PlayerSettings.fullScreenMode      = FullScreenMode.Windowed;
PlayerSettings.resizableWindow     = true;    // device frame refits on resize — Step 1b
PlayerSettings.runInBackground     = true;    // survives alt-tabbing to Figma
```

These are only the *starting* window; the designer resizes freely from there and
`Refit()` rescales the frame to whatever fits. Nothing calls `Screen.SetResolution` —
forcing a resolution would fight the drag and defeat the point.

**As built — the save/restore held.** After the build, `ProjectSettings.asset` came back
with **no** diff in window size, `fullScreenMode`, `resizableWindow`, `runInBackground` or
the scripting backend: the script's restore covers all five. The one residual is a 3-line
`m_BuildTarget: Standalone` block Unity appended to the per-platform batching settings
during the `-buildTarget Win64` platform switch — Unity's doing, not the script's. Harmless
defaults; revert it if you want a clean diff.

---

## Step 3 — GPS stub for standalone

[GPSHandler.cs:81](../src/app/MergulhoVirtual/Assets/Scripts/GPSHandler.cs#L81) hardcodes
the Fernando de Noronha coordinate only under `UNITY_EDITOR`:

```csharp
#if UNITY_EDITOR
    return new GPSLocation(-32.44f, -3.85f);
```

On Windows, `Input.location` never leaves `Initializing`, the coroutine in `Start()`
burns its 20 s timeout, `gpsOk` stays false and `CurrentPlaceName` is null. That *is* a
real supported state (the Praias landing has a UI for it), but it is not the state the
designer wants to land in.

**Fix — one token:**

```csharp
#if UNITY_EDITOR || UNITY_STANDALONE
```

The stub coordinate `(-3.85, -32.44)` falls inside Praia da Cacimba do Padre's OSM
outline, so the build boots into a real beach exactly like the editor does, and the
Praias hero selector reaches every other one.

**Also check** `debugTxt` is wired in the scene — `Start()` writes `debugTxt.text`
unguarded at line 33 while `Update()` null-checks it. A null there throws before the
GPS path is even reached.

---

## Step 4 — Sample-photo picker

[GalleryPicker.cs:26](../src/app/MergulhoVirtual/Assets/Scripts/Photo/GalleryPicker.cs#L26)
falls through to:

```csharp
#else
    callback(null, "gallery picker not supported on this platform");
#endif
```

so on Windows **the entire Avistamentos form is untestable** — the picker always errors,
no thumbnail ever appears, and the submit path can never be reached because the photo is
the one required field. That is Slice 3, one of the two screens most worth reviewing.

**Decision taken: bundled sample photos**, not a native file dialog. A real dialog means
adding StandaloneFileBrowser — a third UPM dependency with a Windows-only native DLL that
cannot be tested from this machine. Samples are deterministic, dependency-free, and give
the designer a realistic thumbnail grid every time.

### Implementation

1. Copy 3–4 JPEGs into `Assets/StreamingAssets/SamplePhotos/`. Good candidates already in
   the repo: the shark photos under
   [Resources/Animals/](../src/app/MergulhoVirtual/Assets/Resources/Animals/)
   (`hammerhead.jpg`, `tiger_shark.jpg`, `nurse_shark.jpg`) — a shark photo is exactly
   what a real sighting report carries. Copy them; do not move them, and do not reference
   `Resources/` from the picker (those are imported as Sprites, not readable files).
2. Add a `UNITY_STANDALONE` branch to `GalleryPicker`:

```csharp
#elif UNITY_STANDALONE
    PickSampleInStandalone(callback);
```

```csharp
#if UNITY_STANDALONE && !UNITY_EDITOR
    static int sampleCursor;

    static void PickSampleInStandalone(PickCallback callback)
    {
        var dir = Path.Combine(Application.streamingAssetsPath, "SamplePhotos");
        // On Windows, StreamingAssets is a plain directory under <exe>_Data — no
        // UnityWebRequest needed (that is the Android/WebGL path).
        var files = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.jpg")
            : System.Array.Empty<string>();
        if (files.Length == 0) { callback(null, "no sample photos bundled"); return; }
        System.Array.Sort(files);                       // stable order across runs
        var src = files[sampleCursor++ % files.Length]; // cycle, so repeat picks differ

        // GalleryPicker's contract is "a file the caller owns and may move/delete".
        // Copy out of StreamingAssets so the job pipeline can never delete a bundled asset.
        var dst = Path.Combine(Application.temporaryCachePath,
                               $"sample-{System.Guid.NewGuid():N}.jpg");
        try { File.Copy(src, dst, true); }
        catch (System.Exception e) { callback(null, e.Message); return; }
        callback(dst, null);
    }
#endif
```

**Do not** decode and re-encode anywhere on this path — the EXIF rule in CLAUDE.md
("Sighting reports") holds for the sample photos too, and a `Texture2D.EncodeToJPG`
round-trip here would quietly make the review build exercise a different upload path
than the real one.

**Alternative seam, if you would rather not touch `GalleryPicker`:** `IPhotoPicker`
([IPhotoPicker.cs:70](../src/app/MergulhoVirtual/Assets/UI/Interfaces/IPhotoPicker.cs#L70))
is already the clean boundary — write a `SamplePhotoPickerAdapter : IPhotoPicker` beside
[GalleryPhotoPickerAdapter](../src/app/MergulhoVirtual/Assets/Scripts/UI/UiToolkit/UiServiceAdapters.cs#L483)
and pick between them in `AppUiHost.Awake` under the define. Slightly more honest
(a sample library is not a gallery), one more file.

---

## Step 5 — Build script + make target

There is no `BuildPipeline` usage anywhere in
[Assets/Editor/](../src/app/MergulhoVirtual/Assets/Editor/) yet — this is the first build
automation in the repo. Follow the existing headless pattern: an `-executeMethod` entry
point plus a Makefile target that tails the log on failure.

`Assets/Editor/WindowsReviewBuild.cs`:

```csharp
public static class WindowsReviewBuild
{
    public static void BuildHeadless()
    {
        // Apply the desktop-only player settings from Step 2 here, not by hand,
        // so the committed ProjectSettings keep the Android profile.
        ...
        var opts = new BuildPlayerOptions {
            scenes           = new[] { "Assets/Scenes/MainScene.unity" },
            locationPathName = "Builds/Windows/MergulhoVirtual.exe",
            target           = BuildTarget.StandaloneWindows64,
            options          = BuildOptions.Development,  // keeps the log + profiler
        };
        var report = BuildPipeline.BuildPlayer(opts);
        if (report.summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
```

Makefile target, matching the `ds-*` house style (editor must be CLOSED):

```make
win-build: ## Build the Windows review exe for the designer (headless; editor closed)
	@mkdir -p $(ULOG)
	@$(UNITY) -batchmode -quit -nographics -projectPath $(UPROJ) \
		-buildTarget Win64 \
		-executeMethod WindowsReviewBuild.BuildHeadless \
		-logFile $(ULOG)/win-build.log \
		|| { echo "FAILED — last 60 log lines:"; tail -60 $(ULOG)/win-build.log; exit 1; }
	@echo "win-build OK -> $(UPROJ)/Builds/Windows/"
```

Add `Builds/` to the **Unity project's**
[.gitignore](../src/app/MergulhoVirtual/.gitignore) — not the repo-root one. Its
"# Builds" section currently lists only `*.apk` / `*.aab` / `*.unitypackage` / `*.app`,
so a `Builds/Windows/` folder would otherwise show up as ~300–500 MB of untracked
files. It must never be committed — and never added to Git LFS either, which the repo
uses only for the Firebase binaries and the TextCore font assets.

Remember `-buildTarget Win64` on the command line: without it Unity builds with whatever
target the project was last switched to, and the first switch triggers a full reimport.

---

### App UI sample — the one blocker this plan did not anticipate

The first `make win-build` failed in 46 s with, repeated ~13×:

```
Assets/Samples/App UI/2.2.2/UI Kit/Scripts/Examples.cs(11,28): error CS0234:
  The type or namespace name 'UI' does not exist in the namespace 'Unity.AppUI'
```

**Why `make ds-compile` passes and only the player build fails.** The imported App UI
sample has **no `.asmdef`**, so `Examples.cs` compiles into **Assembly-CSharp** — the main
game assembly. `APP_UI_EDITOR_ONLY` is in the **Standalone** scripting-define list (and in
iPhone's) but **not Android's**, and the App UI package's own asmdefs carry that define, so
its runtime types do not exist in a Standalone *player*. Editor compilation resolves them
fine, which is exactly why the compile check is green and the build is not.

**Resolved by deleting `Assets/Samples/App UI/`** (576 KB, 43 files). It is referenced by
nothing — `grep -rl "Unity.AppUI" Assets --include=*.cs` outside the sample returns empty —
it is a Package Manager sample re-importable in two clicks, and CLAUDE.md already records
the App UI spike as moot ("nothing here wraps it"). Revert with
`git checkout -- 'src/app/MergulhoVirtual/Assets/Samples'`, but the build will fail again.

The alternative — an Editor-only `.asmdef` on the sample — was rejected: an asmdef with no
references cannot see `Unity.AppUI` at all, so it would need the package's assembly list
enumerated and kept in sync, and would be clobbered on sample re-import.

**Two build-output notes:**
- `-buildTarget Win64` made Unity append a 3-line `m_BuildTarget: Standalone` block to the
  per-platform batching settings in `ProjectSettings.asset`. That is Unity's platform
  switch, not the build script, and the script's save/restore does not cover it. Harmless
  defaults; revert if you want a clean diff.
- The output contains `Mergulho Virtual_BurstDebugInformation_DoNotShip/` (596 KB). The
  name is literal — exclude it from the designer's zip.

---

## Step 6 — Getting it to the designer

What the build actually lays out:

```
Builds/Windows/
├── MergulhoVirtual.exe                                 667,648 bytes
├── UnityPlayer.dll
├── MergulhoVirtual_Data/                               scenes, Managed/, StreamingAssets/
├── MonoBleedingEdge/                                   the Mono runtime
├── D3D12/
├── DirectML.dll  +  DirectML.Debug.dll                 Inference Engine backend
└── Mergulho Virtual_BurstDebugInformation_DoNotShip/   596 KB — EXCLUDE from the zip
```

- Zip `Builds/Windows/` **whole, minus that last folder**. The name is literal — Unity
  emits it for `BuildOptions.Development` builds and it ships nowhere. Everything else is
  required: the `.exe` is useless without `UnityPlayer.dll` and `MergulhoVirtual_Data/`
  beside it, and a designer sent only the `.exe` will report that the app does not start.
- Windows SmartScreen will warn on an unsigned exe ("Windows protected your PC" →
  More info → Run anyway). Tell them in advance, in the same message as the link.
  Do **not** buy a code-signing certificate for a review build.
- `BuildOptions.Development` writes a log to
  `%USERPROFILE%\AppData\LocalLow\TRN\Mergulho Virtual\Player.log` — `TRN` /
  `Mergulho Virtual` are the project's `companyName` / `productName`. Ask for that file
  with any bug report.

---

## Hand the designer this list

Everything between the rules below is written **for them**, not for you — paste it into the
message carrying the download link. It is deliberately jargon-free; the technical *why*
behind each "known and expected" item is in the implementer notes after it.

---

**Opening it**

1. Unzip the **whole folder**, not just `MergulhoVirtual.exe`. The .exe on its own will not
   start — it needs `UnityPlayer.dll` and the `MergulhoVirtual_Data` folder sitting right
   next to it. You can safely delete the folder whose name ends in
   `_BurstDebugInformation_DoNotShip`.
2. Double-click `MergulhoVirtual.exe`. Windows will probably say **"Windows protected your
   PC"** — click **More info**, then **Run anyway**. That appears because the app is not
   code-signed, which a review build never will be. It is expected.

**Switching device sizes — this is the main thing to exercise**

The app draws a phone-shaped frame in the middle of the window, surrounded by grey bezel.
That frame can be five different phones:

| Key | Device | Size |
|---|---|---|
| `1` | Android, small | 360 × 640 |
| `2` | Android, common | 360 × 800 |
| `3` | **V2 reference — what the designs were drawn at** | **390 × 844** |
| `4` | Pixel | 412 × 915 |
| `5` | iPhone | 430 × 932 |

- **F3** = next device, **F2** = previous. Or press **1**–**5** to jump straight to one.
- It starts on **3**, the reference size.
- The name of the current device is printed on a label in the grey bezel just below the
  frame — so any screenshot you send back says for itself which size it was taken at.
- Resizing the window rescales the frame; it does not stretch the layout.

The two Android presets deliberately have **no bottom home-indicator bar** and a thinner
status bar at the top, while the iPhone ones have both. That is real per-device behaviour,
not a bug in the build.

**What to look at**

Início, Praias, Praia (the detail page) and Avistamentos. Especially at **360 wide** — that
is narrower than anything the designs were drawn at, so it is where things are most likely
to break.

**Known and expected — please do not report these**

1. **Mergulho / AR does not work.** A PC has no phone camera, and that screen is still the
   old one, due to be replaced.
2. **The sighting counter and the photo upload fail** — the server rejects desktop builds.
   The form still plays its full, correct path, and the report then lands in *"Seus
   avistamentos pendentes"* as pending. That is the right behaviour when there is no
   connection; it is not a broken screen.
3. **Adding a photo gives you one of three shark pictures**, cycling — hammerhead, nurse
   shark, tiger shark. You cannot use your own images in this build.
4. **"Sobre" looks wrong and spills outside the phone frame.** It is the last old screen,
   being replaced.
5. **The startup splash image also spills outside the frame** — same reason.
6. **SOS, "Saiba mais sobre a espécie" and "Baixar a tábua de maré do mês" do nothing.**
   Not built yet.

**If it crashes**

Send a screenshot — the label in the bezel says which device size you were on — plus this
file: `%USERPROFILE%\AppData\LocalLow\TRN\Mergulho Virtual\Player.log`. Paste that path
straight into the Windows Explorer address bar.

---

**Implementer notes on that list** (do not send these):

- Items 4 and 5 are the same defect: the frame applies to the UI Toolkit panel only, and the
  legacy uGUI `ScreenUI` canvas — splash image and `Sobre` — still paints full-window behind
  it (Step 1b). `Sobre` is additionally a `ScaleWithScreenSize` CanvasScaler at an
  **800×600 landscape** reference, so it is awkwardly scaled on top of spilling. Slice 6
  deletes both; not worth fixing.
- Item 2 is App Check falling through to `DebugAppCheckProviderFactory` with no registered
  desktop token, so the backend 401s. *If you want it to actually upload:* register a
  desktop debug token in the Firebase Console and ship a
  `firebase-appcheck-debug-token.txt` beside the exe — but that is a shared secret in a file
  you are emailing. Not recommended for a design review.
- Items 1, 6 are Slice 4, Slice 5 / Decision D1 and the unhosted DHN PDF, per
  [DESIGN_IMPLEMENTATION.md](../DESIGN_IMPLEMENTATION.md).

---

## Do not

- **Do not commit the Standalone player-settings changes.** Set them inside the build
  script. A committed `fullscreenMode: 3` / `resizableWindow: true` is harmless for
  Android today and a confusing landmine later.
- **Do not change `AppPanelSettings.asset` on disk.** The 160-dpi Constant Physical Size
  calibration is what makes every Figma dp transcribe literally. Override it at runtime
  under the define, or author a separate desktop asset — never edit the shared one.
- **Do not run `make ds-shots` just to check the Windows build's look.** It renders
  through the same panel at the same 390×844, so it already tells you what the desktop
  window will show — the build is for *interaction*, not for appearance.
- **Do not let this become a supported target.** No CI, no `TokenDisciplineTests`
  equivalent, no tests. When Slice 4 lands and there is a device to hand the designer,
  delete the four branches and this doc.
