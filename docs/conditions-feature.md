# Beach Conditions Feature — Implementation Roadmap

Real-time-ish swim-safety conditions surfaced on MainScreen (glanceable pill) and the beach detail page (full card). Designed **offline-first** because the device may have spotty or no internet at Fernando de Noronha.

## Status legend

- [x] done
- [ ] not started
- [~] in progress

---

## Done so far

- [x] **Step 1** — MainScreen header restructured. New `TopBar` under `MainScreen` with `BeachName` (TMP_Text, left) and `ConditionsPill` (placeholder Image, right). Existing debug HUD (`Header Text`, `Footer Text`, `FPS_Text`) untouched, still under `Panel`.
- [x] **Step 2** — `BeachNameView.cs` written and wired. Subscribes to `GPSHandler.PlaceChanged`; updates the TopBar's BeachName text. Empty-string fallback when GPS hasn't resolved a beach yet.
- [x] **Step 3.1** — `tools/generate_tides.py` written and run. Outputs `Assets/Resources/tides_noronha.json` (8784 hourly samples, ~64 KB, 366-day window). Uses `pytides2` when installed for real astronomical phasing; falls back to a stdlib sum-of-cosines synthesis (correct shape/range, arbitrary phase) for development. **Before any production deployment, install pytides2 and re-run, otherwise tide TIMES will not match reality.**
- [x] **Step 3.2** — `TideService.cs` written. Loads `Resources/tides_noronha`, polls every 60 s in `Update`, emits `TideChanged` on hour-boundary transitions. `next24hHeights` always populated; next-high/low scanned ahead via local-extremum sweep. Editor-only `[ContextMenu]` "Advance 1h" hook for sparkline cursor verification per Step 7.3.
- [x] **Step 4.1** — `ConditionsTypes.cs` written. `ConditionsSnapshot` is `[Serializable]` and JsonUtility-friendly: paired `bool hasX + float x` fields with nullable accessors. `IsStale` computed from `fetchedAtTicksUtc` (>4h).
- [x] **Step 4.2 / 4.4** — `ConditionsService.cs` written. Subscribes to `GPSHandler.PlaceChanged`, loads cache from `Application.persistentDataPath/conditions_<sanitized>.json`, then hits Marine + Forecast APIs in parallel. Partial-success merging (preserves prior cached fields the latest fetch didn't fill). Refresh loop every 30 min while same beach is active; abandons in-flight requests on beach change.
- [x] **Step 4.3** — `ReverseGeocoding.GetCentroid(string) → Vector2?` added. Returns the polygon-vertex average (lon→x, lat→y).
- [x] **Step 5** — `MoonPhase.cs` written. Static utility, no MonoBehaviour. `Phase` / `Illumination` / `Name` against a J2000-anchored synodic-month sinusoid. EditMode tests cover reference epoch, half-synodic (full), real 2024 new/full moons (1-day tolerance), and `Name` quadrant boundaries.
- [x] **Step 6.2 (code)** — `ConditionsPillView.cs` written. Subscribes to `ConditionsService.ConditionsChanged` and `TideService.TideChanged`, computes moon at render time from `DateTime.UtcNow`, and renders into a single `TMP_Text` as `"1.8 m  ·  Cheia  ·  ↑ subindo"`. Tap handler deep-links to the current beach's detail via `BeachesScreenController.ShowDetailFor(name)` then `ScreenManager.ShowBeaches()`. Also added a public `ShowDetailFor(string)` method on `BeachesScreenController` with pending-name handling so the detail opens immediately on the next `OnEnable` activation. **Deferred from the original plan**: the 3-segment image+text+divider layout and the 8 moon glyph sprites (Step 6.1, 6.3) — single-text rendering is sufficient v1 and avoids 8 new image assets. Revisit when the data is solid in the field.
- [x] **Step 7.1 (code)** — `TideSparkline.cs` written. `[RequireComponent(typeof(RawImage))]`, allocates a 192×48 RGBA32 `Texture2D` with bilinear filtering (so it scales gracefully when the parent VerticalLayoutGroup stretches it past native dims), subscribes to `TideService.TideChanged`, and re-renders pixels on each event: column-by-column lerp of `next24hHeights`, fill below the curve in semi-transparent cyan, brighter line on top, and a 2 px white "now" cursor at the left edge. Texture is `Destroy`'d in `OnDestroy` to avoid leaks.
- [x] **Step 8.1 (code)** — `ConditionsCardView.cs` written. Subscribes to `ConditionsService.ConditionsChanged`, `TideService.TideChanged`, and a 60 s `Update` tick that re-renders the freshness line. Renders all five rows (Onda / Maré / Lua / Vento / Água) plus a dimmed `Atualizado` line into a single `TMP_Text` using TMP rich text — `<b>` for label, `<pos=88px>` to align the value column, `<alpha=#80>` to dim the freshness footer. Null-safe: any missing nullable field renders as `—`; tide and moon are always available; freshness shows `Atualizado: agora / há Xm / há Xh / há Xd`. **Deferred from the original plan**: the per-row HorizontalLayoutGroup structure (Step 8 layout block) — single-text rendering with TMP `<pos>` is sufficient v1 and avoids 12 nested children.

---

## Wiring next (user, in Unity Editor)

### Step 6 — `ConditionsPill` button + label + view

Hierarchy structure today (verified against `MainScene.unity`):
```
ScreenUI (Canvas)
└── MainScreen
    └── TopBar
        ├── BeachName    (TMP_Text, left)
        └── ConditionsPill   ← we extend this
            (no children yet, only an Image @ alpha 0.6)
```

Add a child label, make the pill clickable, and attach the view.

#### 1. Add a TMP_Text child for the label

1. Hierarchy → right-click `ConditionsPill` → **UI → Text - TextMeshPro**. Unity creates a child named `Text (TMP)`. Rename it to `ConditionsLabel`.
2. Select `ConditionsLabel` → in the Inspector, Rect Transform anchor preset → click the cross-hairs icon → top-left of the picker, **stretch / stretch** (full-stretch). Set Left/Right/Top/Bottom = 8/8/4/4.
3. TextMeshPro - Text component:
   - Font Size: `18` (or whatever fits — start small, the line is moderately long).
   - Alignment: **Center** horizontal, **Middle** vertical.
   - Color: white (`R 255 G 255 B 255 A 255`).
   - Text input box: leave empty — the view will set it at runtime.
   - **Extra Settings → Margins**: `0 0 0 0` (the memory rule about TMP margins applies here — if the text appears clipped after wiring, suspect this first).
4. ⚠ Note: `ConditionsPill` is a leaf inside `TopBar`. If `TopBar` has a `HorizontalLayoutGroup`, the pill's RectTransform width is layout-controlled — that's fine, it doesn't propagate to this child. The full-stretch anchors here are stretching inside `ConditionsPill`, not inside `TopBar`.

#### 2. Make `ConditionsPill` itself a button

1. Select `ConditionsPill` → Inspector → **Add Component** → type `Button` → enter.
2. The new Button component asks for a `Target Graphic`. Drag `ConditionsPill` (itself, with its existing Image) into the Target Graphic slot, OR click the small ⊙ picker → select the existing Image on this same GameObject.
3. Confirm `ConditionsPill`'s Image component still has `Raycast Target = ✓` (default true). Without it, Button presses won't register.

#### 3. Add the view component and wire its 6 slots

1. Select `ConditionsPill` → Inspector → **Add Component** → type `Conditions Pill View` → enter.
2. Wire fields by dragging from the Hierarchy:

   | Inspector field | Drag from Hierarchy |
   |---|---|
   | `Label` | `ConditionsLabel` (the TMP_Text child you just created) |
   | `Conditions` | `ConditionsServices` (root GameObject) |
   | `Tides` | `TideServices` (root GameObject) |
   | `Gps` | `LocationServices` (root GameObject) |
   | `Screen Manager` | `ScreenManager` (root GameObject) |
   | `Beaches Controller` | `BeachesScreen` (root GameObject — wherever you have `BeachesScreenController` attached) |

   For typed-MonoBehaviour fields like `Tides` / `Gps` / `Screen Manager` / `Beaches Controller`, dragging the parent GameObject is fine — Unity resolves the component automatically.

3. Save (Ctrl+S).

#### 4. Verify

1. Press Play. Open the BottomNav's **Beaches** screen and pick any beach in the dropdown to seed the GPS-resolved place.
2. Switch back to AR (BottomNav → AR). The pill should read something like:
   - `1.8 m  ·  Cheia  ·  ↑ subindo` if Marine API succeeded **and** the tide JSON is loaded.
   - `—  ·  Cheia  ·  ↑ subindo` if there's no internet (wave segment falls back to em-dash; tide+moon still render).
   - `—  ·  <something>  ·  —` if both `tides_noronha.json` is missing AND no network. Re-run `python3 tools/generate_tides.py` if the tide segment is em-dashed at startup.
3. Tap the pill → app should jump straight to the beach detail page for the currently-selected beach.
4. If the pill text is empty, check the Console for warnings — most likely `Label` is unwired or `gameObject.activeInHierarchy` was false at first frame.

The detail-card UI (Step 8) is still TODO. Once the sparkline renders, ping me to start Step 8.

### Step 7 — Tide sparkline on `BeachesDetail`

`BeachesDetail` is a `VerticalLayoutGroup` (Control Child Size W+H, ForceExpand W on). Add a `RawImage` child for the sparkline with a `LayoutElement` that pins its row height to 48 px. Width is left to the layout — the texture filters bilinearly so a wider row renders fine.

#### 1. Add the RawImage

1. Hierarchy → `ScreenUI → BeachesScreen → BeachesDetail` (it's currently inactive in the scene; click ▶ to expand). Right-click `BeachesDetail` → **UI → Raw Image**. Unity creates a child named `RawImage`.
2. Rename it to `TideSparkline` (F2).
3. By default the new RawImage is full-stretched and showing a white square. With the parent VLG it'll be repositioned automatically once we add the LayoutElement.

#### 2. Pin its height with a LayoutElement

1. Select `TideSparkline` → Inspector → **Add Component** → type `Layout Element` → enter.
2. In the new `Layout Element` component, tick **Preferred Height** and set the value to `48`. Leave everything else unchecked (Min/Preferred Width, Flexible W/H all default).
3. The sparkline now lays out as a thin row, full-width inside the detail card.

#### 3. Attach the script and wire `Tides`

1. Select `TideSparkline` → Inspector → **Add Component** → type `Tide Sparkline` → enter.
2. The component shows one wired field (`Raw Image` is auto-resolved by `[RequireComponent]`) and several `[SerializeField]`s. Drag `TideServices` (scene-root GameObject) onto the **Tides** field.
3. Leave the four color slots at their defaults (cyan-ish fill, white cursor, transparent background). They're tweakable later.

#### 4. Verify

1. Save (Ctrl+S). Press Play.
2. Tap **Beaches** in the BottomNav, pick any beach in the dropdown, then tap a beach in the list to open the detail page. The sparkline should appear as a wave-shaped fill with a white cursor at the left edge.
3. Test the cursor advance: while still in Play mode, find `TideServices` in the Hierarchy → in the Inspector, click the kebab (⋮) menu next to the `Tide Service` component header → **Advance 1h (editor test)**. The curve content should shift one step (you'll see the leftmost value drop and the rightmost extend). The cursor stays at column 0 — the curve is what scrolls.
4. If the sparkline area is empty / transparent, check the Console for `TideService:` warnings (most likely cause: `tides_noronha.json` missing → run `python3 tools/generate_tides.py` again).

### Step 8 — Conditions card on `BeachesDetail`

A single `TMP_Text` child of `BeachesDetail`, rendered by `ConditionsCardView`. The view does the layout via TMP rich text (no nested layout groups needed).

#### 1. Add the card text

1. Hierarchy → `ScreenUI → BeachesScreen → BeachesDetail` (expand it).
2. **Right-click** `BeachesDetail` → **UI → Text - TextMeshPro**. A child named `Text (TMP)` appears.
3. Press **F2**, rename to `ConditionsCard`.
4. **Drag-reorder** it inside `BeachesDetail` so it sits where you want — typically just below `DescriptionText`. (Order it as: BannerImage → Name → DescriptionText → ConditionsCard → TideSparkline → BackButton, but adjust to whatever your existing layout uses.)
5. Inspector → `TextMeshPro - Text (UI)`:
   - **Text Input** box: clear it (the script will fill it).
   - **Font Size**: `16`.
   - **Alignment**: top-left (top icon in vertical row, left icon in horizontal row).
   - **Color**: white.
   - **Extra Settings → Margins**: `0 0 0 0`.
6. The `BeachesDetail` parent has `VerticalLayoutGroup` with `Control Child Size: Width+Height = on`, so the TMP_Text auto-sizes to its content. No `LayoutElement` needed.

#### 2. Attach the script and wire 2 fields

1. With `ConditionsCard` selected → **Add Component** → type `Conditions Card View` → enter.
2. Wire the two scene-root references:

   | Inspector field | Drag from Hierarchy |
   |---|---|
   | `Label` | leave empty — `Awake` auto-resolves it from the same GameObject's `TMP_Text` |
   | `Conditions` | `ConditionsServices` (scene root) |
   | `Tides` | `TideServices` (scene root) |

   (You *can* drag `ConditionsCard` itself onto `Label` if you want it explicit. Both work.)

3. Leave `Value Column Px` at the default `88`. Bump it if a label name overflows into the value column.

4. **Ctrl+S**.

#### 3. Verify

1. Press Play → tap **Beaches** → pick a beach in the dropdown → tap a beach in the list to open the detail page.
2. The card should show 5 left-aligned bold labels with right-aligned values, plus a dimmer `Atualizado:` line below:
   ```
   Onda    1.8 m · 11 s · SW
   Maré    subindo, próxima alta 14:32 (1.4 m)
   Lua     Cheia · 98% iluminada
   Vento   15 km/h E
   Água    27 °C

   Atualizado: agora
   ```
   Offline state: weather rows show `—`, tide and moon still render, freshness shows `Atualizado: —`.
3. Test the freshness ticker: leave the detail page open for ~1 minute. The `Atualizado:` line should advance from `agora` → `há 1m` (the view ticks once a minute via `Update`).
4. Test the live tide hour-tick: with `TideServices` selected, kebab menu → **Advance 1h (editor test)**. The `Maré` row's `próxima alta XX:XX` time should update.

#### 4. Common breakages

- **Card text is empty** → `Conditions` and `Tides` weren't wired. The view renders even when both are null (you'd see `—` everywhere except moon). Empty text means the `Label` reference is missing — confirm the `TMP_Text` is on the same GameObject so `Awake` can auto-find it.
- **Label and value columns overlap** → `Value Column Px` is too small. Bump it from 88 to 96 or 100.
- **Freshness shows `Atualizado: há 99999d`** → `fetchedAtTicksUtc` is `0`. That means no successful network fetch yet AND no cache. Expected on first launch with no internet.
- **Console: `Tag <pos> not found`** → wrong TMP version. Should not happen in this project; if it does, replace `<pos=88px>` with a tab + manual spacing in `ConditionsCardView.Row`.

That completes the v1 conditions feature: services, pill, sparkline, and detail card. Step 9 (per-beach risk rules) is intentionally deferred until the raw values are field-validated.

---

## Architecture

Each data source is a single-responsibility scene-root MonoBehaviour, mirroring the existing pattern (`LocationServices`, `ComputerVisionServices`, `BackendServices`):

```
Scene root
├── LocationServices       → GPSHandler            (existing)
├── ComputerVisionServices → CameraFeedToInference (existing)
├── BackendServices        → BackendServices       (existing — sighting count)
├── TideServices           → TideService           (NEW — bundled JSON, always available)
├── ConditionsServices     → ConditionsService     (NEW — Open-Meteo + cache)
└── (no GameObject needed) → MoonPhase             (NEW — static utility class)
```

### Offline-first contract

Every service must:

1. **Render with whatever data it has.** Never block UI on a missing field.
2. **Persist to disk** (`Application.persistentDataPath`) any data fetched from the network — survives app restart and offline launches.
3. **Expose freshness** (timestamp of last successful fetch) so consumers can show a "stale" indicator.
4. **Refresh on `PlaceChanged` and every 30 min** while online; silently no-op when offline.

### Pub/sub pattern

Each service exposes `Current<X>` (property) + `<X>Changed` (event), same as `GPSHandler.CurrentPlaceName` / `PlaceChanged`. UI views subscribe in `OnEnable`, unsubscribe in `OnDisable`, and seed from `Current<X>` to handle late subscription.

### Data availability matrix

| Source | Offline-first guarantee | Notes |
|---|---|---|
| Beach name | ✓ Always | Bundled `places.json`. Existing. |
| **Tide** | ✓ Always (1-year window) | Bundled JSON, regenerated yearly via Python script. |
| **Moon phase** | ✓ Always | Calendrical, computed in C#. No data file. |
| **Wave height / period / direction** | Best-effort + cache | Open-Meteo Marine API. Cache last successful fetch. |
| **Sea-surface temperature** | Best-effort + cache | Open-Meteo Marine API. Same cache. |
| **Wind speed / direction** | Best-effort + cache | Open-Meteo Forecast API. Same cache. |
| Sighting count | Best-effort, no cache today | Pre-existing `BackendServices`. Out of scope; revisit later. |

---

## Step 3 — Tides (bundled JSON, always-offline)

### 3.1 — Python generator

New file: `tools/generate_tides.py`

- Input: hardcoded lat/lon for Fernando de Noronha port (matches one entry in `places.json`).
- Try **pytides** with DHN-published harmonic constants for the Fernando de Noronha port. Constants come from DHN's "Catálogo de Estações Maregráficas" / "Tábuas das Marés."
- Fallback if constants are hard to source: **pyTMD** with the FES2014 global ocean tide model. Slightly lower local accuracy (~5–10 cm) but planet-wide coverage.
- Output: `Assets/Resources/tides_noronha.json`, format:
  ```json
  {
    "station": "Fernando de Noronha",
    "lat": -3.85,
    "lon": -32.44,
    "generated_at": "2026-04-28T00:00:00Z",
    "valid_until": "2027-04-28T00:00:00Z",
    "samples_per_day": 24,
    "start_date": "2026-04-28",
    "heights_m": [/* 8760 floats: hourly tide height in metres relative to MSL */]
  }
  ```
- ~150 KB JSON.

Run once now via:
```bash
cd tools && python generate_tides.py
```

Yearly regen task added to `docs/conditions-feature.md` change log when run.

### 3.2 — `TideService.cs`

New file: `Assets/Scripts/TideService.cs`

```csharp
public class TideService : MonoBehaviour
{
    public TideSnapshot CurrentTide { get; private set; }
    public event Action<TideSnapshot> TideChanged;

    // Loaded once on Start from Resources/tides_noronha
    // Polled every minute in Update; emits TideChanged when the
    // current-hour bucket changes or never-before-emitted.
}

public struct TideSnapshot
{
    public float currentHeightM;
    public bool rising;                 // true if next sample > current
    public DateTime nextHighAt;
    public float nextHighM;
    public DateTime nextLowAt;
    public float nextLowM;
    public float[] next24hHeights;      // 24 floats — for the sparkline
    public DateTime windowStart;        // anchor for the 24h window
}
```

Loading: `Resources.Load<TextAsset>("tides_noronha")` → `JsonUtility.FromJson` (or Newtonsoft if the array nesting trips JsonUtility — same caveat as `places.json`).

### 3.3 — Wire to scene

- Hierarchy → right-click empty space → **Create Empty** → name `TideServices`.
- Inspector → Add Component → `TideService`.

No Inspector wiring needed (loads from Resources by hardcoded name).

### 3.4 — Acceptance

- Press Play with internet **disabled**: `TideService.CurrentTide` populates within 1s of scene load.
- `TideChanged` fires once per hour boundary.
- `next24hHeights` length is 24, all finite floats.

---

## Step 4 — Conditions (Open-Meteo + offline cache)

### 4.1 — Data types

New file: `Assets/Scripts/ConditionsTypes.cs`

```csharp
public struct ConditionsSnapshot
{
    public DateTime fetchedAt;          // when this was pulled from the network
    public bool isStale;                // computed: fetchedAt > 4h ago
    public float? waveHeightM;
    public float? wavePeriodS;
    public float? waveDirectionDeg;
    public float? seaTempC;
    public float? windSpeedKmh;
    public float? windDirectionDeg;
}
```

Nullable fields so partial-success is representable (e.g. Marine API returned but Forecast API failed).

### 4.2 — `ConditionsService.cs`

New file: `Assets/Scripts/ConditionsService.cs`

Responsibilities:
- Subscribe to `GPSHandler.PlaceChanged`. On change: load cache for that beach, emit `ConditionsChanged`, then attempt network refresh.
- Network: two `UnityWebRequest`s in parallel — Marine + Forecast — for the beach's lat/lon (looked up from `places.json` centroid).
- On success: merge into `ConditionsSnapshot`, persist to `Application.persistentDataPath/conditions_<beachKey>.json`, emit `ConditionsChanged`.
- On failure: log, keep existing cached snapshot, emit nothing new.
- Refresh timer: every 30 min while same beach is active.

Endpoints (lat/lon are placeholders — replaced with the resolved beach centroid):
- `https://marine-api.open-meteo.com/v1/marine?latitude=-3.85&longitude=-32.44&hourly=wave_height,wave_period,wave_direction,sea_surface_temperature&forecast_days=1&timezone=auto`
- `https://api.open-meteo.com/v1/forecast?latitude=-3.85&longitude=-32.44&hourly=wind_speed_10m,wind_direction_10m&forecast_days=1&timezone=auto`

We pull the **current hour's** value from each hourly array.

### 4.3 — Beach centroids

`ReverseGeocoding.GetAllPlaceNames()` already exposes names, but we need lat/lon per beach. Add `ReverseGeocoding.GetCentroid(string placeName) → Vector2?` that averages the polygon vertices. ~10 lines added to `ReverseGeocoding.cs`.

### 4.4 — Persistent cache

Path: `Application.persistentDataPath + "/conditions_" + Sanitize(beachName) + ".json"` (sanitize: replace spaces + accents with underscore, lower-case).

Read on startup / on every `PlaceChanged`. Write after every successful network refresh.

### 4.5 — Wire to scene

- Hierarchy → **Create Empty** → name `ConditionsServices`.
- Add Component → `ConditionsService`.
- Inspector → wire `Gps` slot → `LocationServices`.

### 4.6 — Acceptance

- First launch with internet: pill populates within ~2 s of beach resolving.
- Force-quit, disable internet, re-launch: pill shows last known values for that beach with a stale indicator.
- First launch with no internet ever for a given beach: tide + moon visible; wave/wind/temp show "—".

---

## Step 5 — Moon phase (offline always)

### 5.1 — `MoonPhase.cs`

New file: `Assets/Scripts/MoonPhase.cs`

Static utility, no MonoBehaviour:

```csharp
public static class MoonPhase
{
    // 0.0 = new, 0.25 = first quarter, 0.5 = full, 0.75 = last quarter
    public static float Phase(DateTime utc);
    public static float Illumination(DateTime utc);   // 0..1
    public static MoonPhaseName Name(float phase);    // 8 enum values
}

public enum MoonPhaseName { New, WaxingCrescent, FirstQuarter, WaxingGibbous, Full, WaningGibbous, LastQuarter, WaningCrescent }
```

Algorithm: synodic-month sinusoid from a reference new moon (e.g. 2000-01-06 18:14 UTC). Accuracy ±1 day for any consumer-grade UI; that's fine.

### 5.2 — Acceptance

- Unit tests in `Assets/Scripts/Tests/Editor/MoonPhaseTests.cs`: spot-check against known new/full moon dates. Tolerance 1 day.

---

## Step 6 — `ConditionsPillView` (TopBar pill UI)

### 6.1 — Pill content

Inside the existing `ConditionsPill` Image, build:

```
[wave icon]  1.8 m   |   [moon glyph]   |   ↑ subindo
```

Three segments, separated by faint vertical dividers. Each segment is one `TMP_Text` + one Image. Use `HorizontalLayoutGroup` on `ConditionsPill` (Control Child Size W+H, Force Expand W on, Spacing 4, Padding 8 / 8 / 4 / 4).

### 6.2 — `ConditionsPillView.cs`

New file: `Assets/Scripts/UI/ConditionsPillView.cs`

- Subscribes to `ConditionsService.ConditionsChanged`, `TideService.TideChanged`.
- Computes moon phase at render time from `DateTime.UtcNow`.
- Renders gracefully:
  - `waveHeightM == null` → wave segment shows "—"
  - All three nullable → still shows tide + moon (always available)
- Tap handler → navigates to BeachesScreen detail for the current beach. Wire via UI Button on `ConditionsPill`.

### 6.3 — Moon glyph assets

8 small sprites (32×32) in `Assets/Images/UI/moon/` — one per `MoonPhaseName`. Either drawn flat or generated.

### 6.4 — Acceptance

- Pill renders even with zero network history (tide + moon visible, wave "—").
- Taps deep-link to the matching beach detail.

---

## Step 7 — Tide sparkline

### 7.1 — `TideSparkline.cs`

New file: `Assets/Scripts/UI/TideSparkline.cs`

Approach: Texture2D regeneration on `TideChanged`. ~40 lines.

```csharp
[RequireComponent(typeof(RawImage))]
public class TideSparkline : MonoBehaviour
{
    [SerializeField] TideService tides;
    [SerializeField] Color fillColor = new(0.4f, 0.8f, 1f, 0.6f);
    [SerializeField] Color cursorColor = Color.white;

    void OnEnable() { tides.TideChanged += Render; if (tides.CurrentTide.next24hHeights != null) Render(tides.CurrentTide); }
    void OnDisable() { tides.TideChanged -= Render; }

    void Render(TideSnapshot t)
    {
        // 192×48 Texture2D, fill below normalized curve, vertical "now" cursor.
    }
}
```

Width 192, height 48. Pixel-aliased; if it looks jagged we upgrade to a custom MaskableGraphic later.

### 7.2 — Where it lives

In the existing `BeachesScreen` detail panel, add a `RawImage` named `TideSparkline` near the top of the conditions card. RectTransform sized to native pixel dims for crispness (192×48).

### 7.3 — Acceptance

- Sparkline renders the moment a beach detail opens, with a vertical cursor at "now."
- Cursor advances visibly across the curve as time passes (test by exposing a `[ContextMenu]` "Advance 1h" hook on `TideService` for editor verification).

---

## Step 8 — Beach detail conditions card

In `BeachesScreen → BeachesDetail`, add a **Conditions** section (under the existing description/banner):

```
Conditions
  Onda     1.8 m · 11 s · SW
  Maré     subindo, próxima alta 14:32 (1.4 m)
           [TideSparkline 192×48]
  Lua      Nova · 8% iluminada
  Vento    15 km/h E (offshore)
  Água     27 °C
  Atualizado: há 2h            ← freshness indicator
```

Layout: `VerticalLayoutGroup` (Control Child Size W+H, Force Expand Width on). Each row: a `HorizontalLayoutGroup` with a label TMP_Text (preferred width 80) and a value TMP_Text (flexible width 1).

### 8.1 — `ConditionsCardView.cs`

New file: `Assets/Scripts/UI/ConditionsCardView.cs`

Same subscription pattern as the pill. Renders all fields including null-safe rendering ("—" when missing). Freshness row shows "Atualizado: agora / há Xm / há Xh / há Xd" based on `ConditionsSnapshot.fetchedAt`.

### 8.2 — Acceptance

- Card renders for every beach in `places.json`.
- Renders cleanly with all fields null (everything "—" except tide and moon).
- Freshness updates live.

---

## Step 9 — Per-beach risk rules (deferred, future)

After steps 3–8 ship and you have field experience with the raw values, add:

- `Assets/Resources/risk_rules.json` — per-beach thresholds (e.g. `{ "Cacimba do Padre": { "redIfWaveOver": 2.5, "redIfPeriodOver": 12, "redIfSwellDir": ["SW","S"] } }`).
- `Assets/Scripts/RiskCalculator.cs` — pure function from `(beach, ConditionsSnapshot, TideSnapshot, MoonPhase) → RiskLevel + reasons[]`.
- `RiskBanner` component on the detail card top.
- `ConditionsPill` color tint driven by risk level.

Out of scope until raw conditions are working in the field.

---

## Cross-cutting decisions captured

- **Tide source**: bundled JSON via `tools/generate_tides.py`, regenerated yearly. Computed from harmonic constants (pytides) or FES2014 (pyTMD) — generator picks one.
- **Wave/wind source**: Open-Meteo Marine + Open-Meteo Forecast. No API key. 16-day forecast horizon, we use the current hour.
- **Wind not in Marine API**: it's in the regular Forecast API, hence two endpoints.
- **Cache location**: `Application.persistentDataPath` (survives reinstalls? No — survives updates, not reinstalls. Acceptable).
- **Stale threshold**: snapshots older than 4 h are flagged stale (visible to user; values still rendered).
- **Refresh cadence**: 30 min while the same beach is active. Re-fetch on every `PlaceChanged`.
- **HTTPS only**: all network is HTTPS, avoiding the Android cleartext-traffic block that affects the existing dev-LAN `BackendServices`.

## Open questions

1. **Confirm DHN harmonic constants are obtainable**. If not, fallback to FES2014 in the generator. *(Resolved in step 3.1 once the script runs.)*
2. **Beach centroid accuracy** — averaging polygon vertices is fine for ~1 km beaches; if a polygon is highly elongated or concave, centroid may sit outside it. Spot-check by logging the computed centroids on first run. *(Step 4.3.)*
3. **Cache eviction** — never eviction in v1 (per-beach files, ~30 beaches × ~2 KB = trivial). Revisit if footprint grows.
4. **Localization** — strings ("subindo", "próxima alta", "há 2h", "offshore") hardcoded pt-BR for now. If we add other languages later, extract to a small dictionary; not worth doing for v1.

## Yearly maintenance

- Re-run `tools/generate_tides.py` annually. Replace `Assets/Resources/tides_noronha.json` and commit.
- Add a calendar reminder when the `valid_until` date in the JSON crosses 60 days from today.
