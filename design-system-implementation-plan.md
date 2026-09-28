# Mergulho Virtual — Design System Implementation Plan

**Goal:** Replace the debug UGUI screens with a Material Design 3–styled UI built on UI Toolkit, where every component is developed, tested, and debugged in isolation before it ever touches the app, AR, or Firebase code.

The word you were looking for is exactly the right instinct: **isolation**, enforced at three levels — compile-time (assembly definitions), runtime (a component gallery), and test-time (per-component test suites). A component that cannot reference AR or Firebase code cannot be broken by them, and a bug reproduced in the gallery is a bug fixed without launching an AR session.

---

## Architecture: layers and enforced boundaries

```
┌─────────────────────────────────────────────────────────┐
│ Assembly-CSharp (existing code, unchanged)              │
│   AR/, Services/, Firebase, SeaDetector, ScreenManager  │
│   → CAN reference the new assemblies below              │
├─────────────────────────────────────────────────────────┤
│ MergulhoVirtual.UI.asmdef            (screens + VMs)    │
│   depends on: DesignSystem + service *interfaces*       │
├─────────────────────────────────────────────────────────┤
│ MergulhoVirtual.DesignSystem.Gallery.asmdef             │
│   depends on: DesignSystem only                         │
├─────────────────────────────────────────────────────────┤
│ MergulhoVirtual.DesignSystem.asmdef  (the component lib)│
│   depends on: UnityEngine.UIElements + TextCore ONLY    │
│   → CANNOT reference Assembly-CSharp, AR, or Firebase   │
└─────────────────────────────────────────────────────────┘
```

