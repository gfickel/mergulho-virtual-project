using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The correction budget in <see cref="MotionGateCore"/>: "decide on PATH
/// TRAVELLED, not on magnitude".
///
/// Same rule as the rest of the AR test suite: assert INVARIANTS, not today's
/// numbers. The budget's whole claim is a set of structural properties —
///
///   • standing earns nothing, so standing suppresses everything (which is the
///     behaviour the shipped threshold-0 gate already had, and must not lose);
///   • what is admitted can never exceed what travel earned;
///   • the gate stays transparent while the user is genuinely walking;
///   • an admitted correction is bled in over time, never popped in one frame;
///   • with the budget off, the gate is the pre-budget gate, exactly.
///
/// — and each of those is what these tests pin. The COMPOSITE the budget scores
/// in the offline harness is measured by `make ar-sim`, not asserted here.
///
/// The rig mirrors the real one: the camera is a CHILD of the XR Origin, so a
/// counter-shift feeds back into what the gate measures next frame. Feeding
/// MotionGateCore a position that does not include its own correction is the one
/// way to use it wrongly, so every test here goes through <see cref="Rig"/>.
/// </summary>
public class MotionGateBudgetTests
{
    const float Dt = 1f / 30f;

    /// <summary>XR Origin + camera-as-child, driven one frame at a time.</summary>
    sealed class Rig
    {
        public readonly MotionGateCore Core = new MotionGateCore();
        public Vector3 Origin;
        public Vector3 Session;
        public Vector3 Cam => Origin + Session;

        public Rig(MotionGateCore.Settings settings)
        {
            Core.Config = settings;
            Core.Reset();
        }

        /// <summary>Advance one frame after moving the tracker's session pose by
        /// <paramref name="sessionDelta"/>. Returns the shift applied to the origin.</summary>
        public Vector3 Frame(Vector3 sessionDelta, bool isStill, float dt = Dt)
        {
            Session += sessionDelta;
            Vector3 shift = Core.Step(Cam, isStill, dt);
            if (Core.SuppressedLastStep) Origin += shift;
            return shift;
        }
    }

    static MotionGateCore.Settings Budget(float ratePerMeter = 0.07f, float bleed = 0.25f) =>
        new MotionGateCore.Settings
        {
            minDelta = 0.0005f,
            relocalizationJumpThreshold = 0f,
            correctionBudgetPerMeter = ratePerMeter,
            correctionBudgetCapM = 1.5f,
            correctionBudgetHalfLifeS = 120f,
            correctionFloorM = 0.05f,
            correctionBudgetWindowS = 2f,
            correctionBudgetMinSpeedMps = 0.25f,
            accrueBudgetFromGatePath = true,
            correctionBleedSpeedMps = bleed,
        };

