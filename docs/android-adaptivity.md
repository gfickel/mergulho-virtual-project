# Android device adaptivity — assessment

**Written 2026-09-28.** For whoever works on this next.

**Question asked:** the app must adapt across a large number of Android resolutions and
aspect ratios — is that currently a problem?

**Short answer: the layouts adapt (verified), landscape is closed, and the catastrophic
DPI case is now closed in code.** What is left is one cosmetic wrap bug at 360 dp, an
optional single-device confirmation of a residual few-percent sizing drift, and a scope
decision about tablets. Nothing remaining on this list can render the app unusable on
hardware nobody here can test.

---

## What was tested

[UiScreenshotHarness](../src/app/MergulhoVirtual/Assets/Editor/UiShots/UiScreenshotHarness.cs)
already parameterizes the frame, so a device matrix needs no new code:

```bash
MV_SHOT_WIDTH=360 MV_SHOT_HEIGHT=640 MV_SHOT_TOP_INSET=24 MV_SHOT_BOTTOM_INSET=0 \
  MV_SHOT_THEMES=light MV_SHOT_DIR="$PWD/.shots/sm-360x640" make ds-shots SHOT=shell
```

Rendered and inspected, light theme, all four shell screens:

| Frame | Insets | Stands for |
|---|---|---|
| 390 × 844 | 47 / 34 | the V2 reference everything was transcribed at |
| **360 × 640** | 24 / 0 | small 16:9 Android — the narrow-and-short stress case |
| 412 × 915 | 24 / 0 | Pixel 7/8 class |

360 dp is the important one: it is the most common Android width and is **30 dp narrower
than anything in the design**.

---

## Finding 1 — the layouts genuinely adapt (verified, no action)

At 360 × 640 every screen reflows correctly. Nothing clips, nothing overflows, there is no
horizontal scroll, and no element is pushed off-frame.

- Home's welcome title rewraps to two lines, the body to five, and the card grows.
- The 2×2 feature grid narrows proportionally (`flex-grow: 1; flex-basis: 0`), it does not
  break to one column — correct, the cells stay square-ish.
- Reportar's species and size chips rewrap from two rows to three.
- All four screens are `AppScrollView`-wrapped, so short viewports just scroll.

This is backed by the source: a sweep of every `.uss` in `Assets/UI/` and
`Assets/DesignSystem/Components/` finds **no layout-scale fixed width**. Every fixed
`width:` is component-scale — 18–96 px icons, avatars, checkboxes, nav destinations, the
104 px media-picker thumb, the 160 px carousel card. Nothing assumes 390.

**One real defect found**, and it is cosmetic: on Praias the "Maré agora" stat value
`1.2 m · subindo` fits one line at 390 dp and wraps at 360 dp, orphaning the middot at the
end of the first line (`1.2 m ·` / `subindo`). Fix in
[PraiasScreen.uss](../src/app/MergulhoVirtual/Assets/UI/Screens/PraiasScreen.uss) — either
`white-space: nowrap` with a step-down type size, or split value and qualifier onto
deliberate lines in the formatter. Worth checking the other stat slots for the same shape.

---

## Finding 2 — rotation ~~is enabled~~ **LOCKED TO PORTRAIT 2026-09-28** (done)

[ProjectSettings.asset](../src/app/MergulhoVirtual/ProjectSettings/ProjectSettings.asset):

```
defaultScreenOrientation: 4          # AutoRotation
allowedAutorotateToPortrait: 1
allowedAutorotateToPortraitUpsideDown: 1
allowedAutorotateToLandscapeRight: 1
allowedAutorotateToLandscapeLeft: 1
```

and the activity in
[AndroidManifest.xml](../src/app/MergulhoVirtual/Assets/Plugins/Android/AndroidManifest.xml)
declares `configChanges="…|orientation|screenSize|screenLayout|smallestScreenSize|…"` with
no `android:screenOrientation`, so it rotates in place without restarting.

**Every V2 frame is portrait.** Rotated to roughly 844 × 390 the fixed-height blocks
dominate: `MvHeroHeader` is 340 dp (or 240 dp compact), Home's conditions card is
`min-height: 234px` — 60–87 % of the viewport height, before any content. It scrolls, so
it is not broken, but it is not designed and it looks it.

### Applied

Landscape was not wanted, so the app is now portrait-locked in
[ProjectSettings.asset](../src/app/MergulhoVirtual/ProjectSettings/ProjectSettings.asset):

