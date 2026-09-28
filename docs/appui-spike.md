# Phase 0 spike — evaluate Unity App UI (`com.unity.dt.app-ui` 2.2.2)

The package is already added to [Packages/manifest.json](../src/app/MergulhoVirtual/Packages/manifest.json)
(it resolves on next editor open; needs internet once). It is **stable 2.2.2**, not the
pre-release the implementation plan worried about.

**What's being decided:** whether Phase 2 components *internally* wrap App UI controls
or stay fully self-built. Either way the app only ever sees the `Md*` API — the three
components already built (MdButton, MdIconButton, MdChip), the tokens, gallery, and
tests all survive either verdict. If App UI wins, only the internals of those three
small components get swapped.

## Steps (the only part that needs you + a device)

1. Open the project (let the package resolve), then **Window > Package Manager >
   App UI > Samples** → import **"UI Kit"** (and "Navigation" if present).
2. Open the imported sample scene, press Play — first impression on desktop.
3. **File > Build Profiles > Android** → temporarily add the sample scene as scene 0
   → Build & Run on your phone. (Revert the scene list afterwards.)
4. On device, judge — 10 minutes of poking, not a science project:
   - [ ] Scrolling long lists: smooth at the panel's refresh rate, no hitching?
   - [ ] Text sharpness at your device's DPI (small labels especially)?
   - [ ] Touch targets and press feedback feel right?
   - [ ] Startup/memory overhead acceptable (Logcat for warnings/errors)?
   - [ ] Look at a Button/TextField up close: how far is the default look from M3?
     Imagine restyling via USS overrides — straightforward or a fight?

## Decision rule (from the implementation plan)

- **Solid on device AND restyling looks straightforward** → App UI becomes the
  substrate; `Md*` components wrap and restyle its controls.
- **Feels heavy, glitchy on device, or fights the M3 look** → drop the dependency
  (delete the manifest line) and keep the self-built path — which is already
  underway and works regardless.

Report the verdict back and the plan's Phase 2 continues on the chosen substrate.
