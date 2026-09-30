using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MergulhoVirtual.DesignSystem.Gallery;
using MergulhoVirtual.UI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MergulhoVirtual.UiShots
{
    /// <summary>
    /// Headless UI screenshot harness: renders the UI Toolkit screens and the
    /// design-system gallery straight to PNG, with the Unity editor CLOSED, so
    /// the rendered UI can be inspected without a device build.
    ///
    /// <code>
    ///   make ds-shots                 # everything, both themes → &lt;repo&gt;/.shots/
    ///   make ds-shots SHOT=home       # only subjects whose id contains "home"
    /// </code>
    /// or, in the editor, <c>Tools &gt; Mergulho Virtual &gt; Design System &gt;
    /// Capture UI Screenshots</c>.
    ///
    /// <para><b>How the frame is made faithful.</b> Each shot gets its own
    /// throwaway <see cref="PanelSettings"/> calibrated exactly like the app's
    /// runtime panel — <see cref="PanelScaleMode.ConstantPhysicalSize"/> with
    /// <c>referenceDpi = 160</c> — so <b>1 USS px = 1 dp = 1 Figma pt</b>
    /// (DESIGN_IMPLEMENTATION.md §1.2). Supersampling for legibility is done by
    /// setting <c>fallbackDpi = 160 × scale</c> and the render texture to
    /// <c>dp × scale</c>: the panel still measures 390×844 <i>panel units</i>,
    /// it is just rasterised at 2×, matching the 2× Figma renders in
    /// <c>.figma-sync/png/</c> pixel for pixel.</para>
    ///
    /// <para><b>Safe area.</b> <see cref="Screen.safeArea"/> is meaningless in
    /// batchmode (the hidden window is 640×480), so the insets the host would
    /// compute at runtime are passed explicitly to
    /// <see cref="IAppScreen.SetEdgeInsets"/> — 47 dp top, 34 dp bottom by
    /// default, overridable. The fake status bar and home indicator the Figma
    /// frames draw are OS chrome and are deliberately NOT rendered
    /// (DESIGN_IMPLEMENTATION.md §1).</para>
    ///
    /// <para><b>Determinism.</b> Screens are constructed against
    /// <see cref="UiShotFixtures"/> — frozen clock, fixed conditions/tide, no
    /// GPS, no backend — so a PNG only changes when the UI changes.</para>
    /// </summary>
    public static class UiScreenshotHarness
    {
        // =====================================================================
        // SUBJECT TABLE — adding a screen is one line.
        //
        // Screens are resolved by *simple type name* out of the MergulhoVirtual.UI
        // assembly and constructed by matching their constructor parameters
        // against UiShotFixtures.Services (plus the fixed clock and the sprite
        // loader). That indirection is deliberate: screens land and change shape
        // while this file stays put, and a subject that cannot be built is
        // skipped with a log line instead of aborting the run.
        // =====================================================================
        static readonly Subject[] Subjects =
        {
            // --- App screens, standalone (no bottom bar) ---------------------
            new Subject("home",           ShotKind.Screen, "HomeScreen"),
            new Subject("home-tall",      ShotKind.Screen, "HomeScreen")    { HeightDp = 1400 },
            new Subject("home-dismissed", ShotKind.Screen, "HomeScreen")    { VmMethod = "DismissWelcome" },
            new Subject("praias",         ShotKind.Screen, "PraiasScreen"),

            // §8.7's states, where they are actually wired: the Início conditions
            // card with nothing fetched and the service reporting a failed attempt.
            // Offline vs. generic error is the connectivity fixture, nothing else.
            new Subject("home-conditions-error",   ShotKind.Screen, "HomeScreen")
                { VmMethod = "DismissWelcome", ConditionsUnavailable = true },
            new Subject("home-conditions-offline", ShotKind.Screen, "HomeScreen")
                { VmMethod = "DismissWelcome", ConditionsUnavailable = true, Offline = true },

            // Mergulho (Tela 8). Two states, and BOTH are worth keeping: the bare
            // one is what the HUD looks like until a tap lands on an animal (and
            // what every species looks like today, since the three spec fields are
            // blank on every AnimalDef), the card one is the frame to compare
            // against. It opens the tiger shark because Tela 8 draws the tiger
            // shark — and UiShotFixtures fills that one species' spec rows with the
            // frame's own sample strings so the table has something to lay out.
            // Nothing paints a background here: what shows through the transparent
            // parts of these PNGs is the panel's clear colour, and on a device it
            // is the live camera.
            new Subject("mergulho",       ShotKind.Screen, "MergulhoScreen"),
            new Subject("mergulho-card",  ShotKind.Screen, "MergulhoScreen")
                { VmMethod = "ShowSpecies", VmArgs = new object[] { "tiger_shark" } },
            // The Praia detalhe subjects drive BeachDetailViewModel.ShowBeach,
            // which is the first ViewModel the constructor graph builds. Two
            // beaches on purpose: Sancho is about as full as the content file
            // gets today (an advisory and two species), and Conceição is the
            // common case — not one editorial field filled, which is 14 of the
            // 17 beaches. The empty one is the shot to look at.
            new Subject("praia-detalhe",  ShotKind.Screen, "PraiaDetalheScreen") { HeightDp = 1500 },
            new Subject("praia-detalhe-empty", ShotKind.Screen, "PraiaDetalheScreen")
                { VmMethod = "ShowBeach", VmArgs = new object[] { "Praia da Conceição" } },

            // Reportar (Tela 11 / Tela 12). The empty subject IS Tela 11 — an
            // untouched form over the pending feed, which the "empty state" frame
            // also draws. The filled one is the review shot: it drives the
            // ViewModel through exactly what a user would tap, in order, so what
            // lands in the PNG is the real bound state and not a posed tree.
            new Subject("report",         ShotKind.Screen, "ReportScreen") { HeightDp = 1650 },
            new Subject("report-filled",  ShotKind.Screen, "ReportScreen")
            {
                HeightDp = 1850,
                VmCalls = new[]
                {
                    new VmCall("SelectSpecies", 0),
                    new VmCall("SelectSize", 1),
                    new VmCall("ToggleBehaviour", 0),
                    new VmCall("ToggleBehaviour", 1),
                    new VmCall("ToggleBehaviour", 2),
                    // The fixture picker answers synchronously with a committed
                    // JPEG, so this lands a real thumbnail in the grid.
                    new VmCall("PickPhoto"),
                    new VmCall("SelectProfile", 0),
                },
            },

            // Espécie (Decision D1). There is NO FIGMA FRAME for this screen, so these
            // shots are not a comparison — they ARE the design review, and there are
            // three because the page has three genuinely different shapes.
            //   especie          lemon_shark: the fullest page in the catalog — the only
            //                    species with videos, so the only one that shows that
            //                    section at all.
            //   especie-no-video hammerhead: what the other FOUR species look like, and
            //                    the longest description, so it is the wrap test.
            //   especie-playing  the video card doing something. Reachable only through
            //                    the ViewModel (ToggleVideo), which is the reason that
            //                    state lives there rather than in the screen.
            // The 3D viewport and the video frame are stand-in gradients from
            // UiShotFixtures — no rig and no H.264 decoder exist in batchmode. They show
            // the framing, not the animal.
            new Subject("especie", ShotKind.Screen, "EspecieScreen")
            {
                HeightDp = 1900,
                VmMethod = "ShowSpecies",
                VmArgs = new object[] { "lemon_shark" },
            },
            new Subject("especie-no-video", ShotKind.Screen, "EspecieScreen")
            {
                HeightDp = 1300,
                VmMethod = "ShowSpecies",
                VmArgs = new object[] { "hammerhead" },
            },
            new Subject("especie-playing", ShotKind.Screen, "EspecieScreen")
            {
                HeightDp = 1900,
                VmCalls = new[]
                {
                    new VmCall("ShowSpecies", "lemon_shark"),
                    new VmCall("ToggleVideo", 0),
                },
            },

            // Conteúdo educativo (the educational-article feature). There is NO FIGMA
            // FRAME for either screen, so these shots are not a comparison — they ARE
            // the design review. Four subjects, because the content has four genuinely
            // different shapes and no single article exercises them all:
            //   conteudos          the index: four categories, one article each, three
            //                      with a cover and one without.
            //   conteudo           tubarões: the ONLY article with a video block and
            //                      with speciesRef — five of them in a row, which is the
            //                      run the tight-gutter rule and the per-species name
            //                      lookup exist for. Also an info callout, an attributed
            //                      quote, a captioned+credited figure and L2/L3 headings.
            //   conteudo-sem-capa  como-registrar: the article with NO hero, i.e. the
            //                      back-button-row path instead of the 240dp hero. Also
            //                      carries the HTML-escaped-entity paragraph the credit
            //                      convention section spells out, which is the one thing
            //                      here only a render can answer.
            //   conteudo-praticas  mergulho-responsável: the remaining block types —
            //                      numberedList, a warning AND a success callout, two
            //                      beachRefs, and the one figure in the whole corpus
            //                      with NEITHER a caption NOR a credit (so the gating
            //                      that drops both lines is visible).
            // The video card's poster is a stand-in gradient from UiShotFixtures — no
            // H.264 decoder exists in batchmode — so it shows the framing, not the clip.
            new Subject("conteudos", ShotKind.Screen, "ArticlesScreen") { HeightDp = 1900 },
            new Subject("conteudo", ShotKind.Screen, "ArticleScreen")
            {
                HeightDp = 2600,
                VmMethod = "Show",
                VmArgs = new object[] { "tubaroes-de-noronha" },
            },
            new Subject("conteudo-sem-capa", ShotKind.Screen, "ArticleScreen")
            {
                HeightDp = 2400,
                VmMethod = "Show",
                VmArgs = new object[] { "como-registrar-um-avistamento" },
            },
            new Subject("conteudo-praticas", ShotKind.Screen, "ArticleScreen")
            {
                HeightDp = 2500,
                VmMethod = "Show",
                VmArgs = new object[] { "mergulho-responsavel" },
            },

            // --- The app shell: router + MdNavigationBar + a screen ----------
            // This is the one that is directly comparable to a whole V2 frame,
            // because it includes the bottom bar the standalone shots omit.
            new Subject("shell-home",    ShotKind.Shell, "HomeScreen")    { Route = AppRoutes.Home },
            // The one directly comparable to Tela 8 — the card AND the bottom bar.
            new Subject("shell-mergulho", ShotKind.Shell, "MergulhoScreen")
            {
                Route = AppRoutes.Mergulho,
                VmMethod = "ShowSpecies",
                VmArgs = new object[] { "tiger_shark" },
            },
            new Subject("shell-praias",  ShotKind.Shell, "PraiasScreen") { Route = AppRoutes.Praias },
            new Subject("shell-praia-detalhe", ShotKind.Shell, "PraiaDetalheScreen") { Route = AppRoutes.PraiaDetalhe },
            new Subject("shell-report",  ShotKind.Shell, "ReportScreen") { Route = AppRoutes.Avistamentos },
            // Espécie with the bar, which is what a pushed sub-screen really looks like
            // (the bar stays up). Navigate rather than Push, so no tab reads as selected
            // — the same small inaccuracy shell-praia-detalhe has.
            new Subject("shell-especie", ShotKind.Shell, "EspecieScreen")
            {
                Route = AppRoutes.Especie,
                VmMethod = "ShowSpecies",
                VmArgs = new object[] { "lemon_shark" },
            },
            // The article index with the bar up — what a pushed sub-screen really looks
            // like. Navigate rather than Push, so no tab reads as selected: the same
            // small inaccuracy shell-praia-detalhe and shell-especie carry.
            new Subject("shell-conteudos", ShotKind.Shell, "ArticlesScreen") { Route = AppRoutes.Conteudos },

            // --- Design-system gallery --------------------------------------
            // "gallery" is the device-frame view; "gallery-sections" additionally
            // emits one naturally-sized PNG per Build*Section (gallery-buttons-light.png, …).
            new Subject("gallery",          ShotKind.Gallery),
            new Subject("gallery-sections", ShotKind.GallerySections),
        };

        // --- Frame defaults (all overridable; see ReadOptions) ----------------
        const int DefaultWidthDp = 390;   // every V2 Protótipo frame is 390pt wide
        const int DefaultHeightDp = 844;  // iPhone-class portrait content height
        const int DefaultScale = 2;       // 2× supersample, like the Figma renders
        const float DefaultTopInsetDp = 47f;    // status bar / notch
        const float DefaultBottomInsetDp = 34f; // home indicator
        const int MaxTextureSideDp = 4000;      // guard rail: 4000 dp × 2 = 8000 px

        /// <summary>What the panel clears to where no screen paints — on a device
        /// that strip is the AR camera or the OS; black makes the gap obvious.</summary>
        static readonly Color ClearColor = new Color(0f, 0f, 0f, 1f);

        const string ThemeLightPath = "Assets/DesignSystem/Theme-Light.tss";
        const string ThemeDarkPath = "Assets/DesignSystem/Theme-Dark.tss";
        const string GalleryStylesPath = "Assets/DesignSystem/Gallery/Gallery.uss";
        const string UiScreensDir = "Assets/UI/Screens";

        // =====================================================================
        // Entry points
        // =====================================================================

        [MenuItem("Tools/Mergulho Virtual/Design System/Capture UI Screenshots", priority = 210)]
        public static void CaptureAll()
        {
            var options = ReadOptions();
            Directory.CreateDirectory(options.OutputDir);

            var written = new List<ShotRecord>();
            int skipped = 0;

            foreach (var subject in Subjects.Concat(DiscoverUndeclaredScreens()))
            {
                foreach (var theme in options.Themes)
                {
                    // GallerySections fans out into ids the table never spells
                    // ("gallery-buttons", …), so it opts out of the outer filter
                    // and filters per section instead.
                    bool matches = subject.Kind == ShotKind.GallerySections
                        ? string.IsNullOrEmpty(options.Filter) ||
                          options.Filter.StartsWith("gallery", StringComparison.OrdinalIgnoreCase)
                        : options.Matches(subject.Id, theme);
                    if (!matches) continue;
                    try
                    {
                        // Per-subject fixture switches (the registry holds one fake
                        // per interface, so the failure states are a flag, not a
                        // second instance). Cleared in the finally below.
                        UiShotFixtures.ConditionsUnavailable = subject.ConditionsUnavailable;
                        UiShotFixtures.Offline = subject.Offline;

                        switch (subject.Kind)
                        {
                            case ShotKind.Screen:
                                written.Add(CaptureScreen(subject, theme, options, shell: false));
                                break;
                            case ShotKind.Shell:
                                written.Add(CaptureScreen(subject, theme, options, shell: true));
                                break;
                            case ShotKind.Gallery:
                                written.Add(CaptureGalleryFrame(subject, theme, options));
                                break;
                            case ShotKind.GallerySections:
                                written.AddRange(CaptureGallerySections(subject, theme, options));
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        skipped++;
                        Debug.LogWarning($"[ui-shots] SKIPPED {subject.Id}-{Name(theme)}: {Unwrap(e).Message}");
                    }
                    finally
                    {
                        UiShotFixtures.ConditionsUnavailable = false;
                        UiShotFixtures.Offline = false;
                    }
                }
            }

            WriteManifest(options, written);

            var summary = new StringBuilder();
            summary.Append($"[ui-shots] {written.Count} PNG(s) → {options.OutputDir}");
            if (skipped > 0) summary.Append($" ({skipped} subject(s) skipped — see warnings above)");
            Debug.Log(summary.ToString());
            // Plain stdout too: `make` greps this out of the Unity log.
            Console.WriteLine($"UI-SHOTS-SUMMARY written={written.Count} skipped={skipped} dir={options.OutputDir}");

            if (written.Count == 0)
                throw new Exception("[ui-shots] nothing was captured — every subject failed or the filter matched none.");
        }

        /// <summary>
        /// Any <see cref="IAppScreen"/> in the UI assembly the table does not name
        /// gets a bare + shell subject for free, so a screen that lands after this
        /// file was written still shows up in the run. Declare it in the table when
        /// it needs variants (a taller frame, a pre-navigated state, …).
        /// </summary>
        static IEnumerable<Subject> DiscoverUndeclaredScreens()
        {
            Type[] types;
            try { types = AppScreenTypes().OrderBy(t => t.Name, StringComparer.Ordinal).ToArray(); }
            catch (Exception e)
            {
                Debug.LogWarning($"[ui-shots] screen discovery failed: {Unwrap(e).Message}");
                yield break;
            }

            var declared = new HashSet<string>(Subjects.Select(s => s.ScreenTypeName).Where(n => n != null));
            foreach (var type in types)
            {
                if (declared.Contains(type.Name)) continue;
                string id = Slug(type.Name.EndsWith("Screen", StringComparison.Ordinal)
                    ? type.Name.Substring(0, type.Name.Length - "Screen".Length)
                    : type.Name);
                if (string.IsNullOrEmpty(id)) continue;
                Debug.Log($"[ui-shots] auto-discovered {type.Name} → subjects '{id}' and 'shell-{id}'");
                yield return new Subject(id, ShotKind.Screen, type.Name);
                yield return new Subject("shell-" + id, ShotKind.Shell, type.Name);
            }
        }

        // =====================================================================
        // Screens
        // =====================================================================

        static ShotRecord CaptureScreen(Subject subject, ShotTheme theme, Options options, bool shell)
        {
            var screenType = ResolveScreenType(subject.ScreenTypeName);
            int heightDp = subject.HeightDp > 0 ? subject.HeightDp : options.HeightDp;

            using (var panel = ShotPanel.Create(options.WidthDp, heightDp, options.Scale, theme, options))
            {
                AddScreenStyleSheet(panel.Root, screenType.Name);

                var built = BuildScreen(screenType);
                var screen = built.Instance as IAppScreen
                    ?? throw new Exception($"{screenType.Name} does not implement IAppScreen");

                if (shell)
                {
                    // Also register the *other* known screens so the bottom bar's
                    // destinations resolve; a tab with no screen refuses to activate.
                    var router = new MdRouter();
                    router.SetTabs(AppTabs.Default);
                    router.Register(screen);
                    foreach (var extra in OtherScreens(screenType))
                        router.Register(extra);

                    panel.Root.Add(router);
                    router.SetEdgeInsets(options.TopInsetDp, 0f, 0f, options.BottomInsetDp);
                    if (!router.Navigate(subject.Route ?? screen.Key))
                        throw new Exception($"router refused to navigate to '{subject.Route ?? screen.Key}'");
                }
                else
                {
                    panel.Root.Add(screen.Root);
                    screen.OnEnter();
                    screen.SetEdgeInsets(options.TopInsetDp, 0f, 0f, options.BottomInsetDp);
                }

                // Post-build navigation / form filling (open a beach detail, tap
                // through the report form). Applied after OnEnter, which is what
                // resets the screen to its landing state.
                InvokeVmCalls(built.ViewModel, subject);

                return panel.Render(options, PngPath(options, subject.Id, theme), subject.Id, theme);
            }
        }

        /// <summary>Every other buildable IAppScreen, so the shell's tabs resolve. Failures are ignored — this is best-effort garnish.</summary>
        static IEnumerable<IAppScreen> OtherScreens(Type except)
        {
            foreach (var type in AppScreenTypes())
            {
                if (type == except) continue;
                IAppScreen screen = null;
                try { screen = BuildScreen(type).Instance as IAppScreen; }
                catch (Exception e) { Debug.Log($"[ui-shots] shell: {type.Name} not registered ({Unwrap(e).Message})"); }
                if (screen != null) yield return screen;
            }
        }


        // =====================================================================
        // Gallery
        // =====================================================================

        static ShotRecord CaptureGalleryFrame(Subject subject, ShotTheme theme, Options options)
        {
            using (var panel = ShotPanel.Create(options.WidthDp, options.HeightDp, options.Scale, theme, options))
            {
                BuildGallery(panel, theme);
                return panel.Render(options, PngPath(options, subject.Id, theme), subject.Id, theme);
            }
        }

        static IEnumerable<ShotRecord> CaptureGallerySections(Subject subject, ShotTheme theme, Options options)
        {
            var records = new List<ShotRecord>();

            // One tall provisional panel: build the gallery, then lift each
            // section out into a bare wrapper, measure it, resize the panel's
            // target texture to the measured height and render. Resizing the
            // target texture (rather than rebuilding) keeps the whole pass to a
            // single gallery build.
            using (var panel = ShotPanel.Create(options.WidthDp, MaxTextureSideDp, options.Scale, theme, options))
            {
                var gallery = BuildGallery(panel, theme);
                var sections = gallery.Query<VisualElement>(className: "gallery-section").ToList();
                if (sections.Count == 0) throw new Exception("no .gallery-section elements found");

                foreach (var section in sections)
                {
                    string id = subject.Id.Replace("-sections", "") + "-" + Slug(SectionTitle(section));
                    if (!options.Matches(id, theme)) continue;
                    try
                    {
                        // A wrapper reproducing the gallery's page background +
                        // scroll padding, so the section lays out (and reads) the
                        // same as it does inside the gallery.
                        var wrapper = new VisualElement();
                        wrapper.AddToClassList("gallery-root");
                        var pad = new VisualElement();
                        pad.AddToClassList("gallery-scroll");
                        // .gallery-root/.gallery-scroll are flex-grow:1 (they fill
                        // the gallery frame); pinned to 0 here so resolvedStyle.height
                        // reports the section's NATURAL height, which is what the
                        // texture is then resized to. flex-shrink must go to 0 as
                        // well — Yoga's default of 1 would squash the wrapper down
                        // to whatever height the panel happens to be at (i.e. the
                        // *previous* section's), which is exactly how this read the
                        // same 233 dp for half the catalog before.
                        wrapper.style.flexGrow = 0f;
                        wrapper.style.flexShrink = 0f;
                        pad.style.flexGrow = 0f;
                        pad.style.flexShrink = 0f;
                        section.style.flexShrink = 0f;
                        wrapper.Add(pad);
                        pad.Add(section); // reparents out of the gallery's ScrollView

                        panel.Root.Clear();
                        panel.Root.Add(wrapper);
                        panel.Pump();

                        int measured = Mathf.Clamp(Mathf.CeilToInt(wrapper.resolvedStyle.height), 24, MaxTextureSideDp);
                        panel.Resize(options.WidthDp, measured, options.Scale);

                        records.Add(panel.Render(options, PngPath(options, id, theme), id, theme));
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[ui-shots] SKIPPED {id}-{Name(theme)}: {Unwrap(e).Message}");
                    }
                }
            }
            return records;
        }

        /// <summary>Builds the real <see cref="GalleryController"/> tree — one source of truth for the catalog.</summary>
        static VisualElement BuildGallery(ShotPanel panel, ShotTheme theme)
        {
            var styles = Load<StyleSheet>(GalleryStylesPath);
            var controller = panel.Host.AddComponent<GalleryController>();
            var so = new SerializedObject(controller);
            so.FindProperty("document").objectReferenceValue = panel.Document;
            so.FindProperty("lightTheme").objectReferenceValue = Load<ThemeStyleSheet>(ThemeLightPath);
            so.FindProperty("darkTheme").objectReferenceValue = Load<ThemeStyleSheet>(ThemeDarkPath);
            so.FindProperty("galleryStyles").objectReferenceValue = styles;
            so.ApplyModifiedPropertiesWithoutUndo();

            // GalleryController is not [ExecuteAlways], so edit mode never fires
            // its OnEnable — the tree is built by calling it directly.
            var onEnable = typeof(GalleryController).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
            if (onEnable == null) throw new Exception("GalleryController.OnEnable not found");
            onEnable.Invoke(controller, null);

            panel.Pump();
            return panel.Root;
        }

        static string SectionTitle(VisualElement section)
        {
            var label = section.Q<Label>(className: "gallery-section__title");
            return label != null ? label.text : section.name;
        }

        // =====================================================================
        // Screen construction (reflection — tolerant of screens that do not
        // exist yet, and of constructor shapes this file has never seen)
        // =====================================================================

        readonly struct BuiltScreen
        {
            public readonly object Instance;
            /// <summary>The first ViewModel-shaped constructor argument, for post-build navigation.</summary>
            public readonly object ViewModel;
            public BuiltScreen(object instance, object viewModel) { Instance = instance; ViewModel = viewModel; }
        }

        static BuiltScreen BuildScreen(Type screenType)
        {
            object vm = null;
            var instance = Construct(screenType, depth: 0, firstViewModel: ref vm);
            return new BuiltScreen(instance, vm);
        }

        static object Construct(Type type, int depth, ref object firstViewModel)
        {
            if (depth > 4) throw new Exception($"constructor graph too deep at {type.Name}");

            var ctor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault()
                ?? throw new Exception($"{type.Name} has no public constructor");

            var args = new object[ctor.GetParameters().Length];
            var parameters = ctor.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
                args[i] = ResolveArgument(parameters[i], depth, ref firstViewModel);

            var instance = ctor.Invoke(args);
            if (firstViewModel == null && depth == 0)
                firstViewModel = args.FirstOrDefault(a => a != null && a.GetType().Name.EndsWith("ViewModel", StringComparison.Ordinal));
            return instance;
        }

        static object ResolveArgument(ParameterInfo parameter, int depth, ref object firstViewModel)
        {
            var type = parameter.ParameterType;

            if (UiShotFixtures.Services.TryGetValue(type, out var service)) return service;

            // Frozen clock + fixed-offset local time: this is what makes the PNGs
            // byte-stable across machines and days.
            if (type == typeof(Func<DateTime>)) return (Func<DateTime>)(() => UiShotFixtures.FixedNowUtc);
            if (type == typeof(Func<DateTime, DateTime>)) return (Func<DateTime, DateTime>)UiShotFixtures.ToNoronhaLocal;
            // THREE sprite loaders now (beach covers, species photos and article
            // images), told apart by parameter name — they are the same delegate type,
            // so the name is the only signal, and the beach loader stays the default.
            // The article one is NOT interchangeable with the others: an article
            // authors a complete Resources path, so prefixing it with a folder makes
            // every figure resolve to null and disappear from the PNG with no warning.
            if (type == typeof(Func<string, Sprite>))
            {
                string name = parameter.Name ?? "";
                if (name.IndexOf("article", StringComparison.OrdinalIgnoreCase) >= 0)
                    return (Func<string, Sprite>)UiShotFixtures.LoadArticleSprite;
                return name.IndexOf("species", StringComparison.OrdinalIgnoreCase) >= 0
                    ? (Func<string, Sprite>)UiShotFixtures.LoadSpeciesSprite
                    : (Func<string, Sprite>)UiShotFixtures.LoadBeachSprite;
            }
            if (type == typeof(Action)) return (Action)(() => { });
            if (type == typeof(Action<string>)) return (Action<string>)(_ => { });

            // A ViewModel (or any other plain class from the UI layer): build it
            // the same way, recursively.
            if (type.IsClass && !type.IsAbstract && type.Assembly == UiAssembly && type.GetConstructors().Length > 0)
            {
                var built = Construct(type, depth + 1, ref firstViewModel);
                if (firstViewModel == null && type.Name.EndsWith("ViewModel", StringComparison.Ordinal))
                    firstViewModel = built;
                return built;
            }

            if (parameter.HasDefaultValue) return parameter.DefaultValue;

            throw new Exception(
                $"cannot resolve constructor parameter '{parameter.Name}' of type {type.Name} — " +
                "register a fake for it in UiShotFixtures.Services");
        }

        /// <summary>
        /// Drives the ViewModel into the state a subject wants, in declaration
        /// order: the single <see cref="Subject.VmMethod"/> first (one call is the
        /// common case — "open this beach"), then <see cref="Subject.VmCalls"/> for
        /// the states no single call can reach, like a filled-in form.
        /// </summary>
        static void InvokeVmCalls(object viewModel, Subject subject)
        {
            if (!string.IsNullOrEmpty(subject.VmMethod))
                Invoke(viewModel, new VmCall(subject.VmMethod, subject.VmArgs ?? Array.Empty<object>()));
            if (subject.VmCalls == null) return;
            foreach (var call in subject.VmCalls)
                Invoke(viewModel, call);
        }

        static void Invoke(object viewModel, VmCall call)
        {
            if (viewModel == null) throw new Exception($"'{call.Method}' requested but no ViewModel was constructed");

            var method = viewModel.GetType().GetMethod(
                call.Method, BindingFlags.Instance | BindingFlags.Public, null,
                call.Args.Select(a => a?.GetType() ?? typeof(object)).ToArray(), null)
                ?? throw new Exception($"{viewModel.GetType().Name}.{call.Method} not found");
            method.Invoke(viewModel, call.Args);
        }

        static Assembly UiAssembly => typeof(IAppScreen).Assembly;

        static Type ResolveScreenType(string simpleName)
        {
            var type = AppScreenTypes().FirstOrDefault(t => t.Name == simpleName)
                ?? throw new Exception($"screen type '{simpleName}' not found in {UiAssembly.GetName().Name} (not written yet?)");
            return type;
        }

        static IEnumerable<Type> AppScreenTypes() =>
            UiAssembly.GetTypes().Where(t =>
                !t.IsAbstract && typeof(IAppScreen).IsAssignableFrom(t) && typeof(VisualElement).IsAssignableFrom(t));

        /// <summary>Each screen ships its own USS next to the .cs; the host loads them onto the panel root.</summary>
        static void AddScreenStyleSheet(VisualElement root, string screenTypeName)
        {
            foreach (var path in new[] { $"{UiScreensDir}/{screenTypeName}.uss" })
            {
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            }
            // The shell shows more than one screen's markup, so load them all.
            foreach (var guid in AssetDatabase.FindAssets("t:StyleSheet", new[] { UiScreensDir }))
            {
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
                if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            }
        }

        // =====================================================================
        // The render pipeline
        //
        // UI Toolkit has no public "render this panel to a texture" call, and in
        // batchmode nothing drives the runtime panel loop. The three internal
        // UIElementsRuntimeUtility entry points below are what the player loop
        // itself calls; they were picked empirically (RenderOffscreenPanels()
        // alone leaves the texture untouched in edit mode).
        // =====================================================================
        sealed class ShotPanel : IDisposable
        {
            public GameObject Host;
            public UIDocument Document;
            public PanelSettings Settings;
            public RenderTexture Texture;
            public VisualElement Root => Document.rootVisualElement;
            public int WidthDp, HeightDp, Scale;

            public static ShotPanel Create(int widthDp, int heightDp, int scale, ShotTheme theme, Options options)
            {
                var panel = new ShotPanel { WidthDp = widthDp, HeightDp = heightDp, Scale = scale };
                panel.Settings = ScriptableObject.CreateInstance<PanelSettings>();
                panel.Settings.name = "UiShotPanelSettings";
                panel.Settings.themeStyleSheet = Load<ThemeStyleSheet>(theme == ShotTheme.Dark ? ThemeDarkPath : ThemeLightPath);
                // 1 USS px = 1 dp, exactly like Assets/UI/AppPanelSettings.asset.
                // The scale factor is smuggled in through the DPI ratio so the
                // panel still *measures* in dp while rasterising at `scale`×.
                panel.Settings.scaleMode = PanelScaleMode.ConstantPhysicalSize;
                panel.Settings.referenceDpi = 160f;
                panel.Settings.fallbackDpi = 160f * scale;
                panel.Settings.clearColor = true;
                panel.Settings.colorClearValue = options.ClearColor;

                panel.Texture = NewTexture(widthDp, heightDp, scale);
                panel.Settings.targetTexture = panel.Texture;

                panel.Host = new GameObject("UiShotHost") { hideFlags = HideFlags.DontSave };
                panel.Document = panel.Host.AddComponent<UIDocument>();
                panel.Document.panelSettings = panel.Settings; // must precede rootVisualElement
                if (panel.Document.rootVisualElement == null)
                {
                    panel.Dispose();
                    throw new Exception("UIDocument produced no rootVisualElement");
                }
                panel.Root.style.flexGrow = 1f;
                return panel;
            }

            static RenderTexture NewTexture(int widthDp, int heightDp, int scale)
            {
                var rt = new RenderTexture(widthDp * scale, heightDp * scale, 24,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "UiShotTarget",
                    antiAliasing = 1,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                rt.Create();
                return rt;
            }

            public void Resize(int widthDp, int heightDp, int scale)
            {
                var old = Texture;
                WidthDp = widthDp; HeightDp = heightDp; Scale = scale;
                Texture = NewTexture(widthDp, heightDp, scale);
                Settings.targetTexture = Texture;
                if (old != null) { old.Release(); Object.DestroyImmediate(old); }
                Pump();
            }

            /// <summary>Runs layout/style/binding updates on every runtime panel.</summary>
            public void Pump()
            {
                UpdatePanels();
                UpdatePanels(); // a second pass settles GeometryChangedEvent reactions (safe-area padding, sparkline geometry)
            }

            public ShotRecord Render(Options options, string path, string id, ShotTheme theme)
            {
                Pump();
                var runtimePanel = Root.panel;
                RepaintPanel(runtimePanel);
                RenderPanel(runtimePanel);

                var previous = RenderTexture.active;
                RenderTexture.active = Texture;
                var readback = new Texture2D(Texture.width, Texture.height, TextureFormat.RGBA32, false, false);
                readback.ReadPixels(new Rect(0, 0, Texture.width, Texture.height), 0, 0);
                readback.Apply();
                RenderTexture.active = previous;

                var png = readback.EncodeToPNG();
                Object.DestroyImmediate(readback);
                File.WriteAllBytes(path, png);

                var record = new ShotRecord
                {
                    Id = id,
                    Theme = Name(theme),
                    File = Path.GetFileName(path),
                    WidthPx = Texture.width,
                    HeightPx = Texture.height,
                    WidthDp = WidthDp,
                    HeightDp = HeightDp,
                    Scale = Scale,
                    Bytes = png.Length,
                };
                Debug.Log($"[ui-shots] {record.File}  {record.WidthPx}×{record.HeightPx}px " +
                          $"({record.WidthDp}×{record.HeightDp}dp @{record.Scale}×)  {record.Bytes / 1024} KB");
                return record;
            }

            public void Dispose()
            {
                if (Host != null) Object.DestroyImmediate(Host);
                if (Texture != null) { Texture.Release(); Object.DestroyImmediate(Texture); }
                if (Settings != null) Object.DestroyImmediate(Settings);
                Host = null; Texture = null; Settings = null;
            }
        }

        // --- The three internal calls that make offscreen rendering happen ----
        const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Type RuntimeUtility =
            typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");

        static MethodInfo Required(string name, params Type[] signature)
        {
            var method = signature.Length == 0
                ? RuntimeUtility?.GetMethod(name, StaticAny, null, Type.EmptyTypes, null)
                : RuntimeUtility?.GetMethod(name, StaticAny, null, signature, null);
            return method ?? throw new Exception(
                $"UIElementsRuntimeUtility.{name} not found — the offscreen render path needs porting to this Unity version.");
        }

        static void UpdatePanels() => Required("UpdatePanels").Invoke(null, null);

        static void RepaintPanel(IPanel panel)
        {
            var method = RuntimeUtility.GetMethods(StaticAny).First(m => m.Name == "RepaintPanel");
            method.Invoke(null, new object[] { panel });
        }

        static void RenderPanel(IPanel panel)
        {
            var method = RuntimeUtility.GetMethods(StaticAny).First(m => m.Name == "RenderPanel");
            method.Invoke(null, new object[] { panel, true });
        }

        // =====================================================================
        // Options / plumbing
        // =====================================================================

        sealed class Options
        {
            public string OutputDir;
            public int WidthDp = DefaultWidthDp;
            public int HeightDp = DefaultHeightDp;
            public int Scale = DefaultScale;
            public float TopInsetDp = DefaultTopInsetDp;
            public float BottomInsetDp = DefaultBottomInsetDp;
            public Color ClearColor = UiScreenshotHarness.ClearColor;
            public string Filter;
            public ShotTheme[] Themes = { ShotTheme.Light, ShotTheme.Dark };

            public bool Matches(string id, ShotTheme theme) =>
                string.IsNullOrEmpty(Filter)
                || id.IndexOf(Filter, StringComparison.OrdinalIgnoreCase) >= 0
                || $"{id}-{Name(theme)}".IndexOf(Filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static Options ReadOptions()
        {
            var options = new Options
            {
                OutputDir = Env("MV_SHOT_DIR") ?? RepoPath(".shots"),
                Filter = Env("MV_SHOT_FILTER"),
            };
            options.WidthDp = EnvInt("MV_SHOT_WIDTH", options.WidthDp);
            options.HeightDp = EnvInt("MV_SHOT_HEIGHT", options.HeightDp);
            options.Scale = Mathf.Clamp(EnvInt("MV_SHOT_SCALE", options.Scale), 1, 4);
            options.TopInsetDp = EnvFloat("MV_SHOT_TOP_INSET", options.TopInsetDp);
            options.BottomInsetDp = EnvFloat("MV_SHOT_BOTTOM_INSET", options.BottomInsetDp);

            string themes = Env("MV_SHOT_THEMES");
            if (!string.IsNullOrEmpty(themes))
            {
                options.Themes = themes.Split(',')
                    .Select(t => t.Trim().Equals("dark", StringComparison.OrdinalIgnoreCase) ? ShotTheme.Dark : ShotTheme.Light)
                    .Distinct().ToArray();
            }
            return options;
        }

        static string Env(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        static int EnvInt(string key, int fallback) =>
            int.TryParse(Env(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        static float EnvFloat(string key, float fallback) =>
            float.TryParse(Env(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        /// <summary>&lt;repo root&gt;/<paramref name="relative"/> — Application.dataPath is &lt;repo&gt;/src/app/MergulhoVirtual/Assets.</summary>
        static string RepoPath(string relative) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../../..", relative));

        static string PngPath(Options options, string id, ShotTheme theme) =>
            Path.Combine(options.OutputDir, $"{id}-{Name(theme)}.png");

        static T Load<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException($"[ui-shots] missing asset: {path}");

        static string Name(ShotTheme theme) => theme == ShotTheme.Dark ? "dark" : "light";

        static Exception Unwrap(Exception e) => e is TargetInvocationException t && t.InnerException != null ? Unwrap(t.InnerException) : e;

        static string Slug(string text)
        {
            var sb = new StringBuilder();
            foreach (var ch in (text ?? "").Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }

        static void WriteManifest(Options options, List<ShotRecord> records)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generated_utc\": \"{DateTime.UtcNow:O}\",");
            sb.AppendLine($"  \"fixed_clock_utc\": \"{UiShotFixtures.FixedNowUtc:O}\",");
            sb.AppendLine($"  \"frame_dp\": [{options.WidthDp}, {options.HeightDp}],");
            sb.AppendLine($"  \"scale\": {options.Scale},");
            sb.AppendLine($"  \"safe_area_dp\": {{ \"top\": {options.TopInsetDp.ToString(CultureInfo.InvariantCulture)}, \"bottom\": {options.BottomInsetDp.ToString(CultureInfo.InvariantCulture)} }},");
            sb.AppendLine("  \"note\": \"1 USS px = 1 dp = 1 Figma pt; PNGs are rasterised at scale× so they line up with .figma-sync/png/ 2x renders. The status bar / home indicator are OS chrome and are not drawn.\",");
            sb.AppendLine("  \"shots\": [");
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                sb.Append($"    {{ \"id\": \"{r.Id}\", \"theme\": \"{r.Theme}\", \"file\": \"{r.File}\", " +
                          $"\"px\": [{r.WidthPx}, {r.HeightPx}], \"dp\": [{r.WidthDp}, {r.HeightDp}], " +
                          $"\"scale\": {r.Scale}, \"bytes\": {r.Bytes} }}");
                sb.AppendLine(i == records.Count - 1 ? "" : ",");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(options.OutputDir, "manifest.json"), sb.ToString());
        }

        // =====================================================================
        // Types
        // =====================================================================

        enum ShotKind { Screen, Shell, Gallery, GallerySections }

        enum ShotTheme { Light, Dark }

        sealed class Subject
        {
            public readonly string Id;
            public readonly ShotKind Kind;
            public readonly string ScreenTypeName;

            /// <summary>Frame height in dp; 0 = the run's default (844).</summary>
            public int HeightDp;

            /// <summary>Shell subjects only — the route to navigate to.</summary>
            public string Route;

            /// <summary>Optional ViewModel method invoked after the screen is built (e.g. "ShowDetail").</summary>
            public string VmMethod;
            public object[] VmArgs;

            /// <summary>
            /// Several ViewModel calls, applied in order after
            /// <see cref="VmMethod"/>. A form-shaped screen has no single "show
            /// this" entry point — its interesting state is six taps deep — and
            /// replaying the taps is what keeps the shot honest.
            /// </summary>
            public VmCall[] VmCalls;

            /// <summary>
        /// Flip the conditions fixture to "nothing loaded, the fetch failed" for
        /// this subject only — the §8.7 error state, wired into the Início card.
        /// </summary>
        public bool ConditionsUnavailable;

        /// <summary>Report no network link, which turns the failure above into the
        /// offline variant.</summary>
        public bool Offline;

        public Subject(string id, ShotKind kind, string screenTypeName = null)
            {
                Id = id;
                Kind = kind;
                ScreenTypeName = screenTypeName;
            }
        }

        /// <summary>One ViewModel method call, as a subject declares it.</summary>
        sealed class VmCall
        {
            public readonly string Method;
            public readonly object[] Args;

            public VmCall(string method, params object[] args)
            {
                Method = method;
                Args = args ?? Array.Empty<object>();
            }
        }

        sealed class ShotRecord
        {
            public string Id, Theme, File;
            public int WidthPx, HeightPx, WidthDp, HeightDp, Scale, Bytes;
        }
    }
}