```diff
- defaultScreenOrientation: 4            # AutoRotation
+ defaultScreenOrientation: 0            # UIOrientation.Portrait
- allowedAutorotateToLandscapeRight: 1
- allowedAutorotateToLandscapeLeft: 1
+ allowedAutorotateToLandscapeRight: 0
+ allowedAutorotateToLandscapeLeft: 0
```

A fixed `defaultScreenOrientation` makes Unity ignore the `allowedAutorotate*` flags
entirely, so zeroing the two landscape ones is redundant *today* — it is there so the
intent survives anyone flipping the mode back to AutoRotation later.
`allowedAutorotateToPortraitUpsideDown` was deliberately left at 1: it is inert under a
fixed orientation and is not what "no landscape" asked for.

Applies to iOS as well — it is a cross-platform Player setting, not an Android one.

**The AR rotation code was deliberately NOT touched.**
`CameraFeedToInference.GetUprightCameraTexture` branches on `Screen.orientation`, and
CLAUDE.md records that convention as empirically derived and fragile ("portrait needs
270°, not 90°"). Locking portrait means it now always takes the portrait branch — the one
measured at 98 % in the rotation sweep — so this *reduces* risk there. Leave the branching
in place; deleting it is a separate change with its own device sweep.

**Worth confirming on the next device build** (cheap, no new hardware): rotate the phone
and check that the app stays portrait, and that the AR camera feed in Mergulho is still
upright.

---

## Finding 3 — the sizing policy: catastrophic case fixed, residual drift unverified (mitigated)

This is what the screenshots **cannot** tell you. They prove the layout is correct *at a
given dp size*. They do not prove what dp size a real Android device produces.

Both PanelSettings use **Constant Physical Size** at `referenceDpi: 160`. So:

```
panel width in USS units = Screen.width × 160 / Screen.dpi
Android dp               = Screen.width × 160 / densityDpi
```

These are equal **only if `Screen.dpi == densityDpi`**, and Unity does not guarantee that
on Android. That was two failure modes. One is now closed in code; the other is real but
much smaller than it first looked.

### Failure mode 1 — `Screen.dpi` returns 0 — **neutralised 2026-09-28**

Unity's own docs say `Screen.dpi` "returns 0 if the device / platform does not provide DPI
information". `fallbackDpi` is consulted **only** in that case. It was 160 — identical to
`referenceDpi` — so the fallback scale was exactly 1 and the panel would have been as many
USS units wide as the device has **physical pixels**: 1080 USS units on a 1080p phone, the
whole UI at roughly a third size, on that device alone. The worst kind of field bug:
silent, total, and hardware-specific.

[AppUiBuilder.cs](../src/app/MergulhoVirtual/Assets/Editor/AppUiBuilder.cs)
`CreateOrUpdateAppPanelSettings()` now sets:

```diff
- settings.fallbackDpi = 160;   // == referenceDpi → fallback scale 1 → USS units == pixels
+ settings.fallbackDpi = 440;   // xxhdpi; consulted only when Screen.dpi is 0/invalid
```

`make ui-setup` was run, and
[AppPanelSettings.asset](../src/app/MergulhoVirtual/Assets/UI/AppPanelSettings.asset) now
carries `m_FallbackDpi: 440`. `m_ScaleMode: 1` (ConstantPhysicalSize) and
`m_ReferenceDpi: 160` are **unchanged** — the sizing *policy* is exactly what it was, only
the no-information fallback moved. 440 is a standard xxhdpi Android density, and the
arithmetic lands where the design already lives:

```
1080 px × 160 / 440 = 393 USS units        (the reference frame is 390)
```

So a device that reports no DPI now renders within 1 % of the reference width instead of
at a third scale. **This needed no hardware to fix and needs none to verify** — it is
arithmetic over a constant, and the constant is in the asset.

### Failure mode 2 — physical DPI rather than bucketed `densityDpi` (remains, low severity)

If `Screen.dpi` returns the panel's true physical DPI rather than Android's bucketed
`densityDpi`, two phones that Android considers identical (same dp, same bucket) get
different USS widths — 1080 px at 405 dpi → 427 USS, at 395 dpi → 437, where Android calls
both 393 dp.

**Reframe this honestly: it is a fidelity question, not a breakage question.** Finding 1
exercised the layouts across 360–412 dp — a 52 dp spread, far wider than this effect
produces — with nothing clipping, nothing overflowing and no horizontal scroll. A few
percent of width drift is absorbed by the very same reflow that already works. What it
actually costs is exactness: **390 is never precisely what you get**, and content-per-screen
varies a little more than the design assumed. That is not worth changing the design
contract over.

### The one-device check (cheap, opportunistic — no longer step 1)

The readout is still worth taking, but it is now a confirmation, not a gate. Add to
`AppUiHost.OnEnable`, behind a debug flag, and read it in Android Logcat on the next device
build you were making anyway:

```csharp
var root = document.rootVisualElement;
root.RegisterCallback<GeometryChangedEvent>(_ => Debug.Log(
    $"[Adapt] px={Screen.width}x{Screen.height} dpi={Screen.dpi} " +
    $"uss={root.resolvedStyle.width:F1}x{root.resolvedStyle.height:F1} " +
    $"orient={Screen.orientation} safe={Screen.safeArea}"));
```

**One phone is enough for most of this, and one phone is what there is.** The open question
is a *mechanism* question — does Unity's Android backend hand you the bucketed `densityDpi`
or the physical DPI — and a mechanism does not vary by handset. So on the single available
device, comparing the logged `dpi` against that device's known density bucket, and `uss`
against its known Android dp width, largely settles it:

| Reading on the one device | Meaning | Action |
|---|---|---|
| `uss` ≈ the device's real Android dp width, `dpi` ≈ its bucket (160/240/320/480…) | Unity reports bucketed density; Constant Physical Size behaves | nothing — the policy is sound, and it is sound everywhere |
| `uss` close to dp but off by a few %, `dpi` a non-bucket number | physical-DPI behaviour | accept it — the layout is fluid (Finding 1). Only revisit if pixel-exact transcription becomes a requirement |

What one device **cannot** settle is the tail risk that some *other* handset returns
`Screen.dpi == 0`. That is fine: that case is exactly the one `fallbackDpi: 440` already
absorbs, so it no longer needs observing. Also fold in the Finding 2 confirmation on the
same build — rotate the phone, check the app stays portrait and the AR feed stays upright.

### The alternative, documented but **not currently needed**

`ScaleWithScreenSize` with reference `390 × 844` and `match: 0` (width) makes panel width
exactly 390 USS units on *every* device, height varying by aspect. Fully deterministic, the
design transcribes exactly, at the cost of the UI being physically larger on a big phone.
It is also a **different design contract** from what the design system documents today
("1 USS px = 1 dp = 1 Figma pt"), so it would be a decision, not a patch. With failure mode
1 closed and failure mode 2 demoted to cosmetic, there is no reason to reach for it.

---

## Finding 4 — tablets and foldables stretch unbounded (verified, decide)

There is **no `max-width` on any screen content** — the only `max-width` rules in the whole
tree are `MdDialog` 560, `MdSnackbar` 600, `MdBottomSheet` 640 and `MvOptionCard: 100%`.
At 600–840 dp (tablet, unfolded foldable) the conditions card's label/value rows and the
welcome body stretch to absurd line lengths, and the 2×2 grid becomes four wide short
tiles.

Two options: cap the scroll content at ~480 dp and centre it (one rule per screen root), or
declare phones-only. Worth noting `androidSupportedAspectRatio: 1` with
`androidMaxAspectRatio: 2.4` is also set; with `AndroidTargetSdkVersion: 0` (auto → recent)
Android largely ignores max-aspect anyway, so this is low priority — but confirm against the
**merged** manifest in a built AAB rather than the source manifest above, which does not
contain it.

---

## Suggested order

**Already done, neither of which needed hardware:** Finding 2 (portrait-locked in
ProjectSettings) and the urgent half of Finding 3 (`fallbackDpi` 160 → 440, verified in
`AppPanelSettings.asset`). What is left:

1. **Fix the 360 dp middot wrap** (Finding 1) — now the top actionable item. One rule in
   [PraiasScreen.uss](../src/app/MergulhoVirtual/Assets/UI/Screens/PraiasScreen.uss), plus a
   pass over the other stat slots for the same shape; then re-run the matrix.
2. **Confirm Finding 3's residual on the existing device** — opportunistic, not blocking.
   Drop the `[Adapt]` log into whatever device build happens next and read it once; it
   answers "bucketed or physical" and changes nothing if it comes back bucketed. Check the
   portrait lock and the AR feed orientation on the same build.
3. **Decide Finding 4** (tablet cap) — still a scope decision, not a bug. Only if tablets
   are in scope.

Re-run the matrix after each change; `MV_SHOT_DIR` keeps the sets side by side, and each
carries a `manifest.json` recording frame, scale and insets.

**Cost note:** `ds-shots` re-renders glyphs and can churn the LFS-tracked font assets under
`Assets/DesignSystem/Fonts/` — check `git status` after and `git checkout` them if the diff
is noise. It also needs a real X display (`DISPLAY=:1` by default) and the editor closed.