The key trick: code inside an `.asmdef` **physically cannot** reference `Assembly-CSharp` (Unity's dependency graph only flows the other way). You don't need to migrate any existing code — just placing the design system in its own assembly makes it impossible for a component to sneak a dependency on `ConditionsService` or AR Foundation. If someone tries, it's a compile error, not a code-review argument.

Proposed folder layout:

```
Assets/
  DesignSystem/
    MergulhoVirtual.DesignSystem.asmdef
    Tokens/          (_colors.uss, _typography.uss, _shape.uss,
                      _elevation.uss, _motion.uss, Theme-Light.tss, Theme-Dark.tss)
    Fonts/           (Roboto font assets, MaterialSymbols font asset)
    Components/
      MdButton/      (MdButton.cs, MdButton.uss)
      MdChip/        ...one folder per component...
    Tests/
      MergulhoVirtual.DesignSystem.Tests.asmdef  (EditMode + PlayMode)
    Gallery/
      MergulhoVirtual.DesignSystem.Gallery.asmdef
      GalleryScene.unity, Gallery.uxml, GalleryController.cs
  UI/
    MergulhoVirtual.UI.asmdef
    Screens/         (AnimalsScreen.uxml/.cs, BeachesScreen, RegisterScreen, ConditionsScreen)
    ViewModels/      (plain C# classes, no UnityEngine.UIElements references)
    Interfaces/      (IConditionsService, ITideService, IGeocoding, IAnimalRepository)
```

### Component contract (the isolation rules)

Every component in `DesignSystem/Components` follows the same contract:

1. It is a **custom control** — a `[UxmlElement]` partial class extending `VisualElement`, with `[UxmlAttribute]` properties for configuration (Unity 6 syntax; no UxmlFactory boilerplate).
2. **Data in through properties, events out through callbacks** (`clicked`, `valueChanged`, etc.). No singletons, no `FindObjectOfType`, no service calls, no scene assumptions.
3. Styling lives in the component's own `.uss` file using BEM-style classes (`md-button`, `md-button--filled`, `md-button--disabled`) and **only token variables** (`var(--md-sys-color-primary)`) — never hard-coded colors or sizes.
4. Each component ships with: the C# class, the USS file, a gallery entry showing every variant/state, and a test file. A component without all four is not done.

---

## Phase 0 — Spike and decision (1–2 days)

Before committing, timebox an evaluation of Unity **App UI** (`com.unity.dt.app-ui`): install it, run its samples on a real Android device and (if available) iOS, and judge two things — whether it renders and performs well on mobile, and how far its default look can be pushed toward M3 via USS overrides.

**Decision rule:** if App UI feels solid on device *and* restyling is straightforward, use it as the component substrate and this plan's Phase 2 becomes "wrap and restyle App UI components behind our own `Md*` API" (your code only ever sees `MdButton`, so App UI stays swappable). If it feels heavy, pre-release-flaky, or fights the M3 look, build the components yourself — for the ~12 components this app needs, that's honestly not much more work than deep-restyling someone else's. **The rest of this plan is written for the self-built path and holds either way.**

Also in Phase 0: generate the color scheme. Open Material Theme Builder, pick a seed color (a Noronha ocean blue/turquoise), export the light+dark schemes, and keep the export file in the repo — it becomes `_colors.uss`.

## Phase 1 — Foundations (2–3 days)

Everything components will depend on, and nothing else.

1. **Assemblies & folders** as diagrammed above. Verify the compile-time firewall works by trying (and failing) to reference `ConditionsService` from inside `DesignSystem`.
2. **PanelSettings tuned to dp.** Configure scaling (Constant Physical Size / reference DPI) so that **1 USS px ≈ 1 dp**. From then on you transcribe M3 spec values literally: FAB = `width: 56px`, nav bar = `height: 80px`. Verify with a ruler-test UXML on two devices with different densities.
3. **Design tokens as USS variables.** Transcribe the Theme Builder export into `_colors.uss` (`--md-sys-color-primary`, `--md-sys-color-surface-container`, all the on-colors); define `_shape.uss` (corner radii 4/8/12/16/28/full), `_elevation.uss`, `_motion.uss` (durations/easings), and `_typography.uss` (the M3 type scale: display/headline/title/body/label sizes and line heights as utility classes like `.md-typescale-title-large`). Bundle into `Theme-Light.tss` and `Theme-Dark.tss`.
4. **Fonts.** Your existing TMP assets don't carry over — UI Toolkit uses TextCore font assets. Generate them from Roboto (Regular/Medium/Bold) and from the Material Symbols `.ttf` (google/material-design-icons) for the icon set. Add an `MdIcon` helper element that maps icon names to glyphs.
5. **Two tiny utilities:** a `SafeAreaElement` that pads by `Screen.safeArea` (UI Toolkit doesn't do this automatically, and an AR app is edge-to-edge), and a `WorldAnchor` helper wrapping `RuntimePanelUtils.CameraTransformWorldToPanel` for pinning UI to 3D positions (used in Phase 4).

**Exit criterion:** a UXML page of colored boxes and text styles renders correctly in light and dark themes on a physical Android device, with correct physical sizes and safe-area padding.

## Phase 2 — Component library, gallery, and tests (~2 weeks, iterative)

Build components leaf-first, one at a time, each fully finished (code + USS + gallery + tests) before starting the next. Suggested order, mapped to what your app actually needs:

| # | Component | Replaces / used by | Notes |
|---|-----------|-------------------|-------|
| 1 | `MdButton` (filled, tonal, outlined, text) | everywhere | Establishes the state-layer pattern all others copy |
| 2 | `MdIconButton`, `MdFab` | AR photo capture | |
| 3 | `MdChip` (assist + filter) | `ConditionsPillView` | |
| 4 | `MdProgressIndicator` (circular + linear) | loading states for Firebase/tide fetches | |
| 5 | `MdCard` (elevated, filled, outlined) | `ConditionsCardView` | Container only; content is slotted children |
| 6 | `MdListItem` | `ListItemView` (animals/beaches lists) | 1/2/3-line variants, leading image, trailing icon |
| 7 | `MdTextField` | Register screen | Label float, error state, supporting text |
| 8 | `MdMenu` / `MdDropdown` | `BeachSelectorDropdown` | Needs an overlay layer in the panel root |
| 9 | `MdTopAppBar` | screen headers | Collapses with scroll later; static first |
| 10 | `MdNavigationBar` | `ScreenManager` tab switching | 3–4 destinations |
| 11 | `MdDialog`, `MdSnackbar` | confirmations, errors | Share the overlay layer with MdMenu |
| 12 | `MdBottomSheet` | AR shark info (today's `infoText`) | Drag-to-dismiss can come later |
| 13 | `MdSparkline` | `TideSparkline` | Not an M3 component — custom `Painter2D` drawing, but token-colored |

### The Gallery (your main debugging weapon)

A standalone scene, `GalleryScene.unity`, that is a scrollable catalog: every component, every variant, every state (enabled/disabled/pressed/error), with a **light/dark toggle** and a **font-scale toggle** in the corner. Think Storybook, but in Unity. It runs in the Editor for instant iteration and gets included in debug builds so you can poke every component on-device — where the real bugs (touch targets, text sharpness, DPI) live — without navigating the app or starting an AR session. When you find a UI bug in the app later, the first move is always "reproduce it in the gallery"; if it reproduces, the fix is a pure component fix with a regression test; if it doesn't, the bug is in the screen/wiring layer by elimination.

### Test strategy per component

- **EditMode tests (fast, majority):** construct the element in code and assert structure and logic — correct child hierarchy, USS classes toggling when properties change (`variant = Filled` → has `md-button--filled`), value clamping, disabled state blocking callbacks. No scene needed.
- **PlayMode tests (interaction):** a minimal test scene with a `UIDocument`; synthesize input by sending pooled pointer events (`PointerDownEvent.GetPooled(...)` → `element.SendEvent(evt)`) and assert callbacks fire, state layers apply, and the event doesn't leak to elements underneath. This is where you'll catch the picking-mode class of bugs *before* AR integration.
- **ViewModel tests (Phase 3):** plain C# with fake `IConditionsService`/`ITideService` implementations — no Unity runtime at all.
- Optional, later: golden-screenshot comparison of gallery pages for visual regressions. Don't start here; add it if visual churn becomes a problem.

**Definition of done per component:** renders all variants in gallery (both themes, on device) · EditMode tests green · PlayMode interaction tests green · no compile-time deps outside `UnityEngine.UIElements` · touch targets ≥ 48 dp · USS uses only token variables.

## Phase 3 — Screen assembly (≈1 week)

Now, and only now, compose screens from finished components.

1. **Interfaces first.** Extract `IConditionsService`, `ITideService`, `IAnimalRepository`, etc. in `MergulhoVirtual.UI/Interfaces`, and have the existing service classes in `Assembly-CSharp` implement them (legal in that direction). A small composition-root MonoBehaviour in the scene injects real implementations.
2. **ViewModels** as plain C# (`AnimalsViewModel`, `BeachConditionsViewModel`, `RegisterViewModel`): hold state, expose change events, call services. Fully unit-testable with fakes.
3. **Screens** as UXML documents + thin code-behind that binds ViewModel ⇄ components. A simple `MdRouter` (show/hide screen roots, back-stack) replaces `ScreenManager`, driven by `MdNavigationBar`.
4. **Strangler pattern:** keep the old UGUI debug canvas in the scene behind a build-symbol or debug toggle until each new screen reaches parity, then delete it screen by screen. Never a big-bang cutover.

## Phase 4 — AR integration (2–3 days)

The plan's only genuinely risky seam, kept deliberately small and last:

1. **Picking-mode audit.** Every layout-only container gets `picking-mode: ignore`. Add a PlayMode test that sends a pointer event at screen-center over an "AR screen" layout and asserts no VisualElement picks it.
2. **Input guard.** Add the `EventSystem.current.IsPointerOverGameObject()` check to `ObjectInteraction.HandleInput`, and confirm the Input System UI module + auto-attached `PanelRaycaster` cover the runtime panel. Test both directions on device: taps on UI must not raycast sharks; taps on camera background must.
3. **Shark info via `MdBottomSheet`,** replacing the `infoText` GameObject; position/summon it from the existing SphereCast hit.
4. **World-pinned labels** (species name above a shark) via the Phase 1 `WorldAnchor` helper — screen-space element following a world position. Avoid world-space UI Toolkit panels for v1.
5. **Photo capture check:** confirm whether captures should include UI (then `ScreenCapture`) or not (then camera render output — the overlay is excluded automatically).

## Phase 5 — Hardening (3–4 days + ongoing)

Device matrix pass (small/large, notched, 60/120 Hz, low-end Android — text sharpness and dynamic-atlas settings are the usual suspects) · full dark-theme sweep in the gallery · pt-BR/en string pass · profile UI cost during an active AR session with inference running (expect noise-level; verify) · delete the last debug UGUI remnants · optional: wire EditMode+PlayMode tests into GitHub Actions via GameCI so the component suite runs on every push.

---

## Risks and honest caveats

- **UI Toolkit runtime maturity:** excellent for exactly this app shape (screens, lists, overlays), but expect device-specific text/atlas tuning. The gallery-on-device habit is your early-warning system.
- **If Phase 0 picks App UI:** it's partly pre-release; the `Md*` wrapper layer is your insurance — the app never depends on App UI types directly.
- **USS has no mixins:** shared patterns (state layers, elevation) live in utility classes applied in C# constructors, or you accept some duplication. Decide the convention in component #1 (`MdButton`) and copy it everywhere.
- **Scope discipline:** M3 is enormous; this app needs ~13 components. Resist building components no screen uses.

## Rough timeline

Phase 0: 1–2 days → Phase 1: 2–3 days → Phase 2: ~2 weeks → Phase 3: ~1 week → Phase 4: 2–3 days → Phase 5: 3–4 days. **Total: 4–5 weeks** solo at a steady pace, with a working, tested component gallery existing from roughly day 5 onward.
