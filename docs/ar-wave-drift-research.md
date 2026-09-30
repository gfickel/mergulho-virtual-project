# How ocean waves corrupt ARCore/ARKit VIO — a physically defensible model for an offline simulator

**Purpose.** Build a simulator that reproduces, offline and deterministically, the phantom camera
motion that ARCore's visual-inertial odometry (VIO) produces when a user stands on a Fernando de
Noronha beach and points the phone at breaking surf. This document is the *model*: mechanism
(§1), forcing (§2), the synthesized numeric spec to implement (§3), mitigations and what is
actually reachable from Unity + ARCore (§4), and PDR numbers (§5).

**Status of the evidence.** Nobody has published a measurement of ARCore VIO pointed at surf. Every
number below is one of three kinds, and each is labelled inline:

| Tag | Meaning |
|---|---|
| **[M]** | Measured / published, cited. |
| **[D]** | Derived by me from cited physics or from standard VIO estimator structure. Arithmetic is shown. |
| **[E]** | Extrapolation / engineering judgement. Treat as a tunable, not a fact. |

Read §3 as a *parameterised hypothesis to be falsified on the beach*, not as ground truth. §6 lists
the specific things I could not find and what would settle them.

---

## 1. Failure mechanism

### 1.1 What ARCore actually is

ARCore's motion tracking is **"multi-state constraint Kalman filter (MSCKF) style VIO/SLAM …
called concurrent odometry and mapping (COM)"** — that phrasing is from an independent empirical
evaluation of four proprietary VIO systems, and matches Google's own COM patent family **[M]**
([Yang et al. 2022, arXiv:2207.06780](https://arxiv.org/abs/2207.06780);
[WO2017201282A1](https://patents.google.com/patent/WO2017201282A1/en)). Three consequences matter:

1. **It is a filter, not a batch optimiser.** Filter-based estimators are the *vulnerable* class in
   dynamic scenes: benchmarks show optimisation-based systems "enforce global geometric consistency
   over a sliding window [and] effectively marginalize dynamic outliers even when they occupy a
   significant portion of the field of view, whereas filter-based estimators exhibit a critical
   vulnerability threshold where dynamic feature clusters compromise the innovation term and lead to
   irreversible state divergence" **[M]**
   ([Benchmarking VIO under sensor degradation and dynamic occlusion, arXiv:2609.18628](https://arxiv.org/html/2609.18628)).
   In that benchmark OpenVINS (EKF/MSCKF family, i.e. ARCore's family) peaks at **5.54 m ATE RMSE at
   30 % dynamic occlusion**, while ORB-SLAM3 (optimisation) stays at **0.04–0.15 m even at 90 %
   occlusion** **[M]**. ARCore is architecturally on the wrong side of that line.
2. **It runs on a 640×480 VGA CPU image at 30 fps.** Google: "ARCore records the 640x480 (VGA) CPU
   image that's used for motion tracking"; camera configs can be filtered to 30 fps **[M]**
   ([Recording and playback](https://developers.google.com/ar/develop/recording-and-playback),
   [Configuring the camera](https://developers.google.com/ar/develop/c/camera-configs)).
   This sets the whole pixel-space budget in §1.4.
3. **It also maintains a map.** COM = odometry *plus* mapping. The map is what produces the sudden
   corrections (§1.7), and it is why the error is not a clean monotonic ramp.

Google's own documentation is thin on failure: it says only that ARCore "detects visually distinct
features … called feature points and uses these points to compute its change in location", and that
"flat surfaces without texture, such as a white wall, may not be detected properly" **[M]**
([Fundamental concepts](https://developers.google.com/ar/develop/fundamentals)). **Water is not
mentioned anywhere in ARCore's docs.** The relevant failure — a *textured* surface that moves
coherently — is not in Google's model of the world at all.

### 1.2 The pipeline, stage by stage

**(a) Feature detection on foam and glitter.** Water is not a texture-poor surface. Breaking surf is
a *high-contrast, corner-rich* surface: foam edges, bubble patches, the bore front, the foam lines
left on wet sand. A corner detector will happily fire on it. The relevant "insufficient features"
failure mode does **not** apply — the opposite does. Note the two channels behave differently:

| Channel | Behaviour | Effect on the tracker |
|---|---|---|
| **Foam / whitecaps** | Advected. A foam patch is a *material* marker that translates shoreward with the bore, and survives long enough to be tracked: oceanic whitecap foam decay time measured at **0.2–10.4 s** over 552 breakers, effective (area-weighted) **1.4–4.8 s** **[M]** ([Callaghan et al. 2012, JGR Oceans](https://agupubs.onlinelibrary.wiley.com/doi/10.1029/2012JC008147)); surfactants extend it ~3× **[M]**. **Surf-zone residual foam sheet — sand- and organics-stabilised, continually replenished — lives far longer and is unquantified in the literature (§2.5).** | **The dangerous one.** Long-lived, coherently moving tracks. |
| **Sun glitter** | *Specular*, not material. A glint exists where the local surface slope satisfies the specular condition for (sun, surface, eye); the slope field is dominated by centimetric gravity-capillary waves **[M]** ([sun glitter and sea-surface roughness](https://www.sciencedirect.com/science/article/pii/S0034425718300105); [Sun glitter](https://en.wikipedia.org/wiki/Sun_glitter)). Glints therefore **twinkle on and off in place** rather than translating. | Mostly a *feature-starvation and track-churn* nuisance: short-lived tracks, wasted detector budget, elevated outlier rate. Contributes noise, not bias. |

Model them as two separate corruption channels. Foam drives the bias; glitter drives the noise and
the track-loss rate.

**(b) Feature tracking — the foam is comfortably trackable, and that is the problem.** With
f ≈ 500 px for a 640-wide VGA image at ~60–66° HFOV, a camera 1.5 m above the water plane, and a bore
closing at 4 m/s **[D]**:

| Water feature range Z | Depression below horizon | Image speed | Per frame @30 fps | Angular rate |
|---|---|---|---|---|
| 40 m | 18.8 px | 1.9 px/s | 0.06 px | 0.21 °/s |
| 25 m | 30 px | 4.8 px/s | 0.16 px | 0.55 °/s |
| 15 m | 50 px | 13.3 px/s | 0.44 px | 1.5 °/s |
| 10 m | 75 px | 30 px/s | 1.0 px | 3.4 °/s |
| 5 m (inner surf) | 150 px | 90 px/s | 3.0 px | 10.3 °/s |
| 3 m (swash, v=3 m/s) | 250 px | 250 px/s | 8.3 px | 28.7 °/s |

*(geometry: y = f·h/Z, ẏ = f·h·v/Z²)* **[D]**

Every one of these is **inside a normal KLT/patch search window (≈15–31 px)**. The foam does not move
fast enough to break the tracker; it moves slowly, smoothly and *persistently* — exactly the profile
of a good inlier. There is no "this track is obviously bad" signal at the tracking stage.

**(c) Outlier rejection: RANSAC does not save you, for two separate reasons.**

*Reason 1 — the moving set can be the majority.* The standard statement: conventional RANSAC "assumes
that all features belong to a static scene", and "RANSAC-based approaches fail when the majority of
keypoints belong to moving objects" **[M]**
([Modified 1-point RANSAC for dynamic VO, Biomimetics 2025](https://pmc.ncbi.nlm.nih.gov/articles/PMC12562752/)).
The automotive analogue is well documented: "the assumption of scene rigidity stops holding when a
large vehicle such as a truck or a van occupies a majority of the field of view", and the system
"incorrectly interprets the motion of the passing truck as motion of the observer's own vehicle
(pseudo motion)" **[M]**
([Robust monocular VO for road vehicles, JIVP 2015](https://link.springer.com/article/10.1186/s13640-015-0065-6)).
**Surf pointed at from a beach is the truck, permanently.**

*Reason 2 — and this is the one people miss — "majority" for translation is not counted in features.
It is counted in 1/Z².* The translational Jacobian of an image measurement scales as 1/Z (depth), so
the Fisher information a feature contributes about camera *translation* scales as **1/Z²**. A feature
on wet sand 3 m away is worth **(25/3)² ≈ 69** features on the sea surface 25 m away, for translation.
Modelling the frame as two rigid groups (static, information I_s; dynamic water, I_d, displaced by d),
weighted least squares puts the estimate at **α·d with α = I_d/(I_s + I_d)**, I ∝ Σ n/Z² **[D]**:

| Scene | Static group(s) | Water group | I_s | I_d | **α** |
|---|---|---|---|---|---|
| C: surf far (30 m), lots of near dry sand in frame | 200 @ 4 m | 120 @ 30 m | 12.50 | 0.13 | **1.1 %** |
| A: typical — surf 15–35 m, dry sand 3–8 m in lower frame | 100 @ 5 m, 50 @ 30 m | 150 @ 25 m | 4.06 | 0.24 | **5.6 %** |
| E: pointed at the horizon, almost no sand in frame | 30 @ 8 m, 40 @ 40 m | 180 @ 25 m | 0.49 | 0.29 | **36.8 %** |
| B: phone tilted down, swash at 3 m fills the lower half | 80 @ 5 m | 100 @ 3 m | 3.20 | 11.11 | **77.6 %** |
| D: standing at the waterline, foam washing at 2–5 m | 60 @ 6 m | 140 @ 3.5 m | 1.67 | 11.43 | **87.3 %** |

**Headline: corruption is dominated by whatever moving water is *closest to the camera*, not by how
much sea is in frame.** Pointing at distant surf with dry sand in the bottom of the frame is nearly
benign (α ≈ 1–6 %). Tilting down so the swash/backwash fills the lower half is catastrophic
(α ≈ 78–87 %) — that is a genuine *majority*, RANSAC picks the water as the consensus set, and the
tracker is now measuring the wave instead of the phone. **[D]**

Robustification (Huber/Cauchy, ARCore certainly has something) reduces the *sustained* α below these
instantaneous values, because a water track's residual grows monotonically and eventually exceeds the
kernel threshold and is dropped. But it cannot eliminate the bias: the detector must keep replenishing
features, each fresh water track starts consistent and diverges, so the corruption arrives as a
**continuous stream of small, always-same-sign pulses** rather than one big rejected blob. Treat the
values above as an upper envelope and the *effective* α as 10–30 % of them for scenes A/C, and close
to them for B/D where the water is the consensus set **[E]**.

### 1.3 How the IMU resolves the disagreement — and why it mostly cannot

This is the crux of the whole model, and it is the part that determines the simulator's **frequency
response**.

The gyro and the accelerometer are not symmetric partners here.

- **Rotation is well constrained.** The gyro directly measures angular rate at high rate and low
  noise; roll and pitch additionally have an absolute reference (gravity) whenever the phone is not
  accelerating — which, for a person standing still, is always. So the filter will *not* accept a
  large rotation explanation for the water flow. Rotation error stays small (§1.8).
- **Translation is essentially unconstrained at low frequency.** The accelerometer measures specific
  force (a − g). **A constant-velocity phantom translation produces exactly the same accelerometer
  reading as standing still.** The accelerometer can only object to *accelerating* phantom motion.
  This is the same observability fact that makes monocular VIO scale "slowly drifting and poorly
  conditioned … severe error drift under zero or constant-velocity trajectories" **[M]**
  ([Motion as a sensing modality for metric scale in monocular VIO, arXiv:2603.26740](https://arxiv.org/html/2603.26740)).

Quantify it. A sinusoidal phantom translation of amplitude A at frequency f implies peak acceleration
**a = A(2πf)²**. The filter can only object if that exceeds its own accelerometer error floor. Measured
accelerometer bias across five phones **[M]**
([Smartphone MEMS accelerometer and gyroscope measurement errors, *Sensors* 23:7609, 2023](https://pmc.ncbi.nlm.nih.gov/articles/PMC10490716/)):
**0.018 m/s² (Vivo X60 Pro, best) to 0.206 m/s² (OnePlus 7 Pro, worst)**; gyro bias
**0.000637 rad/s (Pixel 7 Pro) to 0.006614 rad/s (OnePlus 7 Pro)**. Using the accel bias as a_th, the
*largest phantom amplitude the IMU cannot veto* is **A_max = a_th/(2πf)²** **[D]**:

| accel bias (m/s²) | f=0.04 (swash, 25 s) | **f=0.065 (swell, 15 s)** | **f=0.083 (swell, 12 s)** | f=0.125 (sea, 8 s) | f=0.2 (sea, 5 s) | f=0.5 |
|---|---|---|---|---|---|---|
| 0.018 *best phone* | 0.285 m | **0.108 m** | **0.066 m** | 0.029 m | 0.011 m | 2 mm |
| 0.05 | 0.792 m | **0.300 m** | **0.184 m** | 0.081 m | 0.032 m | 5 mm |
| 0.10 | 1.58 m | **0.600 m** | **0.368 m** | 0.162 m | 0.063 m | 10 mm |
| 0.206 *worst phone* | 3.26 m | **1.24 m** | **0.757 m** | 0.334 m | 0.130 m | 21 mm |

Three conclusions, all of which the simulator must honour:

1. **The IMU is a high-pass *rejector* of phantom motion with a 1/f² corner.** It crushes fast phantom
   jitter and passes slow phantom ramps almost untouched. The wave bands (§2) sit at 0.03–0.2 Hz —
   precisely where the IMU is blind.
   > **Simulate the corruption as low-frequency (0.03–0.2 Hz) phantom translation with a 1/f² amplitude
   > envelope above ~0.1 Hz. Do NOT simulate high-frequency phantom translation jitter — the IMU removes
   > it, and above 0.5 Hz the admissible amplitude (2–21 mm) is below ARCore's own jitter floor.**
2. **Phantom drift should be strongly device-dependent — by up to ~17×** (0.07 m on the best phone vs
   1.24 m on the worst, at the swell band). This is a *testable* prediction and it matches the measured
   device spread in hologram stability (Nokia 7.1 at 19.6 cm vs Galaxy Note 10+ at 5.6 cm in low light
   **[M]**, §1.7). **Expose the accel-bias figure as a simulator parameter** and sweep it.
3. **Longer-period swell is worse.** A_max scales as 1/f², so Noronha's 12–18 s groundswell admits
   **2.6–4× more phantom amplitude than the 5–9 s trade sea.** The surf season is the bad season for
   VIO *because the waves are longer, not because they are bigger* (§2.2).

And note the vicious detail: **standing still is the worst case for scale observability** (no
acceleration excitation ⇒ scale unobservable), which is exactly the posture the app asks the user to
adopt.

### 1.4 The sign: which way does the tracker think it moved?

Optical flow depends only on *relative* motion. If scene points move with world velocity **v_w** and
the camera is static, the points' velocity relative to the camera is **v_w**. If instead the points
were static and the camera moved at **v_c**, their velocity relative to the camera is **−v_c**.
Equating the two flow fields: **v_c = −v_w** **[D]**. (General statement of the same fact: "the
apparent motion observed in optical flow is opposite to the actual camera motion direction",
[MathWorks — What is optical flow](https://www.mathworks.com/help/vision/ug/what-is-optical-flow.html).)

Waves and foam travel **shoreward**, i.e. *toward* the observer. Therefore:

> **The tracker infers that the phone translated SEAWARD — against the direction of wave travel,
> "into" the oncoming wave.**

Cross-check via the flow field: an approaching foam line produces a **diverging / expanding** flow
(points move down and outward, away from the horizon, as y = f·h/Z grows with shrinking Z). A
diverging flow field is the signature of *forward* camera translation, and forward here means toward
the sea. Same answer. **[D]**

**Consequence you can verify on the device.** If ARCore's reported camera pose drifts seaward by δ,
the renderer draws the world from a viewpoint δ further out to sea, so a world-anchored object appears
**δ closer** — it **creeps toward the viewer / up the beach, and grows**, once per wave — and then
**snaps back seaward** when the map corrects. *"Content creeps toward me at wave cadence, then jumps
away"* is the falsifiable prediction of this model. If the observed symptom is the opposite sign, the
model is wrong and the simulator must flip.

**One important degeneracy.** For a camera at height h looking at water at range Z, "the water came
2 m closer" and "the camera rose by h·ΔZ/Z" predict *almost* the same image motion. The vertical-rise
explanation is far cheaper in norm — **16.7× cheaper at Z = 25 m, 6.7× at 10 m, only 2× at 3 m** **[D]**
— so a filter with a motion prior will spend part of the error budget as a **phantom upward
translation** instead of a big forward one. The two are distinguishable only by their depth signature
(forward ∝ 1/Z², vertical ∝ 1/Z), which needs a good depth spread in the corrupting features. Practical
upshot for the simulator: **expect a co-phased vertical (upward) component**, larger than naive
intuition suggests, especially when all the visible foam sits in a narrow range band.

### 1.5 (a) Phantom translation — temporal shape

Not a sinusoid, and not a random walk. A **rectified pulse train**:

- A wave arrives, breaks, and its bore sweeps shoreward through the visible surf zone for a
  **coherent phase** of ~2–5 s. Throughout that phase the flow is unidirectional. This is a
  *forcing pulse*.
- Then the surface goes relatively quiet (less foam, fewer coherent tracks) until the next wave.
- **There is no reverse phase of comparable strength.** Backwash does run seaward, but it is lower
  contrast, shorter-lived, and occupies less of the frame; foam never sweeps seaward at bore speed.

So the corruption is **rectified**: same-sign pulses at the wave frequency with only partial recovery
between them. The recovery comes from (i) the static features re-asserting themselves once the foam
is gone, and (ii) the map/relocalisation snapping the pose back (§1.7). Net result:

**sawtooth ratchet = (slow seaward ramp during each wave) + (partial elastic recovery) + (occasional
discrete snap-back)**, superposed on a low-frequency oscillation at the wave period. Model it as a
**biased, band-limited process, not a zero-mean one.** **[D]**

Magnitude, from **d_phantom = α_eff · c · t_coherent** **[D]**:

| α_eff | c=3.5 m/s, t=2 s | c=4.0, t=3 s | c=4.5, t=5 s |
|---|---|---|---|
| 0.5 % | 0.035 m | 0.060 m | 0.11 m |
| 1 % | 0.070 m | 0.12 m | 0.23 m |
| 2 % | 0.14 m | 0.24 m | 0.45 m |
| 5 % | 0.35 m | 0.60 m | 1.13 m |
| 10 % | 0.70 m | 1.20 m | 2.25 m |
| 30 % (swash in frame) | 2.10 m | 3.60 m | 6.75 m |

### 1.6 (b) Scale drift

Two mechanisms, both active:

1. **Degenerate excitation.** A stationary user provides no acceleration, so monocular VIO scale is
   unobservable and free-floating **[M]** (arXiv:2603.26740, above).
2. **Forced reconciliation.** Vision says "I translated", the accelerometer says "no specific force
   changed". One of the cheapest ways for the filter to reduce that contradiction is to re-scale the
   map — decide the scene is nearer or farther than it is. The diverging (looming) flow of an
   approaching bore is literally the canonical scale-error input.

Published magnitudes for the general case, not surf specifically: AR systems in a large dynamic
industrial environment showed **"a scaling error of up to 14.4 cm/m … quasi-directly proportional to
the path length"** and **~17 m error per 120 m travelled** across ARCore/ARKit/HoloLens **[M]**
([Feigl et al., GRAPP 2020](https://www.scitepress.org/Papers/2020/89899/89899.pdf)). 14.4 cm/m is a
**14 % scale error**. For a stationary user I would model scale as a slow multiplicative random walk
of **±2–10 % over a minute**, correlated with the phantom-translation magnitude **[E]**.

### 1.7 (c) Relocalisation jumps

COM's mapping side periodically corrects the odometry against the map; the correction appears as a
**pose discontinuity within one or a few frames**. The best measured proxy is hologram-stability work
on real phones **[M]** ([Here To Stay, arXiv:2109.14757](https://ar5iv.labs.arxiv.org/html/2109.14757)):

| Action | Mean drift | Range |
|---|---|---|
| Unfocus (brief distraction, same viewpoint) | 1.2 cm | 0.1–14.9 cm |
| Focused move (45° viewpoint change) | 2.0 cm (ARKit 2.6 / **ARCore 7.2**) | 0.1–17.1 cm |
| Pause (5 s interruption) | 2.5 cm | 0.0–44.9 cm |
| Place down (camera covered) | 7.1 cm (ARKit 3.6 / **ARCore 17.8**) | 0.0–82.2 cm |
| Unfocused move | 12.8 cm | 0.1–118.3 cm |
| Walk away ~7 m and return | 43.2 cm (ARKit 25.1 / ARCore 4.8) | 0.1–323.3 cm |

Also from the same paper: low light (brightness < 0.3) strongly degrades stability; scenes with drift
< 2 cm needed **Shannon entropy > 6.9** and **≥ 500 FAST corners per sampled frame** for reliable loop
closure **[M]**. A surf scene has plenty of corners and plenty of entropy — it passes both of those
health checks while being wrong, which is why ARCore will not self-report a problem.

**Simulator values:** jumps of **0.1–0.5 m typical, occasionally > 1 m**, as single-frame
discontinuities, biased **seaward-correcting** (i.e. opposite to the accumulated ratchet), at a rate
of roughly **one per 5–30 s under sustained corruption** **[E — the rate is not published anywhere I
could find]**. Note the app's existing `SpuriousMotionGate.relocalizationJumpThreshold = 0.35 m`
sits squarely inside this band, which is reassuring but means it will pass some real relocalisations
and suppress some phantom ones.

### 1.8 (d) Rotation error

Smallest of the four, because the gyro wins.

- **Roll/pitch**: gravity-referenced whenever the user is still. Expect **< 0.5°** error, essentially
  uncorrupted by the water **[E]**.
- **Yaw**: no absolute reference inside VIO. From the measured phone gyro biases **[M]** (§1.3),
  uncorrected yaw drift is **0.036 °/s = 2.2 °/min (Pixel 7 Pro, best)** to **0.379 °/s = 22.7 °/min
  (OnePlus 7 Pro, worst)** — i.e. **11° to 114° over 5 minutes** **[D]**. Water contamination adds a
  bias on top when the wave approach is **oblique** or a longshore current sweeps foam sideways — the
  lateral flow component maps to a yaw/lateral-translation ambiguity. Model **0.5–3 °/min of extra
  yaw drift, signed by the longshore flow direction** **[E]**.

Do **not** simulate large rotation error. If your simulator produces multi-degree attitude wobble at
wave frequency, it is over-modelling: the gyro prevents that.

### 1.9 Summary of the mechanism

```
foam (high contrast, 1.4-10 s lifetime, advected shoreward at sqrt(gh))
  -> detector fires on it; it is a GOOD feature by every local quality metric
  -> tracker follows it easily (0.06-8 px/frame — well inside the search window)
  -> RANSAC/robust cost weights it by 1/Z^2; when the water is NEAR (swash, waterline)
     it becomes the consensus set and the tracker measures the wave, not the phone
  -> the filter must choose a camera motion to explain the coherent flow
       * rotation explanation  -> BLOCKED by the gyro
       * scale/depth explanation -> partly taken (scale drift)
       * translation explanation -> NOT blocked: at 0.03-0.2 Hz the implied acceleration
         is at or below the phone's own accel bias floor (0.018-0.206 m/s^2 measured),
         so the accelerometer literally cannot object
  -> phantom translation, direction = -v_water = SEAWARD, rectified once per wave
  -> map/COM periodically snaps the pose back  => jumps
```

---
## 2. Quantitative wave characteristics — Fernando de Noronha

Noronha (3.85°S, 32.44°W) is an oceanic island 345 km offshore with an open northern horizon and no
continental shelf, so it receives **North Atlantic groundswell** from NNW–N in the austral summer plus
**SE trade-wind sea** most of the year. Beaches of interest: **Cacimba do Padre** (N-facing, steep,
reflective–intermediate, the barrel) and **Baía do Sueste** (S/SE-facing, sheltered behind reef).

### 2.1 Periods

| Quantity | Value | Source |
|---|---|---|
| Tp, all sea states, NE-Brazil sector (110-yr reanalysis point PNE) | mean **8.6 ± 1.8 s**, median 8.0 s, Tp10 **12.7 s**, Tp5 **13.4 s**, Tp2 **14.4 s**, max **19.6 s**, min 4.5 s | **[M]** [Cotrim MSc thesis](https://sapientia.ualg.pt/server/api/core/bitstreams/369265e2-0910-44dd-952e-3eb643cb6006/content) |
| Swell/sea split convention used in this literature | **swell = Tp > 11 s** (20.9 % of the time at PNE); **wind sea = Tp < 9 s** (79.1 %) | **[M]** ibid. |
| Tp mode at Noronha specifically (WW3) | **8–10 s, 32.3 %** of the time (trade-dominated annual mode) | **[M]** [Ambrosio et al. 2022, Cont. Shelf Res.](https://www.sciencedirect.com/science/article/abs/pii/S0278434322000188) |
| Longest periods, NE Brazil | **up to 21 s**; waves with **Tp > 20 s arrive from NNE–ENE** (Northern-Hemisphere origin) | **[M]** [J. Operational Oceanogr. 2018](https://www.tandfonline.com/doi/full/10.1080/1755876X.2018.1438567) |
| Realistic modelled NE-Brazil swell events | max incoming Tp **17.6 s** (one synthetic case 21.3 s) | **[M]** [Bezerra et al., Deep-Sea Res. I](https://www.sciencedirect.com/science/article/abs/pii/S0967063719303310) |
| PNBOIA Recife buoy (8°09′S 34°33′W) regional | Hs mostly 1–2 m, **Tp 6–8 s**, E–SE | **[M]** |

**Working values.** Groundswell **Tp ≈ 12–18 s**, dominant during a real N-swell event **13–16 s**,
documented tail 19–21 s. Wind sea **Tp ≈ 5–9 s**, with the Noronha mode at the **upper end (8–10 s)**
because the SE trades have the whole equatorial Atlantic as fetch.

Two caveats that matter for the simulator **[E]**:
- PNE and the Recife buoy are **mainland, E/SE-facing** points and structurally under-sample NNW–N
  swell. The *period band* transfers to Noronha; the *frequency of occurrence* of long-Tp events there
  is higher than 20.9 % implies.
- Swell is **dispersive**: Tp *falls through an event*. A 5–6 day Noronha swell starts at ~16–18 s and
  decays to ~11–12 s. So the swell centre frequency is not a constant — sweep it if you simulate multi-day.

### 2.2 Heights

| Quantity | Value | Source |
|---|---|---|
| Offshore Hs mode at Noronha (WW3, annual) | **2.0–2.5 m, 49.5 %** of the time | **[M]** Ambrosio et al. 2022 |
| Regional mean Hs (ERA-5H, 22 km) | ~**1.75 m**; mean swell Hs to 2.5 m; mean **wind-sea Hs ≤ 1.5 m** | **[M]** [Cotrim, Semedo & Lemos 2022, *Climate* 10:53](https://doi.org/10.3390/cli10040053) |
| Extremes | **P95 ≈ 2 m** at tropical latitudes; **max Hs is lowest in the equatorial region** | **[M]** ibid. |
| NE-coast seasonal mean Hs | **1–2 m spring & summer**, **2–3 m in autumn** | **[M]** ibid. |
| Breaking face, peak season Dec–Mar | **4–8 ft ≈ 1.2–2.5 m**; never under 2 ft Dec–Feb; swells last **5–6 days**; optimum angle **NNW** | **[M]** (surf-climatology grade) [Stormrider](https://www.stormrider.surf/region/fernando-de-noronha) |
| Monthly breaking face | Jan–Feb 6 ft · Mar–Apr 5 ft · Nov–Dec 4 ft · **May–Oct 1–2 ft** | **[M]** [mywavefinder](https://www.mywavefinder.com/destination/fernando-de-noronha-brazil-surf-guide/) |
| Big days, Cacimba | up to **12 ft ≈ 3.7 m**; contest surf ran at 4–5 ft (1–1.5 m) | **[M]** [Projeto Golfinho Rotador](https://golfinhorotador.org.br/en/fernando-de-noronha/surf/) |
| Surfable fraction | best month **February, 49 %** clean surfable; September **3 %** | **[M]** [surf-forecast Cacimba stats](https://www.surf-forecast.com/breaks/Cacimbado-Padre/surf-stats) |

**The counter-intuitive seasonality.** Total offshore Hs is *not* much higher in the surf season — the
trades keep winter Hs up. What changes is **direction and period**: "northern swell in summer and
southern swell in winter … higher wave energy on the north coast during summer" **[M]** (Ambrosio et
al. 2022). **Noronha is a long-period, not a big-Hs location** — ERA-5H puts the equatorial region at
Brazil's *lowest* max-Hs. For the simulator this means: **vary Tp seasonally, not Hs**, and expect the
worst VIO corruption in Dec–Mar not because the waves are bigger but because they are **longer-period
and better organised**, which produces cleaner, more coherent foam lines.

### 2.3 Breaking-wave front / foam-line speed — **higher than linear theory**

**Correction to the common assumption.** Measured surf-zone celerity is typically *above* √(gh), not
below. Coastal Wiki: "measured celerity values can be **20 % higher** than predicted by the linear
dispersion relation **or even more**". The often-quoted Suhayda & Pettigrew "1.2 → 0.8" ratios are
relative to **solitary-wave** celerity √(g(h+H)), **not** √(gh) — converted, M = 1.2 at the break point
(H/h = 0.78) gives **c ≈ 1.60·√(gh)**, which reconciles with Svendsen et al.'s "**up to 50–70 %** above
linear theory". Boussinesq roller models use **1.3·√(gd)**. **[M]**
([Suhayda & Pettigrew 1977](https://agupubs.onlinelibrary.wiley.com/doi/abs/10.1029/JC082i009p01419);
[Okamoto et al. ICCE 2010](https://icce-ojs-tamu.tdl.org/icce/index.php/icce/article/download/1339/pdf_327);
[Coastal Wiki, nonlinear dispersion](https://www.coastalwiki.org/wiki/Nonlinear_wave_dispersion_relations))

Formulas **[M]**: bore `c = √(gh)·√((1+H/h)(1+H/2h))`; empirical cnoidal `c ≈ √(gh)·√(1+0.45·Hs/h)`.

Noronha-scale surf zone **[D]**:

| Zone | h (m) | H/h | √(gh) | cnoidal | bore | S&P M=1.2 |
|---|---|---|---|---|---|---|
| outer break, big day | 3.0 | 0.78 | 5.42 | 6.31 | 8.53 | 8.69 |
| outer break, typical | 2.0 | 0.78 | 4.43 | 5.15 | 6.97 | 7.09 |
| mid surf | 1.5 | 0.55 | 3.84 | 4.28 | 5.39 | 5.73 |
| mid surf | 1.0 | 0.50 | 3.13 | 3.47 | 4.29 | 4.60 |
| inner surf / bore | 0.6 | 0.50 | 2.43 | 2.69 | 3.32 | 3.57 |
| shorebreak lip | 0.4 | 0.60 | 1.98 | 2.23 | 2.86 | 3.01 |

**Working values: 2–5 m/s for the visible foam line across most of the surf zone**, briefly **5–9 m/s**
just inside the outer break on a 2–3 m day, decaying to **1.5–2.5 m/s** as the bore collapses at the
shoreline. **One number for a phone-held observer watching the inner surf 10–40 m out: ≈ 3 m/s.**

⚠️ **Gap:** no paper reports absolute surf-zone celerity in m/s for a tropical beach break — the
literature reports *ratios* or tank-scale cm/s. The table above is derived from their formulas.

### 2.4 Swash / foam-backwash cycle on the sand

| Quantity | Value | Source |
|---|---|---|
| Incident (sea-swell) band | **f > 0.05 Hz** (sometimes 0.04) ⇒ T < 20–25 s | **[M]** |
| Infragravity band | **0.004–0.04 Hz = 25–250 s** | **[M]** [Coastal Wiki, IG waves](https://www.coastalwiki.org/wiki/Infragravity_waves) |
| **Swash period vs incident period** | swash periods are **larger by a factor of 1–3** than the mean incident short-wave period | **[M]** [Coastal Wiki, Swash](https://www.coastalwiki.org/wiki/Swash) |
| Measured bore-arrival return period, 7 beaches (incident Tm 6.7–9.1 s) | **16.5–35.6 s, mean 24.0 s** | **[M]** [bore-bore capture, JGR:Oceans / arXiv:1909.11279](https://arxiv.org/pdf/1909.11279) |
| Backwash vs uprush duration | backwash **20–40 % longer** | **[M]** Coastal Wiki |
| Uprush velocity | **up to 3 m/s** steep foreshore, up to 2 m/s gentle | **[M]** Coastal Wiki |
| Backwash velocity | **> 2 m/s** | **[M]** Coastal Wiki |
| Runup/backwash, general field | **2–5 m/s**, "generally larger than those in the surf zone" | **[M]** [Coastal Wiki, swash zone dynamics](https://www.coastalwiki.org/wiki/Swash_zone_dynamics) |

**Which band dominates — and the Noronha answer.** Hughes et al. 2014: short-wave swash dominates on
**reflective and intermediate** beaches; infragravity swash dominates on **dissipative** beaches
**[M]**. Guza & Thornton 1982: incident-band runup is **saturated** (energy independent of incident Hs),
while surf-beat/IG energy grows ~linearly with incident energy **[M]**.

**Cacimba do Padre is steep / reflective–intermediate, so incident-band swash dominates and
IG-dominated swash is NOT expected** (Noronha has no wide dissipative surf zone) **[E — inference; no
swash measurements exist for these beaches]**. Applying the 1–3× factor to Tp = 10–16 s:

- **Cacimba do Padre: visible uprush–backwash cycle ≈ 12–35 s, most often 15–25 s** (consistent with
  the 24 s measured mean), uprush to ~3 m/s, backwash > 2 m/s.
- **Baía do Sueste**: same incident-dominated regime at much lower amplitude, tracking the 6–9 s trade
  sea ⇒ **≈ 8–25 s**, velocities well under 2 m/s.

**This is a distinct third frequency band for the simulator: 0.03–0.08 Hz**, *not* true infragravity.

### 2.5 Visual lifetime of the features themselves

| Quantity | Value | Source |
|---|---|---|
| Whitecap foam decay time, individual events (552 breakers, sub-cm pixels) | **0.2–10.4 s** | **[M]** [Callaghan et al. 2012, JGR Oceans](https://agupubs.onlinelibrary.wiley.com/doi/10.1029/2012JC008147) |
| Effective (area-weighted) decay time | **1.4–4.8 s** (factor-3.4 spread between periods) | **[M]** ibid. |
| Surfactant effect | persists **~3×** longer than clean seawater | **[M]** [Callaghan et al. 2013, JPO](https://journals.ametsoc.org/view/journals/phoc/43/6/jpo-d-12-0148.1.xml) |
| Individual sun glint / cm-roughness decorrelation | **~10–100 ms** | **[M/E]** radar-coherence analogue (ATI-SAR uses ~3 ms lags to preserve coherence); **no optical figure exists** |

**Working values: 1.5–5 s for an identifiable foam patch (full spread 0.2–10 s); ~10–100 ms for a
single glint.**

⚠️ **Two real gaps.** (a) There is **no published decorrelation time for sun glitter as a visual
texture** — the glitter literature treats the surface as quasi-frozen between looks. (b) Callaghan's
numbers are **open-ocean whitecaps** (bubble-plume decay after one deep-water breaker). The
**persistent white foam sheet in a surf zone**, stabilised by entrained sand and organics and
continually replenished, plainly lives far longer — visually tens of seconds to minutes — and I found
**no quantitative study of it**. **Do not apply 1.4–4.8 s to a beach foam sheet.** For the simulator,
treat surf-zone foam-sheet lifetime as a free parameter in the **5–60 s** range **[E]** and note that
*longer* lifetime means *worse* VIO corruption (longer coherent tracks).

### 2.6 Apparent angular rate for a phone on the sand

Geometry **[D]**: front at horizontal range R closing at v, phone at height Hₑ above the water plane;
depression below the horizon θ = arctan(Hₑ/R), so

```
ω = dθ/dt = Hₑ·v / (R² + Hₑ²)          [rad/s]
e.g. Hₑ=1.5, v=3, R=20:  ω = 4.5/402.25 = 0.011187 rad/s = 0.641 °/s
```

**ω, deg/s, at Hₑ = 1.5 m** **[D]**:

| v (m/s) | R = 10 m | R = 20 m | R = 40 m |
|---|---|---|---|
| 2 | 1.68 | 0.427 | 0.107 |
| **3** | **2.52** | **0.641** | **0.161** |
| 5 | 4.20 | 1.068 | 0.268 |
| 7 | 5.88 | 1.496 | 0.375 |

Supporting geometry (Hₑ = 1.5 m): depression 8.53° at 10 m, 4.29° at 20 m, 2.15° at 40 m. **The whole
20–40 m band of surf collapses into 2.14° of image**; 10–40 m spans only 6.38°. Closing times: 10 m at
3 m/s = 3.3 s, 20 m = 6.7 s, 40 m = 13.3 s.

**The decisive scaling: ω ∝ Hₑ·v/R².** Two consequences that dominate the whole model:

1. **Perspective, not speed, governs.** A foam line 40 m out is effectively static in frame
   (0.1–0.4 °/s) and geometrically stable for many seconds — but at 1.5–5 s foam lifetime it
   **decorrelates photometrically before it moves appreciably**. It contributes noise, not bias.
2. **At ~10–15 m the two timescales match** (~2.5 °/s, ~3 s of travel against 1.5–5 s of foam life):
   a foam patch lives exactly long enough to generate a large, coherent, trackable displacement.
   Combined with the independent 1/Z² information argument of §1.2 (nearer = quadratically worse),
   **the danger zone is the inner surf and swash at roughly 3–15 m, not the distant surf.**
3. **Phone height is a linear knob.** Holding the phone at 1.0 m instead of 1.5 m cuts every angular
   rate by ⅓. At water level the approach flow vanishes entirely.

---

## 3. The simulator spec — what the phantom pose error should look like numerically

**Scenario being specified.** User standing on dry/damp sand, phone held at ~1.5 m, roughly horizontal
or tilted down ≤ 20°, breaking surf 10–40 m away, phone essentially still (hand tremor only). This is
the synthesis of §1 and §2. Everything here is **[D]** or **[E]**; §3.8 gives the falsification tests.

### 3.0 Frames

Define a **wave frame**, not a camera frame — this is the single most important modelling decision.

```
ŝ  = unit horizontal vector pointing SEAWARD (anti-parallel to wave propagation)
l̂  = unit horizontal vector ALONGSHORE (ŝ × up), signed by the longshore current
û  = local up
```

The phantom translation lives in **{ŝ, l̂, û}, which is fixed in the world**. It does **not** rotate
with the camera. If the user pans away from the surf, the *amplitude* falls (less water in frame, and
the near water leaves the frame first) but the *direction* stays seaward. A simulator that emits drift
along the camera's local forward axis will be wrong the moment the user turns, and "does the drift
direction follow the camera or stay put?" is the cleanest experiment to distinguish them on device.

### 3.1 Amplitude of phantom translation

Two independent routes converge, which is the main reason to believe the number.

**Route 1 — forcing × information weight** (§1.2, §1.5): d = α_eff · c · t_coherent, with
c = 3–5 m/s (§2.3), t_coherent = 2–4 s, and the α values of §1.2 attenuated by robust rejection.

**Route 2 — IMU admissibility ceiling** (§1.3): at the Noronha groundswell band f ≈ 0.065–0.083 Hz the
accelerometer cannot veto amplitudes up to **A_max ≈ 0.07–0.6 m** (device-dependent, up to 1.2 m on a
badly-biased phone); at the wind-sea band f ≈ 0.11–0.2 Hz, only **≈ 0.01–0.16 m**.

Both land in the same place. **Use these bands:**

| Regime | What's in frame | Peak-to-peak phantom translation along ŝ | Net ratchet per wave |
|---|---|---|---|
| **Benign** | Distant surf (≥ 30 m), plenty of dry sand in the lower frame | **0.02–0.08 m** | 0.005–0.02 m |
| **Nominal** (model this by default) | Surf 15–35 m, sand 3–8 m below it | **0.05–0.30 m** | 0.02–0.10 m |
| **Bad** | Phone at the horizon, little/no sand in frame; or surf inside 15 m | **0.2–0.8 m** | 0.1–0.4 m |
| **Catastrophic** | Phone tilted down onto the swash/backwash at 2–5 m, or user at the waterline | **0.5–3 m** (tracking effectively lost) | 0.3–2 m |

Sanity check against measurement: the *nominal* band (5–30 cm) brackets the measured ARCore hologram
drift for viewpoint changes and brief interruptions in ordinary static scenes (**7.2 cm** focused
move, **17.8 cm** place-down, up to ~0.8–1.2 m worst case) **[M]** (Here To Stay, §1.7). Surf should sit
**at or above** the bad end of ordinary-scene numbers, not below — the nominal band does that.

### 3.2 Frequency content

Three additive bands. Drive them from the *same* wave train so they stay phase-coherent — the whole
point is that this is one forcing, not three noises.

| Band | f | Source | Relative amplitude |
|---|---|---|---|
| **Groundswell / incident bore** | **0.056–0.083 Hz** (Tp 12–18 s; dominant event 13–16 s ⇒ 0.063–0.077 Hz) | the bore sweeping the surf zone; the Dec–Mar N-swell | **1.0** (the carrier) |
| **Wind sea** | **0.11–0.20 Hz** (Tp 5–9 s; Noronha mode 8–10 s ⇒ 0.10–0.125 Hz) | SE trade sea, present all year | **0.2–0.5** — lower than the swell despite similar Hs, *because the IMU rejects it 2.6–4× harder* (§1.3) |
| **Swash / bore-capture** | **0.029–0.083 Hz** (T 12–35 s, most often 15–25 s) | the uprush–backwash cycle on the sand; **1–3× the incident period**, measured mean 24 s (§2.4) | **0.5–1.5** — **the largest contributor whenever the swash is in frame**, because it combines near range (1/Z², §1.2) with a period the IMU cannot see |
| **Set / group modulation** | **0.007–0.014 Hz** (T 70–150 s) | wave groups — *amplitude-modulate the three bands above, do not add as a fourth oscillator* | ×0.3 → ×1.5 envelope |

> **Correction worth flagging:** an earlier, naive version of this model used true **infragravity**
> (0.004–0.04 Hz) as the slow band. That is wrong for Noronha. IG-dominated swash occurs on
> **dissipative** beaches; Cacimba do Padre is **steep / reflective–intermediate**, so incident-band
> swash dominates and there is no wide dissipative surf zone to generate strong IG swash **[M/E]**
> (§2.4, Hughes et al. 2014). The correct slow band is the **swash/bore-capture band at
> 0.03–0.08 Hz**, which is a *different* physical process from IG.

**Spectral shape rule:** apply the **1/f² accelerometer envelope** — multiply each band's amplitude by
`min(1, a_th/(A·(2πf)²))` with `a_th` = the device's accel bias (0.018–0.206 m/s², §1.3). This is not
cosmetic; it *is* the IMU's transfer function, and it is what makes the long-period swell worse than
the short-period sea.

Do **not** add broadband phantom translation noise above ~0.5 Hz: the admissible amplitude there is
2–21 mm, below ARCore's own jitter floor, so it would be indistinguishable from baseline noise and
would make the simulator *less* faithful, not more.

Realise each band as a **narrowband process** (e.g. a 2nd-order resonator driven by white noise, or a
JONSWAP-shaped sum-of-sinusoids with random phases), not a pure sinusoid — real swell is a spectrum
with groupiness, and the groupiness is what produces the infragravity band for free.

### 3.3 The DC / ratchet component — **yes, there is one, and it points seaward**

Direction (derived in §1.4, arithmetic there): **the tracker infers camera motion opposite to the
apparent feature flow. Foam flows shoreward ⇒ inferred camera translation is SEAWARD, against wave
travel.** Rendered consequence: world-anchored content **creeps toward the viewer and grows**, then
**snaps away seaward** on relocalisation.

Shape: **asymmetric sawtooth**, because the forcing is rectified (§1.5). Per wave cycle of period T:

```
  phase 1  (duration t_coherent ≈ 0.2-0.4 T):  ramp seaward at α_eff·c   [the bore sweep]
  phase 2  (remainder of T):                   exponential relaxation back toward 0,
                                               time constant tau_relax ≈ 0.3-1.0 T,
                                               recovering only 50-80% of phase 1
  => net DC drift per wave = (0.2-0.5) x d_pulse
```

**Net drift rate to simulate: 0.02–0.10 m per wave in the nominal regime = roughly 0.1–0.6 m/min at a
10 s wave period** **[D/E]**, until interrupted by a jump (§3.5). Cap the free-running accumulation at
~1–2 m; beyond that ARCore will have relocalised or declared tracking lost.

Make the relaxation fraction a parameter. It is the one number I have no measurement for, and it
single-handedly sets whether the drift is bounded (relaxation ≈ 1) or a runaway ramp (relaxation ≈ 0).

### 3.4 Axis anisotropy — strongly anisotropic, not isotropic

| Axis | Relative amplitude | Why |
|---|---|---|
| **ŝ (seaward/cross-shore)** | **1.0** | The coherent bore motion is cross-shore by definition. |
| **û (up)** | **0.3–1.0** | Two contributions: (a) the vertical-rise degeneracy of §1.4, which is *16.7× cheaper in norm at 25 m* and so absorbs a large share of the error; (b) real sea-surface heave, peak vertical surface velocity ≈ π·Hs/T = **0.35–0.67 m/s** for Hs 1–1.5 m at T 6–11 s **[D]**. Sign: co-phased *upward* with the shoreward sweep. |
| **l̂ (alongshore)** | **0.15–0.4** | Only from oblique wave approach and the longshore current. At Noronha's Cacimba do Padre the optimum swell angle is NNW onto a roughly N-facing beach, i.e. slightly oblique — so a persistent small longshore component is expected, with a *consistent sign* per beach. |

So: an **error ellipsoid roughly 1 : 0.5 : 0.25 aligned to (ŝ, û, l̂)**, world-fixed. **[E]**

A useful corollary: the anisotropy is a *detector*. Log ARCore's camera position while standing still
facing the surf; PCA the residual. If the dominant eigenvector is horizontal and points out to sea and
stays there while you pan, this model is right.

### 3.5 Relocalisation jumps

| Property | Value | Basis |
|---|---|---|
| Magnitude | **0.1–0.5 m** typical; **> 1 m** in ~10 % of events | **[M]** Here To Stay ranges (§1.7) |
| Duration | 1–3 frames (33–100 ms) — a step, not a ramp | **[E]** |
| Direction | Biased **anti-parallel to the accumulated ratchet** (i.e. seaward-correcting ⇒ content jumps away from viewer), plus an isotropic component of similar size | **[D]** it is a correction of the accumulated error |
| Rate | **1 per 5–30 s** under sustained corruption | **[E] — not published; the weakest number in this document** |
| Trigger | Correlate with accumulated ratchet crossing ~0.3–0.5 m | **[E]** |

### 3.6 Scale and rotation channels

- **Scale:** slow multiplicative random walk, **±2–10 % per minute**, correlated with the
  phantom-translation magnitude; worst while the user is perfectly still (§1.6). Upper reference:
  the 14.4 cm/m ≈ 14 % scaling error measured in a dynamic industrial environment **[M]**.
- **Rotation:** roll/pitch error **< 0.5°**, gravity-pinned. Yaw: baseline gyro-bias drift of
  **2.2 °/min (Pixel 7 Pro) to 22.7 °/min (OnePlus 7 Pro)**, derived from the measured biases of
  §1.3 **[D from M]** — a 10× device spread, so this is a sweep parameter too. Add **0.5–3 °/min** of
  water-induced bias signed by the longshore flow **[E]**, plus a wave-frequency yaw wobble of
  **0.05–0.3°** — not more; the gyro forbids it.

### 3.7 Reference parameter set (Noronha, Cacimba do Padre, surf season, nominal posture)

```yaml
frames:
  seaward_azimuth_deg: <beach normal, pointing out to sea>   # world-fixed
  # phantom translation is emitted in this frame, NOT the camera frame

forcing:
  groundswell:  { Tp_s: 14.0,  Hs_m: 2.2, weight: 1.0  }     # f = 0.0714 Hz  (Dec-Mar N swell)
  wind_sea:     { Tp_s: 9.0,   Hs_m: 1.2, weight: 0.35 }     # f = 0.111 Hz   (SE trades)
  swash:        { T_s: 22.0,              weight: 0.8  }     # f = 0.0455 Hz  (1-3x incident)
  group_envelope: { T_s: 110.0, depth: 0.6 }                 # f = 0.0091 Hz  AMPLITUDE MODULATION
  bore_celerity_mps: 4.0          # 2-5 m/s across the surf zone; 3 m/s for inner surf at 10-40 m
  coherent_phase_fraction: 0.25   # of the wave period

geometry:
  camera_height_m: 1.5
  water_range_near_m: 15.0        # nearest moving water in frame  <-- dominant knob
  water_range_far_m: 35.0
  static_range_m: 5.0             # dry sand in the lower frame
  n_water_features: 150
  n_static_features: 150
  # alpha = I_d/(I_s+I_d),  I = sum(n / Z^2)   -> ~5.6% here
  robust_rejection_factor: 0.2    # sustained alpha = 0.2 * alpha_instantaneous -> ~1.1%

phantom_translation:
  amplitude_pp_m: { seaward: 0.15, up: 0.07, alongshore: 0.04 }
  imu_accel_bias_mps2: 0.05       # SWEEP 0.018 (best phone) .. 0.206 (worst) -> ~17x drift spread
                                  # gives the 1/f^2 envelope; A_max = bias/(2 pi f)^2
  ratchet:
    relaxation_fraction: 0.65     # 1.0 = fully bounded, 0.0 = runaway
    net_drift_per_wave_m: 0.05
    accumulation_cap_m: 1.5

jumps:
  rate_hz: 0.07                   # ~1 per 14 s
  magnitude_m: { median: 0.22, p90: 0.55, p99: 1.4 }
  direction_bias: -1.0            # opposite the accumulated ratchet

scale:
  drift_per_minute_frac: 0.05
rotation:
  yaw_drift_deg_per_min: 2.5      # SWEEP 2.2 (Pixel 7 Pro) .. 22.7 (OnePlus 7 Pro) from measured gyro bias
  yaw_wobble_deg: 0.15
  roll_pitch_error_deg: 0.3
```

### 3.8 How to falsify this on the beach

Run these before trusting the simulator. Each one kills a specific assumption.

1. **Sign test.** Stand still, phone at the surf, anchored object placed. Does content creep *toward*
   you between snaps? → confirms §1.4. If it creeps away, **flip ŝ**.
2. **World-fixed vs camera-fixed test.** Log ARCore camera position for 2 min facing the surf, then
   2 min with the surf at 90° to the side. PCA the residual in *world* coords. Does the dominant
   eigenvector stay seaward? → confirms §3.0/§3.4.
3. **1/Z² test — the big one.** Compare (a) phone horizontal, distant surf, sand in lower frame vs
   (b) phone tilted down onto the swash 3 m away. The model predicts a **≈10–50× difference in drift**
   (α 1 % → 78 %). If the two are similar, the 1/Z² information weighting is wrong and the whole §1.2
   argument needs rebuilding.
4. **Frequency test.** FFT the logged position residual. Peaks should appear at 1/Tp and in the
   infragravity band, and **nothing above ~0.3 Hz** beyond baseline jitter. A flat or high-frequency
   spectrum falsifies §1.3.
5. **Occlusion test.** Cover the upper (sea) half of the lens with tape, keeping only sand in view.
   Drift should collapse. This is the cheapest possible proxy for the feature mask ARCore will not
   give you (§4.1) and is worth doing for its own sake.
6. **Dry-sand control.** Same posture, phone pointed inland at the dune. Whatever drift remains is
   ARCore's baseline and must be *subtracted* before attributing anything to the waves.

Log at minimum: `ARCamera` pose per frame, `ARSession.notTrackingReason`, point-cloud count +
confidence distribution, raw accel/gyro, and GNSS fixes — see §4.4 for what AR Foundation 6 exposes.

---
## 4. Mitigations — and what ARCore actually lets you touch

**The framing that matters most.** The corrupting content is *textured*, not featureless. So ARCore
will report `TrackingState.Tracking` / `NotTrackingReason.None` while its pose is wrong. **There is no
API that says "my pose is being corrupted by a dynamic scene."** The whole failure is silent. Every
mitigation below therefore starts from *you* detecting it.

### 4.1 What ARCore exposes — definitive answers

| # | Capability | Reachable from Unity + ARCore public API? |
|---|---|---|
| A1 | Feed a per-pixel / per-region **feature mask** | **No.** Nothing in the API surface. |
| A2 | **Read tracked feature points** (position + confidence + persistent ID) | **Yes**, fully wired in AR Foundation 6 — and this is your best lever. |
| A3 | Force **3DOF / rotation-only**, or disable the translation estimate | **Probably no**, but there is a one-line runtime test. ARKit *does* have it. |
| A4 | **Override / correct** ARCore's pose | **No.** Only `ARSession.Reset()` (nuclear) or shifting the XR Origin. |
| A5 | **Numeric tracking confidence** for the session | **No.** Only a discrete enum. Point-cloud and raw-depth confidence are the only continuous proxies. |
| A6 | **Geospatial / VPS** absolute pose | Yes technically; degrades to GPS-grade on an uncovered beach. |
| A7 | **Scene Semantics with a `WATER` label** | **Yes** — see §4.2. The one real gift. |

**A1 — feature masking: definitively no.** The complete `ArConfig` option set is `DepthMode`,
`LightEstimationMode`, `PlaneFindingMode`, `UpdateMode`, `TextureUpdateMode`, `CloudAnchorMode`,
`AugmentedFaceMode`, `InstantPlacementMode`, `GeospatialMode`, `SemanticMode`, `FocusMode`,
`ImageStabilizationMode`. **There is no region-of-interest, feature-mask, tracking-mask or exclusion API
in any module** — not `ArSession`, `ArFrame`, `ArCamera`, `ArConfig` or `ArImage` **[M]**
([ARCore C API reference index](https://developers.google.com/ar/reference/c)). ARCore's VIO is sealed:
images come *out*, no hint goes back *in*.

> Note the painful asymmetry for this project specifically: the app **already** runs a MobileCLIP
> sea-classifier on the AR frame every 5 s. You can compute a near-perfect water mask — **and there is
> nowhere to send it.** Any mask-*based tracking* fix requires replacing ARCore's VIO, not augmenting it.

**A2 — reading feature points: yes, and richer than expected.**
`PointCloud.getPoints()` → `FloatBuffer`, **4 floats per point: X, Y, Z, confidence**, in world space
consistent with that frame's camera pose; `getIds()` → IDs that are **persistent within a session**
("if a point from point cloud 1 has the same id as the point from point cloud 2, then it represents the
same point in space"); `getTimestamp()` in ns **[M]**
([PointCloud](https://developers.google.com/ar/reference/java/com/google/ar/core/PointCloud),
[ArPointCloud](https://developers.google.com/ar/reference/c/group/ar-point-cloud)).
In Unity: `ARPointCloud.positions` (`NativeSlice<Vector3>?`), `.identifiers` (`NativeSlice<ulong>?`),
`.confidenceValues` (`NativeArray<float>?`, **0..1** — guard with `.IsCreated`), plus an `updated`
event; enabled via `ARPointCloudManager` **[M]**
([ARPointCloud 6.0](https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.0/api/UnityEngine.XR.ARFoundation.ARPointCloud.html)).
Verified against this project's installed `com.unity.xr.arcore@6.3.4` / `arfoundation@6.3.4`: the ARCore
subsystem does extract the 4th component as confidence and widen the IDs to `ulong`, so all three are
genuinely populated on this platform.

Two caveats: (i) **Google never documents a 1:1 correspondence with the VIO's internal feature set**,
and there is **no per-point inlier/outlier flag** — treat it as an observable *correlated* with the
tracker; (ii) confidence was historically **stuck at zero**
([arcore-unity-sdk#159](https://github.com/google-ar/arcore-unity-sdk/issues/159), never visibly
resolved before the repo was archived) — **verify non-zero confidence on the actual device before
building logic on it.**

> **Why A2 is the single best lever available.** Persistent IDs + world positions let you compute, per
> frame, **the apparent 3D velocity of every tracked point yourself**. Points on water move coherently
> shoreward; points on sand/rock/vegetation do not. That is a dynamic-scene detector built entirely on
> public API, on a few hundred points, at essentially zero cost. You cannot stop ARCore using those
> points — but you can *know* that it is.

**A3 — rotation-only: ARKit yes, ARCore almost certainly no, and there is a one-line test.**
ARKit has `AROrientationTrackingConfiguration`, genuine 3DOF, which explicitly "cannot track movement
of the device" **[M]**
([Apple](https://developer.apple.com/documentation/arkit/arorientationtrackingconfiguration)).
ARCore has no `ArConfig` equivalent; the feature request
[arcore-unity-sdk#282 "Rotation Only Mode"](https://github.com/google-ar/arcore-unity-sdk/issues/282)
(Jun 2018) got **no maintainer response** before the repo was archived. Its author's note is directly
relevant: merely *ignoring* ARCore's position does not work either, because "if you point an ARCore
device up at the sky or a blank wall, camera tracking **freezes** instead of continuing to track
rotation."

**The Unity trap, and the test.** AR Foundation *does* have `TrackingMode.RotationOnly` and
`ARSession.requestedTrackingMode` — but that enum is platform-agnostic. In
`ARCoreSessionSubsystem.cs` the setter just forwards `Feature.RotationOnly` to native, and Unity's
ARCore docs nowhere claim ARCore honours it. Crucially `currentTrackingMode` is read back *from native*:

```csharp
public override Feature currentTrackingMode => NativeApi.GetCurrentTrackingMode();
```

> **Do this on device, first, before designing anything around it:** set
> `requestedTrackingMode = TrackingMode.RotationOnly`, wait a frame, read `currentTrackingMode`.
> If it comes back `PositionAndRotation`, ARCore ignored you. One line, definitive, cheaper than any
> amount of doc archaeology. (`RotationOnly` is known-good on ARKit.)

**A4 — pose override / reset / relocalise.**
- **Override: no.** The camera transform is owned by `TrackedPoseDriver` and rewritten every frame.
  Shifting the **XR Origin** is the only route — and it is the officially blessed one: AR Foundation
  ships `XROrigin.MakeContentAppearAt` precisely to "update the Transform to make content appear at a
  given position and rotation … useful when the content itself cannot be moved at runtime" **[M]**.
  *This project's `SpuriousMotionGate` already does exactly this, which is correct.*
- **`ARSession.Reset()`: exists, nuclear, useless here.** Docs: "Resetting the session destroys all
  trackables and resets device tracking (for example, the position of the session is reset to the
  origin)" **[M]** ([ARSession 6.0](https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.0/api/UnityEngine.XR.ARFoundation.ARSession.html)).
  Community reports also note it disabling camera autofocus. It throws away your content placement.
- **Relocalise on demand: no API.** `NotTrackingReason.Relocalizing` is *reported*, never *requested*.
- **Anchors do not help.** `ARAnchor` is ARCore-managed and moves *with* ARCore's corrections, so a
  drifting session drifts your anchors too. Re-anchoring means destroy + create, which visibly pops.

### 4.2 The one real gift: **ARCore Scene Semantics has a `WATER` label**

Most actionable finding in this document, and reachable from Unity today.

- `SemanticLabel` includes **`WATER`** — *"Pixels of ground surfaces covered by water, including lakes,
  rivers, etc."* — alongside `SKY`, `TERRAIN`, `BUILDING`, `TREE`, `ROAD`, `SIDEWALK`, `STRUCTURE`,
  `VEHICLE`, `PERSON`, `OBJECT`, `UNLABELED` **[M]**
  ([SemanticLabel](https://developers.google.com/ar/reference/java/com/google/ar/core/SemanticLabel),
  [Scene Semantics API](https://developers.google.com/ar/develop/scene-semantics)).
- **Unity surface** (`ARCoreExtensions`, `ArSemanticManager`) **[M]**
  ([Scene Semantics on AR Foundation](https://developers.google.com/ar/develop/unity-arf/scene-semantics)):
  - `TryGetSemanticTexture(out Texture2D)` → `R8`, one label per pixel
  - `TryGetSemanticConfidenceTexture(out Texture2D)` → `Alpha8`, 0–255
  - **`GetSemanticLabelFraction(SemanticLabel label)` → float 0..1** — "more efficient than pixel-wise
    searching". **A one-call, per-frame "how much of the frame is water" number.**
  - Enable: `ARCoreExtensionsConfig` → Semantics Mode = Enabled. Available ~1–3 frames into the session.
- **Costs and caveats, all documented [M]:**
  - **Not all ARCore devices support it** — same device list as the Depth API
    ([supported devices](https://developers.google.com/ar/devices)). Must be feature-detected.
  - **Outdoor only**, and **portrait orientation** — "the quality of semantic labels is not guaranteed
    for landscape mode". Fine for this app's portrait shell; it hard-blocks a landscape AR mode.
  - **Unverified for surf [E].** The label's own definition says *ground surfaces covered by water …
    lakes, rivers* — inland water. Whether it fires reliably on breaking ocean surf, foam and **wet
    sand** must be measured. Wet sand in the swash zone is both the ambiguous case *and* the case that
    matters most (§1.2, §2.6).
  - Known bug: the semantic texture arriving **distorted while the device is moving or rotating**
    ([arcore-unity-extensions#197](https://github.com/google-ar/arcore-unity-extensions/issues/197)) —
    harmless for a stationary user, a hazard otherwise.

**What to do with it, given you cannot mask.** Use it as a **gate and a confidence input**:
`GetSemanticLabelFraction(WATER)` above a threshold ⇒ raise the stillness gate's authority, freeze or
damp drift correction, suppress new anchor placement, switch to rotation-only rendering (§4.3), and tell
the user. It converts an un-maskable corruption into a **detectable** one. That is the whole game.

### 4.3 Literature mitigations, with real costs

**Semantic mask-based dynamic feature rejection** — the mainstream academic fix. Numbers **[M]**:

| System | Approach | Accuracy | Cost |
|---|---|---|---|
| **DynaSLAM** (RA-L 2018) | Mask R-CNN + multi-view geometry + inpainting | TUM fr3/walking_xyz ATE RMSE **0.459 → 0.015 m (~97 %)** | **~436–500 ms/frame *with* GPU**; region-growing "up to a few seconds". Not real-time. |
| **DS-SLAM** | SegNet + moving-consistency check | fr3/w/xyz ATE RMSE **0.0247 m** | **59.4 ms/frame with GPU** |
| **SP-SLAM** (Electronics 2024, USV nearshore) | Stereo + distance-segmentation of unreliable-depth regions | ATE RMSE vs ORB-SLAM2: 8.03→3.73, 57.8→3.01, 31.3→3.02, **99.2→3.88**, 35.8→2.10, 31.9→2.71 (**54–96 %**); RPE-trans up to **98.7 %** better | i7-12700: 35.9 ms tracking + 56.9 ms mapping. **Jetson Nano: 167 ms ≈ 8 FPS** |
| **JMSE 13(4):679** (USV nearshore) | **Otsu on HSV H-channel → mask out water *and* sky entirely**, then YOLOv8n-seg + epipolar motion-consistency | TUM dynamic ATE RMSE: w_xyz **0.014** (beats DynaSLAM 0.015 and DS-SLAM 0.025), w_static 0.007, w_rpy 0.038 | **< 100 ms/frame** — but on an **RTX 4090D** |
| **WS-SLAM** (2025) | ORB-SLAM3 + PP-LiteSeg thread masking **water-surface** features, then epipolar culling | **~43 % better positioning accuracy than ORB-SLAM3** ⚠️ *abstract only — full text bot-blocked, tables never seen; treat as unverified* | not obtained |
| **IV-SLAM** (CoRL 2020) | Learned **per-pixel reprojection-noise model** down-weights unreliable regions; **learned to reject surface reflections**, shadows, lens flare, pedestrians | **> 70 % increase in mean distance between failures**; **−35 % translation error** on real robot data | per-frame ms **not published** |

**Two load-bearing caveats for this project:**
1. **All the water-specific papers are boat-mounted, looking across water at a shore. Your geometry is
   inverted** — phone on sand looking *at* the surf, where water may fill most of the frame. The masking
   *concept* transfers; the papers' assumption that **useful static structure remains after masking**
   may not. DynaVINS measured DynaSLAM **diverging** under "overall occlusion … leading to the failure
   of the semantic segmentation module and the absence of features from static objects" **[M]**. **If
   surf fills the frame, masking it leaves nothing.**
2. **Cost rules semantic masking out on a phone.** The cheapest semantic option here is 36–60 ms/frame
   on a desktop i7, or 167 ms on a Jetson Nano. Sharing a phone GPU with URP and the AR camera pass, at
   frame rate, this is not viable. This project's 5 s MobileCLIP cadence is about what a phone can
   spare — which is why semantics belongs in the **detector**, not the tracker.

**Geometric / non-semantic rejection — the cheap family, and the one that actually fits a phone.**

**RD-VIO is the most directly relevant paper in this whole report** — "Robust VIO for **Mobile
Augmented Reality** in Dynamic Environments", benchmarked head-to-head against ARKit/ARCore **[M]**
([arXiv:2310.15072](https://arxiv.org/html/2310.15072v3)):
- **IMU-PARSAC**: two-stage, IMU-prior-gated outlier detection. Stage 1 matches 3D landmarks to 2D
  keypoints *using the IMU pose prediction* and collects error statistics; stage 2 applies a
  **dynamically thresholded** 2D-2D match using those statistics to identify moving objects. Plus
  deferred triangulation and "subframes" for pure rotation.
- **Accuracy:** EuRoC average RMSE **0.136 m**. Online head-to-head: **dynamic scene RD-VIO 19.6 mm APE
  vs ARKit 33.3 mm**; static scene RD-VIO 22.4 mm vs **ARKit 15.6 mm**. Note what that second pair
  means: **ARKit's own error roughly doubles (15.6 → 33.3 mm) just from pedestrians walking through an
  indoor scene.** Surf is far worse than pedestrians.
- **Cost — the striking number: IMU-PARSAC is 1.07 ms/frame.** Keypoint tracking 7.47 ms; **total
  18.38 ms/frame** vs VINS-Mono's 44.72 ms (i7-7700). **Runs real-time on an iPhone X at 30 Hz video /
  100 Hz IMU, 640×480** — the same VGA/30 Hz budget ARCore uses (§1.1).

Also in this family: **DynaVINS** (RA-L 2022) — robust BA weighting features against an
IMU-preintegration prior, no semantics: VIODE parking_lot *high*, stereo-inertial, VINS-Fusion
**0.2780 → 0.0416 m (85 %)**; degradation rate r_d = ATE_high/ATE_none **0.86–0.93 vs VINS-Fusion's
1.51–2.61**; cost: average BA **30.9 → 53.0 ms (+72 %)** **[M]** ([arXiv:2208.11500](https://arxiv.org/pdf/2208.11500)).
**2-point RANSAC with a gravity/IMU prior** (Troiani et al., ICRA 2014) reports **~9× faster** outlier
rejection by using the IMU to shrink the minimal sample **[M]**. Epipolar-residual gating is the
non-semantic *second* stage in both water papers.

> **The one design principle to take from all of this: the IMU prior must be the arbiter, not the image
> majority.** RANSAC/epipolar consensus assumes the dominant motion model is the static world; when surf
> fills the frame the consensus set *is* the water and the rejected "outliers" are the sand (§1.2). That
> is exactly why RD-VIO gates on the IMU prior. **This project's `StillnessDetector` +
> `SpuriousMotionGate` is already an (aggressive, binary) instance of that principle** — the literature
> says the instinct is right; what is missing is graded weighting instead of an on/off gate.

**Graceful degradation under visual corruption.** The standard pattern, stated plainly by RISE-VIO:
"when a sufficient number of valid long-track correspondences are available, the visual update is
activated; **otherwise the visual update is skipped and the estimator continues with IMU-driven
propagation**" — with the honest limit that "under prolonged visual degradation, the estimator
necessarily becomes more inertial-dominated and may accumulate drift" **[M]**
([RISE-VIO](https://pmc.ncbi.nlm.nih.gov/articles/PMC13120272/)).
A relevant asymmetry from the subterranean benchmark: **OpenVINS shows "acute vulnerability to inertial
perturbations, particularly accelerometer bias, despite showing significant stability against visual
parameter variances"** **[M]**. Translation: MSCKF-family filters (ARCore's family) tolerate visual
*noise* well **precisely because they trust vision** — and corrupted-but-confident vision is the one
input they have no defence against.

**Introspective perception** — the literature you asked about, and it names your failure:
**IV-SLAM** learns a context-aware non-i.i.d. reprojection-error model and steers feature extraction
away from unreliable regions; its qualitative results explicitly name **"surface reflections"** as a
learned failure source — the closest thing in print to "water fools my tracker" **[M]**
([arXiv:2008.02760](https://arxiv.org/pdf/2008.02760)). The general theory is in
[Introspective Perception for Mobile Robots](https://arxiv.org/abs/2306.16698): learning perception
error distributions "in an **autonomously supervised** manner" from consistency constraints. **§4.2's
water-fraction gate is a crude, zero-training instance of exactly this.**

**IMU-only coasting — how long can you ignore vision?** From the measured phone biases (§1.3), position
error from double integration **[D from M]**:

| Elapsed | accel-bias term, best (0.018 m/s²) | worst (0.206) | gyro-gravity-leak term, best (0.000637 rad/s) | worst (0.006614) |
|---|---|---|---|---|
| 1 s | 0.9 cm | 10 cm | 0.001 m | 0.011 m |
| 10 s | 0.9 m | 10.3 m | **1.0 m** | **10.8 m** |
| 60 s | 32 m | 371 m | **225 m** | **2336 m** |

*(½·b·t² for accel bias; ⅙·g·ω·t³ for gravity leaking through gyro-induced tilt, which dominates beyond
~2 s.)* These reproduce the source paper's own headline — **"in less than 100 s, a pure inertial
propagation … would result in an error of larger than 1 km"** **[M]**
([*Sensors* 23:7609](https://pmc.ncbi.nlm.nih.gov/articles/PMC10490716/)).

> **Operational conclusion: raw IMU-only position holds for ~1–3 s and is worthless by 10 s.** Any "coast
> through the wave on the IMU" plan must be budgeted in **single-digit seconds** — which, conveniently,
> is about the duration of one bore sweep (§1.5). Coasting one wave is feasible; coasting a minute is not.
> Beyond a few seconds you must drop double integration and switch to **step-counted PDR** (§5), a
> fundamentally better-conditioned estimator.

**Absolute-position aiding.**

| Configuration | Horizontal accuracy | Note |
|---|---|---|
| Single-frequency, single-constellation SPP | ~6 m RMS | |
| Single-frequency multi-GNSS SPP | ~4 m RMS (Xiaomi Mi 8: 4.14 m) | |
| **Dual-frequency L5/E5, open sky, standalone** | **~1.22 m RMS** | L5/E5 code "characterized with noticeably lower noise", "less prone to distortions from multipath" **[M]** ([Remote Sensing 12:744](https://doi.org/10.3390/rs12040744)) |
| Static + PPP post-processing | decimetre | not real-time |

A beach with open sky is near the best case in these studies. `Location.getAccuracy()` is the
**68 % horizontal confidence radius**, the same convention as `GeospatialPose.getHorizontalAccuracy()`,
so the two are directly comparable **[M]**. ⚠️ **Update rate:** no authoritative Android figure found;
1–2 Hz is the working assumption and matches this project's `GnssProvider` (~2 Hz native `LocationManager`).

> **The mismatch you must accept:** GNSS at 1–4 m cannot correct a 0.05–0.3 m phantom oscillation. It
> bounds **minutes-scale** drift only. `GpsArKalmanFusion` is the right tool for the wrong timescale here
> — do not expect it to fix §3.

**ARCore Geospatial API / VPS.** `GeospatialPose` exposes `getHorizontalAccuracy()`,
`getVerticalAccuracy()`, `getOrientationYawAccuracy()`, all documented as the **"radius of the 68th
percentile confidence level"** **[M]**
([GeospatialPose](https://developers.google.com/ar/reference/java/com/google/ar/core/GeospatialPose)).
Published accuracy: "positional accuracy typically better than 5 meters and often around 1 meter, and a
rotational accuracy of better than 5 degrees", built on "tens of billions of images in Street View",
coverage in "over 87 countries", pose in under a second **[M]**
([Google Developers Blog](https://developers.googleblog.com/en/make-the-world-your-canvas-with-the-arcore-geospatial-api/)).
Free; quotas 1,000 sessions/min and 100,000 requests/min. Unity: ARCore Extensions → Optional Features
→ Geospatial.

> **Would it work on a Noronha beach? Essentially no, for the part you'd want.** Docs are explicit that
> it *degrades* rather than fails: "The Geospatial API can also be used in areas that do not have VPS
> coverage", and "in outdoor environments with few or no overhead obstructions, GPS may be sufficient".
> With no Street View over the sand, **Geospatial becomes a wrapper around GNSS + compass** — position ≈
> your existing fusion, and the **heading** (the thing a drift correction most needs, and which
> `GpsArKalmanFusion` currently re-derives from the GPS track every 8 m) stays compass-grade.
> **Call `AREarthManager.CheckVpsAvailabilityAsync()` at the beach first — that is the authoritative,
> free, one-call answer.** Caveat flagged but unread:
> [arcore-unity-extensions#211](https://github.com/google-ar/arcore-unity-extensions/issues/211)
> reports Geospatial returning optimistically high accuracy. **Streetscape Geometry** needs Depth support
> *and* VPS coverage, and there are no buildings to occlude — irrelevant here.

**ZUPT / stillness gating.** In VIO specifically: "for EKF-based VIO algorithms without static scene
detection … IMU measurement errors might lead to biased propagation and result in divergence"; ZUPT is
modelled as a **measurement update** so it corrects the whole state through the covariance **[M]**
([Lightweight hybrid VIO with closed-form ZUPT](https://www.sciencedirect.com/science/article/pii/S1000936120301722)).
Handheld stationarity detection via a rolling Dickey–Fuller test on a 250 ms accelerometer window
**[M]** ([arXiv:1703.00154](https://arxiv.org/pdf/1703.00154)).

> **This project already has it** — but note the structural limit: `SpuriousMotionGate` is a ZUPT applied
> **outside** the filter, so it cannot correct ARCore's internal covariance. Error keeps accumulating
> *inside* ARCore and resurfaces as relocalisation jumps (§1.7, §3.5). Counter-shifting the XR Origin
> hides the symptom; it does not stop the accumulation.

**Rotation-only rendering (app-side) — the pragmatic escape hatch.** You cannot make *ARCore* 3DOF
(A3), but you can discard its translation in your own render transform. For content 15 m out in the
water, translation parallax over a ±0.5 m user sway is nearly invisible, so this costs almost nothing
and immunises you completely against §3. **Strongly consider this as the fallback when the
water-fraction gate trips.** Caveat from A3: if the phone sees *only* water/sky, ARCore may freeze
tracking altogether rather than keep reporting rotation — verify.

### 4.4 Degraded-tracking detection available in AR Foundation 6

| API | Values | Continuous? |
|---|---|---|
| `ARSession.state` (`ARSessionState`) | None, Unsupported, CheckingAvailability, NeedsInstall, Installing, Ready, SessionInitializing, SessionTracking | No |
| **`ARSession.notTrackingReason`** (`NotTrackingReason`) | `None`, `Initializing`, `Relocalizing`, `ExcessiveMotion`, `InsufficientFeatures`, `InsufficientLight`, `CameraUnavailable`, `Unsupported` | No |
| `TrackingState` on trackables | None, Limited, Tracking | No |
| ARCore `TrackingFailureReason` | `NONE`, `BAD_STATE`, `CAMERA_UNAVAILABLE`, `EXCESSIVE_MOTION`, `INSUFFICIENT_FEATURES`, `INSUFFICIENT_LIGHT` | No |
| **Numeric session confidence** | — | **Does not exist** |
| `ARPointCloud.confidenceValues` | 0..1 per point | **Yes** (proxy) |
| Raw Depth confidence image | grayscale, white = full confidence | **Yes** (proxy, Depth-capable devices only) |
| **`GetSemanticLabelFraction(SemanticLabel.Water)`** | 0..1 | **Yes** — the direct signal (§4.2) |

**The trap, stated precisely.** `INSUFFICIENT_FEATURES` is documented as "Motion tracking lost due to
insufficient visual features. Ask the user to move to a different area and to avoid blank walls and
surfaces without detail"; `EXCESSIVE_MOTION` as "Motion tracking lost due to excessive motion" **[M]**
([TrackingFailureReason](https://developers.google.com/ar/reference/java/com/google/ar/core/TrackingFailureReason)).
**There is no `DYNAMIC_SCENE` or `UNRELIABLE_FEATURES` value.** Textured surf satisfies "sufficient
features"; a standing user is not "excessive motion". **Expect `notTrackingReason == None` throughout
the corruption.** (There is a standing complaint that ARCore reports `TRACKING` too often —
[arcore-android-sdk#528](https://github.com/google-ar/arcore-android-sdk/issues/528).) Also note the
raw-depth confidence docs say "surfaces with no texture usually yield a confidence of zero" — that is a
*stereo-from-motion* confidence, and **the docs never claim low confidence on reflective or moving
surfaces**, so do not assume water reads as low-confidence.

**Therefore build your own detector.** In rough order of cost-effectiveness:

1. **IMU-says-still vs pose-says-moving** — free, and the strongest single signal:
   ```
   if (stillnessDetector.IsStill && arCameraDisplacementOverLastSecond > threshold)
       -> the pose is lying.
   ```
   This project already computes both halves; the finding is that you should **drive a UI/behaviour
   change off it**, not only silently counter-shift.
2. **Point-cloud coherence (A2)** — per-ID apparent 3D velocity; a coherent shoreward field means water
   is driving the tracker. Cheap, public API, no model.
3. **Point-cloud churn** — ID turnover rate. High churn with high contrast ⇒ glitter (§1.2).
4. **`GetSemanticLabelFraction(WATER)`** — one call, but device-gated and unverified for surf.
5. **Spectral test** — band-power of the pose residual in 0.03–0.2 Hz vs above 0.5 Hz (§3.8 test 4).

---

## 5. PDR specifics

### 5.1 Step detection — and yes, there is sand data

*Pedometer Accuracy during Walking over Different Surfaces* (Med Sci Sports Exerc 2007), n = 52, six
150-m trials per surface, hip-worn YAMAX SW-700 **[M]**
([PubMed](https://pubmed.ncbi.nlm.nih.gov/17909414/)):

| Surface | Self-selected speed | Pedometer relative error |
|---|---|---|
| Concrete | 5.6 ± 0.5 km/h | baseline |
| Grass | 5.6 ± 0.5 km/h | baseline |
| Wet beach sand | 5.4 ± 0.4 km/h | — |
| **Dry beach sand** | **5.0 ± 0.5 km/h** | **females 4.46 ± 5.72 %; males 1.63 ± 3.57 %** |

Dry sand "significantly reduced walking speed and **increased the number of steps taken and
registered**", with a significant sex difference "possibly by exacerbating hip and walking movements."

⚠️ **That is a mechanical hip pedometer, not a smartphone algorithm. I found no study of smartphone
accelerometer step detection on sand — a genuine gap.**

Smartphone baseline for comparison **[M]**: **0.6 % error at normal cadence, 2.3 % fast, 5.5 % slow**;
good algorithms claim > 98.6 % across step modes and device poses. Note the compounding problem:
**slow walking is already the worst case, and sand forces slower walking.**

**The real warning comes from gait science, not pedometry.** *Quantitative characterization of walking
on sand in ecological conditions* (Gait & Posture) **[M]**: on sand, "self-selected speed and
**short-term stride variability increase**"; on wet sand "stance and double support **do not decrease
with increasing speed**", unlike normal gait; loose sand "can **double** the metabolic cost", with a
positive linear relationship between footprint depth and energetic cost.

> Increased **stride variability** is exactly what defeats fixed-threshold peak detection — and worse, it
> breaks the *assumption* behind every Weinberg-family step-length model (a stable mapping from
> vertical-acceleration amplitude to stride length). On sand the foot sinks, the acceleration peak is
> damped and smeared, and stride length shortens *while* amplitude changes in an uncalibrated way.
> **Expect step-LENGTH error on sand to degrade considerably more than step-COUNT error. Nobody has
> published that number.**

### 5.2 Step-length estimation error

| Model class | Reported error | Note |
|---|---|---|
| **Weinberg** (Δ of vertical accel max−min per step) | **2.48 %** in one study; **8.03 m** absolute over a trajectory in another | protocols differ |
| **Ladetto** | **1.95 %** | same study as Weinberg 2.48 % |
| **Kim** (mean accel within a step) | **7.04 m** absolute | same study as Weinberg 8.03 m |
| **Scarlett** | ⚠️ **no number found** | gap |
| Generic smartphone tests | ~**1 %** stride-length error | |
| **Personalised / adaptive** | **0.64 m** absolute (vs Weinberg 8.03, Kim 7.04, same experiment) — **~12×** better | strongest comparison found |
| **ML (LSTM + denoising autoencoder)** | ⚠️ **no error figure obtained** | gap — the ML numbers found are *gait-mode classification* accuracy (KNN 93.4 %, SVM 92.4 %, DTree 76.6 %), not length error |

⚠️ These come from different papers with different tracks, speeds and mountings; the columns are **not**
strictly comparable. The robust conclusion is the **ordering**: fixed/flat < Weinberg/Kim/Ladetto <
per-user calibrated, and per-user calibration is worth roughly an order of magnitude.

> **For a beach app with repeat local users (guides, researchers), a one-time walk-a-known-distance
> calibration is the single highest-leverage PDR investment available.**

### 5.3 Heading drift

**(a) Gyro-only integration** — derived from the measured phone gyro biases of §1.3 **[D from M]**:

| Phone | Gyro bias | Heading drift @ 1 min | @ 5 min |
|---|---|---|---|
| Pixel 7 Pro (best) | 0.000637 rad/s = 0.036 °/s | **~2.2°** | **~11°** |
| OnePlus 7 Pro (worst) | 0.006614 rad/s = 0.379 °/s | **~23°** | **~114°** |

Corroborating published points **[M]**: a bias of 0.01 rad/s "produces a deviation of **34° after one
minute**"; a well-compensated long run drifted "almost **150° in 10 hours**" (≈ 0.25 °/min); a 12-hour
run with "no thermal compensation" still kept max orientation error "**below 8 degrees**". **The spread
is ~0.25 °/min to ~34 °/min and is entirely bias-calibration-dependent** — which is why no single
authoritative per-phone figure exists, and why you should **measure it on your own target devices.**

**(b) Gyro + magnetometer fusion** — bounds the error instead of letting it grow, but plants a standing
bias **[M]**:

| Result | Error |
|---|---|
| Complementary filter, indoor, with disturbance | **15.8°** mean, σ 14.0° |
| Magnetometer only, same study | 15.1° |
| Gyro only, same study | 21.9° |
| Robust adaptive Kalman PDR | **80 % probability heading error < 4°** |
| One PDR heading algorithm | RMSE **17.4°** |
| Single-step heading absolute error, indoor | **2.19°** and **1.58°** |

**(c) Under magnetic disturbance:** filters "can avoid the impact of magnetic disturbances on roll and
pitch … [but] heading is still seriously influenced" **[M]**. Named sources: indoor concrete rebar and
electronic equipment.

**Outdoors vs indoors:** a beach is near the magnetometer's best case — no rebar, no machinery, clean
geomagnetic field. Residual risks are device-local: magnets in cases/mounts, and the need for figure-8
calibration. ⚠️ **I found no clean outdoor-vs-indoor heading-error pair for a phone**; the fusion numbers
above are predominantly indoor, so treat them as **pessimistic** for this site.

**Android's sensor fusion — the choice you actually face [M]**
([Position sensors](https://developer.android.com/develop/sensors-and-location/sensors/sensors_position)):
- `TYPE_ROTATION_VECTOR` — accel + gyro + **magnetometer**. Absolute, Y to magnetic north, no unbounded
  yaw drift, but jumps under magnetic disturbance.
- `TYPE_GAME_ROTATION_VECTOR` — "identical … except it **does not use the geomagnetic field**". "The Y
  axis does not point north … and **that reference is allowed to drift by the same order of magnitude as
  the gyroscope drifts around the Z axis**." Upside: "relative rotations are more accurate, and not
  impacted by magnetic field changes."
- `TYPE_GEOMAGNETIC_ROTATION_VECTOR` — accel + magnetometer, no gyro: low power, noisier, no drift.

> **The trade in one line: game-rotation-vector is smooth but yaw-drifts; rotation-vector is absolute but
> noisy.** The standard resolution — run the game vector for short-horizon smoothness and correct its yaw
> slowly against an absolute reference — is **already available in this project in a better form than a
> magnetometer**: `GpsArKalmanFusion` computes the azimuth from the GPS track every 8 m walked, and
> outdoors while walking that beats a magnetometer.

### 5.4 Overall PDR position error growth

| Reported **[M]** | Value |
|---|---|
| General envelope | "almost always **below 5 %**" of distance travelled |
| Handheld average relative precision | ~**2.5 %** |
| Walking-distance error, calibrated | **2.62 %** |
| Relative distance error by carrying mode (texting/calling/swinging) | **0.66–1.14 %** |
| 159.2 m trajectory, MAE | **0.98–1.29 m** (0.6–0.8 %) |
| 164 m trajectory, mixed states/poses | ~**3.5 m** (2.1 %) |
| 108 m walk, integrated system | within **0.5 m** |
| Gyro-based PDR at checkpoints | RMSE ~**2.22 m** |

**Metres per minute [D]** — the papers report % of distance, not per-time. At ~1.3 m/s ≈ 78 m/min, a
2–5 % rate gives **≈ 1.5–4 m of position error per minute of walking**, growing with distance covered
rather than elapsed time. On sand — slower (5.0 vs 5.6 km/h ⇒ ~74 m/min) with higher stride variability
— expect the upper end or worse: **3–6 m/min [E, unmeasured]**.

> **The comparison that matters for design.** PDR at 2–5 % of distance is **two to three orders of
> magnitude better than raw IMU double integration** (§4.3: 1–11 m at 10 s, 225–2336 m at 60 s).
> **Standing still, PDR contributes exactly zero error by construction** — the ideal complement to a
> stillness-gated AR session. And GNSS at 1–4 m bounds PDR's accumulation indefinitely. **Stillness gate
> + step-counted PDR + GNSS bound have no dependence whatsoever on what the camera is looking at** —
> which is the only architecture in this document that is structurally immune to §1.

---

## 6. What I could not find — be suspicious of these

Ranked by how much the simulator depends on them.

| # | Gap | Impact on the model | How to settle it |
|---|---|---|---|
| 1 | **No measurement of ARCore/ARKit VIO pointed at surf, anywhere.** Google has published nothing about water, waves or dynamic scenes; the docs address only *featureless* surfaces. | **The whole of §3 is a derived hypothesis.** | §3.8 device tests. This is the headline gap. |
| 2 | **Relocalisation jump *rate*** under sustained corruption. | §3.5's `rate_hz` is pure judgement. | Log pose discontinuities on the beach for 10 min. |
| 3 | **Ratchet relaxation fraction** (§3.3) — how much of each wave's phantom ramp is recovered. | Single-handedly decides bounded drift vs runaway ramp. | Fit to the logged residual's autocorrelation. |
| 4 | **Surf-zone foam-sheet persistence.** Callaghan's 1.4–4.8 s is *open-ocean whitecaps*; a sand- and organics-stabilised beach foam sheet plainly lives far longer and **no quantitative study exists**. | Longer lifetime ⇒ longer coherent tracks ⇒ worse corruption. Free parameter, 5–60 s. | Video the surf, track foam patches. Trivially doable. |
| 5 | **Whether ARCore's `WATER` semantic label fires on ocean surf / foam / wet sand** — the label is defined for "lakes, rivers". | §4.2's whole mitigation. | One device test with `GetSemanticLabelFraction`. |
| 6 | **Whether ARCore honours `TrackingMode.RotationOnly`.** | §4.1 A3. | The one-line `currentTrackingMode` read-back. |
| 7 | **Absolute surf-zone celerity in m/s for a tropical beach break.** Literature gives ratios or tank-scale cm/s only; §2.3's table is derived. | Sets the forcing amplitude. | Video timestack from the beach. |
| 8 | **Smartphone step detection on sand** — only a 2007 hip-worn mechanical pedometer study exists. | §5.1. | Measure with the app. |
| 9 | **Step-length error on sand** for any model family; **Scarlett** model % error; **ML step-length** error figures. | §5.2. | Measure. |
| 10 | **Optical decorrelation time of sun glitter.** The glitter literature treats the surface as quasi-frozen between looks; the 10–100 ms figure is a **radar**-coherence analogue for cm-scale roughness, not an optical measurement. | §1.2's glitter channel. | High-frame-rate video. |
| 11 | **WS-SLAM's actual ATE tables and runtime.** Taylor & Francis bot-blocks HTML, PDF and text proxies. The "~43 %" is from an abstract summary and is **unverified**. | §4.3 (informational only). | Institutional access. |
| 12 | **IV-SLAM's per-frame ms cost / hardware.** Not stated in the paper. | §4.3 cost column. | — |
| 13 | **Authoritative Android GNSS fix-rate figure.** Only request-interval code references found. | §4.3. 1–2 Hz assumed. | Measure via `GnssProvider`. |
| 14 | **Clean outdoor-vs-indoor phone heading-error pair.** All fusion numbers found are indoor. | §5.3 — makes those numbers pessimistic. | Measure. |
| 15 | **Any Google statement that the exposed `PointCloud` IS the VIO tracker's internal feature set**, and any per-point inlier flag. | §4.1 A2 — the detector is a correlate, not ground truth. | — |
| 16 | **Noronha-specific buoy/swash measurements.** The wave-climate numbers come from mainland E/SE-facing points (PNE, Recife buoy) and a 0.5° WW3 grid; there are **no swash measurements for Cacimba or Sueste**. | §2 period/height bands transfer; occurrence frequencies do not. | — |

**Places I am extrapolating rather than citing — read these as tunables, not facts:** the α
information-weighting model and every α value (§1.2); all four amplitude regimes (§3.1); every relative
amplitude and the axis anisotropy ratios (§3.2, §3.4); the ratchet shape and net drift rate (§3.3); all
of §3.5 except the magnitudes; the scale-drift rate (§3.6); the water-induced yaw bias (§1.8, §3.6);
Cacimba/Sueste beach-state classification and their swash periods (§2.4); sand PDR degradation (§5.1,
§5.4).

---

## 7. The five things to do first

1. **Run the §3.8 sign test and the §3.8 1/Z² test.** Between them they validate or destroy the two
   central claims (seaward direction, near-water dominance) in half an hour on the beach, and they need
   no simulator at all.
2. **Read `currentTrackingMode` after requesting `RotationOnly`** (§4.1 A3). One line. If ARCore honours
   it, a whole class of problem disappears. If not, implement **app-side rotation-only rendering**
   (§4.3) as the fallback — for content 15 m out it costs almost nothing.
3. **Check `GetSemanticLabelFraction(SemanticLabel.Water)` on a target device, at the beach** (§4.2). It
   is the only purpose-built water signal in the entire ARCore API, and whether it works on surf is a
   ten-minute empirical question.
4. **Build the free detector:** `IsStill && poseMovedMoreThanThreshold` ⇒ drive behaviour, not just the
   silent counter-shift (§4.4). Add point-cloud per-ID velocity coherence (§4.1 A2) when you want a
   second opinion.
5. **Log a 10-minute stationary session facing the surf**, plus a dry-sand control pointed inland, and
   FFT both. That single dataset settles gaps 1, 2 and 3 in §6 — the three the simulator most depends
   on — and turns §3 from a hypothesis into a calibrated model.

---

*Compiled 2026-09-28. Every number is tagged **[M]** measured/cited, **[D]** derived with arithmetic
shown, or **[E]** extrapolated. §6 is the honest list of what is missing; §3.8 is how to falsify §3.*