    /// <summary>Walk <paramref name="seconds"/> at 1.2 m/s along +Z, then stop.
    /// Returns the rig standing still with whatever budget that earned.</summary>
    static Rig WalkThenStop(MotionGateCore.Settings settings, float seconds = 10f)
    {
        var rig = new Rig(settings);
        var step = new Vector3(0f, 0f, 1.2f * Dt);
        int frames = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < frames; i++) rig.Frame(step, isStill: false);
        rig.Frame(Vector3.zero, isStill: true);   // closes the part-window
        return rig;
    }

    // =====================================================================
    // Zero budget => full suppression. The property the shipped gate has and
    // that the budget must not cost anything to keep.
    // =====================================================================

    [Test]
    public void ZeroBudget_SuppressesEveryTranslation_Entirely()
    {
        var rig = new Rig(Budget());

        // Never travelled, so nothing is owed. A 0.30 m tracker jump — squarely
        // inside the measured relocalisation distribution — must be cancelled whole.
        rig.Frame(Vector3.zero, isStill: true);           // seed
        var jump = new Vector3(0.30f, 0f, 0f);
        rig.Frame(jump, isStill: true);

        Assert.AreEqual(0f, rig.Core.CorrectionBudgetM, 1e-6f, "standing must earn no budget");
        Assert.AreEqual(0f, rig.Core.BudgetAdmittedM, 1e-6f, "nothing may be admitted on no credit");
        Assert.AreEqual(0f, rig.Cam.magnitude, 1e-5f,
            "with no budget the camera's world position must hold exactly");
        Assert.AreEqual(jump.magnitude, rig.Core.TotalSuppressed.magnitude, 1e-5f);
    }

    [Test]
    public void StandingAndSweeping_EarnNoBudget()
    {
        // The reason the accrual is NET DISPLACEMENT over a window and not summed
        // path: a pan really does move the camera (nobody rotates a phone about its
        // optical centre) and the wave oscillation moves it too, but neither is
        // travel — both come back. The IMU calls a pan "moving", so without this the
        // budget would be earned by a user who never went anywhere.
        var rig = new Rig(Budget());
        for (int i = 0; i < 600; i++)   // 20 s of sweeping, +/- 12 cm, zero net
        {
            float a = 2f * Mathf.PI * 0.25f * i * Dt;
            float aPrev = 2f * Mathf.PI * 0.25f * (i - 1) * Dt;
            var d = new Vector3(0.12f * (Mathf.Sin(a) - Mathf.Sin(aPrev)), 0f, 0f);
            rig.Frame(d, isStill: false);
        }

        Assert.AreEqual(0f, rig.Core.BudgetAccruedM, 1e-6f,
            $"a sweep with no net displacement must earn nothing, earned " +
            $"{rig.Core.BudgetAccruedM:F4} m");
    }

    [Test]
    public void SlowDrift_BelowTravelSpeed_EarnsNoBudget()
    {
        // 0.1 m/s is below correctionBudgetMinSpeedMps: shuffling in place, not
        // travelling. The bound is an order of magnitude either side of anything
        // real (a slow walk on sand is 0.75 m/s), so this is a structural test.
        var rig = new Rig(Budget());
        var creep = new Vector3(0f, 0f, 0.1f * Dt);
        for (int i = 0; i < 600; i++) rig.Frame(creep, isStill: false);

        Assert.AreEqual(0f, rig.Core.BudgetAccruedM, 1e-6f,
            "sub-walking-pace drift is not travel and must earn nothing");
    }

    // =====================================================================
    // The budget admits AT MOST what travel earned
    // =====================================================================

    [Test]
    public void Admission_NeverExceedsTheAccruedBudget()
    {
        var rig = WalkThenStop(Budget(), seconds: 10f);   // ~12 m
        float earned = rig.Core.BudgetAccruedM;
        Assert.That(earned, Is.GreaterThan(0f), "a 12 m walk must earn something");

        // A 5 m "correction" — far more than 12 m of walking can possibly owe.
        rig.Frame(new Vector3(5f, 0f, 0f), isStill: true);
        for (int i = 0; i < 2000; i++) rig.Frame(Vector3.zero, isStill: true);   // let it bleed

        Assert.That(rig.Core.BudgetAdmittedM, Is.LessThanOrEqualTo(earned + 1e-4f),
            $"admitted {rig.Core.BudgetAdmittedM:F3} m against {earned:F3} m earned — " +
            "the budget is the only thing bounding fabrication");
        Assert.That(rig.Core.CorrectionBudgetM, Is.LessThan(1e-4f), "and it must be spent");
    }

    [Test]
    public void Admission_IsPartial_NotAllOrNothing()
    {
        // With the bleed off (so the decision is visible in the same frame): a jump
        // bigger than the budget passes only the budget's worth and the remainder is
        // suppressed. All-or-nothing would either fabricate the difference or freeze
        // a correction the tracker was right to make.
        var rig = new Rig(Budget(bleed: 0f));
        rig.Frame(Vector3.zero, isStill: true);
        rig.Core.AccrueCorrectionBudget(0.30f / 0.07f);   // exactly 0.30 m of credit

        var jump = new Vector3(1f, 0f, 0f);
        rig.Frame(jump, isStill: true);

        Assert.AreEqual(0.30f, rig.Core.BudgetAdmittedM, 1e-4f);
        Assert.AreEqual(0.30f, rig.Cam.magnitude, 1e-4f,
            "the admitted part must reach the camera");
        Assert.AreEqual(0.70f, rig.Core.TotalSuppressed.magnitude, 1e-4f,
            "and the rest must be suppressed");
    }

    [Test]
    public void SmallDeltas_NeverSpendTheBudget()
    {
        // The wave oscillation slews at ~1 mm/frame; a map correction is a
        // single-frame step of 0.1-1.4 m. correctionFloorM separates those two —
        // populations one to two orders of magnitude apart — so that the budget is
        // only ever spent on something that could be a correction. Without it the
        // oscillation drains the whole budget in well under a minute.
        var rig = WalkThenStop(Budget(), seconds: 10f);
        float before = rig.Core.CorrectionBudgetM;
        Assert.That(before, Is.GreaterThan(0.1f));

        for (int i = 0; i < 900; i++)   // 30 s of 2 mm/frame oscillation
            rig.Frame(new Vector3(0.002f * (i % 2 == 0 ? 1f : -1f), 0f, 0f), isStill: true);

        Assert.AreEqual(0f, rig.Core.BudgetAdmittedM, 1e-6f,
            "oscillation must not be mistaken for a correction");
    }

    // =====================================================================
    // A clean walk preserves real motion
    // =====================================================================

    [Test]
    public void CleanWalk_PreservesRealMotion()
    {
        // The gate is inert while the IMU reports travel, and the budget must not
        // change that: every metre walked has to reach the camera.
        var rig = new Rig(Budget());
        var step = new Vector3(0f, 0f, 1.2f * Dt);
        int frames = Mathf.RoundToInt(20f / Dt);
        for (int i = 0; i < frames; i++) rig.Frame(step, isStill: false);

        float walked = frames * step.magnitude;
        Assert.AreEqual(walked, rig.Cam.magnitude, 1e-3f,
            $"the camera must have travelled the full {walked:F2} m");
        Assert.AreEqual(0f, rig.Origin.magnitude, 1e-4f,
            "nothing may have moved the origin during a clean walk");
        Assert.AreEqual(0f, rig.Core.TotalSuppressed.magnitude, 1e-5f);
    }

    [Test]
    public void WalkThenStand_TheBudgetSurvivesTheTransition()
    {
        // The sequence the whole mechanism is for: travel banks non-wave odometry
        // error while the gate is inert, the user stops, and the gate would freeze
        // that error in permanently unless something is owed.
        var rig = WalkThenStop(Budget(), seconds: 30f);   // ~36 m
        Assert.That(rig.Core.CorrectionBudgetM, Is.GreaterThan(0.5f),
            $"36 m of walking must leave real credit, had {rig.Core.CorrectionBudgetM:F3} m");
        Assert.That(rig.Core.CorrectionBudgetM,
            Is.LessThanOrEqualTo(rig.Core.Config.correctionBudgetCapM + 1e-4f),
            "and never more than the cap");
    }

    // =====================================================================
    // The bleed
    // =====================================================================

    [Test]
    public void AdmittedCorrection_IsBledIn_NeverPopped()
    {
        var settings = Budget();
        var rig = WalkThenStop(settings, seconds: 20f);
        float credit = rig.Core.CorrectionBudgetM;
        Assert.That(credit, Is.GreaterThan(0.2f));

        float maxPerFrame = settings.correctionBleedSpeedMps * Dt;

        Vector3 before = rig.Cam;
        rig.Frame(new Vector3(0.6f, 0f, 0f), isStill: true);
        // The correction is suppressed whole and queued; the same frame then
        // releases its first bleed slice, so the world moves by at most one slice —
        // never by the 0.6 m the tracker emitted.
        Assert.That((rig.Cam - before).magnitude, Is.LessThanOrEqualTo(maxPerFrame + 1e-5f),
            "the frame a correction arrives must not show the tracker's pop");
        Assert.That(rig.Core.PendingCorrectionM.magnitude, Is.GreaterThan(0f),
            "it must have been queued instead");
        Vector3 prev = rig.Cam;
        for (int i = 0; i < 2000; i++)
        {
            rig.Frame(Vector3.zero, isStill: true);
            Assert.That((rig.Cam - prev).magnitude, Is.LessThanOrEqualTo(maxPerFrame + 1e-5f),
                $"frame {i} moved the world faster than the bleed rate");
            prev = rig.Cam;
        }

        Assert.That(rig.Core.PendingCorrectionM.magnitude, Is.LessThan(1e-4f),
            "the queue must drain");
        Assert.AreEqual(rig.Core.BudgetAdmittedM, (rig.Cam - before).magnitude, 1e-3f,
            "and everything admitted must arrive, just slowly");
    }

    // =====================================================================
    // Budget off == the pre-budget gate
    // =====================================================================

    [Test]
    public void Defaults_HaveTheBudgetOff_SoEveryGoldenStaysValid()
    {
        // ArCoreCharacterizationTests pins MotionGateCore against the pre-refactor
        // reference implementation using Settings.Defaults. That reference has no
        // budget, so the day Defaults turns one on, the golden stops meaning what
        // it says rather than failing honestly.
        Assert.AreEqual(0f, MotionGateCore.Settings.Defaults.correctionBudgetPerMeter,
            "Settings.Defaults is the PRE-BUDGET baseline by contract");
    }

    [Test]
    public void BudgetOff_IgnoresDt_AndIsTheClassicGate()
    {
        // dt is a new parameter and only the budget reads it, so a pre-budget caller
        // passing 0 and one passing a real frame time must get identical results.
        var a = new Rig(MotionGateCore.Settings.Defaults);
        var b = new Rig(MotionGateCore.Settings.Defaults);

        var rng = new System.Random(20260928);
        for (int i = 0; i < 400; i++)
        {
            var d = new Vector3(
                (float)(rng.NextDouble() - 0.5) * 0.04f,
                (float)(rng.NextDouble() - 0.5) * 0.01f,
                (float)(rng.NextDouble() - 0.5) * 0.04f);
            bool still = i % 7 != 0;
            a.Frame(d, still, dt: 0f);
            b.Frame(d, still, dt: Dt);
        }

        Assert.AreEqual(a.Origin.x, b.Origin.x, 0f, "dt must be inert while the budget is off");
        Assert.AreEqual(a.Origin.y, b.Origin.y, 0f);
        Assert.AreEqual(a.Origin.z, b.Origin.z, 0f);
        Assert.AreEqual(a.Core.TotalSuppressed.magnitude, b.Core.TotalSuppressed.magnitude, 0f);
    }

    // =====================================================================
    // Decay
    // =====================================================================

    [Test]
    public void UnspentBudget_DecaysTowardZero()
    {
        // Credit that has gone unclaimed for many jump intervals (the research
        // model's rate is ~0.07 Hz, one per ~14 s) is stale, and spending it later
        // would admit a phantom correction on the strength of a walk that finished
        // minutes ago.
        var settings = Budget();
        var rig = WalkThenStop(settings, seconds: 20f);
        float start = rig.Core.CorrectionBudgetM;
        Assert.That(start, Is.GreaterThan(0.2f));

        int frames = Mathf.RoundToInt(settings.correctionBudgetHalfLifeS / Dt);
        for (int i = 0; i < frames; i++) rig.Frame(Vector3.zero, isStill: true);

        Assert.AreEqual(start * 0.5f, rig.Core.CorrectionBudgetM, start * 0.02f,
            "one half-life of standing must halve the unspent credit");
    }

    // =====================================================================
    // Coexistence with the magnitude threshold
    // =====================================================================

    [Test]
    public void ThresholdAndBudget_Coexist()
    {
        // relocalizationJumpThreshold stays present and working: above it the delta
        // is passed straight through as before, so it is untouched by the budget and
        // the two can be tuned independently at the beach.
        var settings = Budget();
        settings.relocalizationJumpThreshold = 0.35f;
        var rig = new Rig(settings);
        rig.Frame(Vector3.zero, isStill: true);

        rig.Frame(new Vector3(0.5f, 0f, 0f), isStill: true);
        Assert.AreEqual(0.5f, rig.Cam.magnitude, 1e-4f,
            "above the threshold the delta still passes through untouched");
        Assert.AreEqual(0f, rig.Core.BudgetAdmittedM, 1e-6f,
            "and it does not consume budget it never asked for");
    }
}
