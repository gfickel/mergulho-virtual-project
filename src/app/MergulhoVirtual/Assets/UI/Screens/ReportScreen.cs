using System;
using System.Collections.Generic;
using System.IO;
using MergulhoVirtual.DesignSystem;
using MergulhoVirtual.UI.Navigation;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Reportar avistamento — DESIGN_IMPLEMENTATION.md §8.5, Figma frames Tela 11
    /// (109:933, empty form) and Tela 12 (109:1080, filled). The Avistamentos tab
    /// root, replacing the legacy uGUI RegisterScreen.
    ///
    /// <para>Built in code like every other Phase 3 screen: the content APIs it
    /// composes (<see cref="MvMediaPicker.SetItems"/>, MdChip selection,
    /// <see cref="MvOptionCard"/> grouping) are code-only, so a UXML file would
    /// hold nothing but empty boxes. Router-agnostic for the same reason as the
    /// others — it raises <see cref="AppRoutes"/> keys and the host decides how to
    /// travel.</para>
    ///
    /// <para><b>The screen owns no truth.</b> Every string comes from
    /// <see cref="ReportViewModel"/>'s copy constants or
    /// <see cref="ReportFormatter"/>, and every selection is read back out of the
    /// ViewModel on <see cref="Render"/> — the chips, checkboxes and option cards
    /// are painted from it, never from their own state. That is why
    /// <see cref="MvOptionCard"/> does not self-toggle and why the behaviour
    /// checkboxes are bound through a difference check (see
    /// <see cref="BuildBehaviourCard"/>): a control that decided its own state
    /// would be a second authority over the form.</para>
    ///
    /// <para><b>Three submit outcomes, three different endings</b> (see
    /// <see cref="OnSubmit"/>): accepted → brief status, then leave; no photo →
    /// stay and say so; the queue refused it → stay, error tone, form untouched.
    /// Only the first one navigates, because only the first one left something
    /// behind that will be delivered.</para>
    ///
    /// <para><b>The back button is hidden here.</b> Avistamentos is a bottom-bar
    /// destination with nothing behind it — see <see cref="ShowBackButton"/>.</para>
    /// </summary>
    public sealed class ReportScreen : VisualElement, IAppScreen
    {
        /// <summary>
        /// How long "Enviando avistamento…" stays on screen before the screen
        /// bounces back to Início. Long enough to read, short enough that it never
        /// feels like waiting on the upload — which it is not; the report is
        /// already durable on disk by the time this starts.
        /// </summary>
        const long SubmitExitDelayMs = 1200;

        /// <summary>Where a successful submit lands the user. Início is the app's
        /// landing route and the only tab that is never "the thing you just
        /// left".</summary>
        const string ExitRoute = AppRoutes.Home;

        readonly ReportViewModel vm;

        readonly VisualElement content;
        readonly MdTopAppBar header;
        readonly ScrollView scroll;

        readonly Label beachLabel;

        readonly VisualElement speciesSection;
        readonly List<MdChip> speciesChips = new List<MdChip>();
        readonly List<MdChip> sizeChips = new List<MdChip>();
        readonly List<MdCheckbox> behaviourBoxes = new List<MdCheckbox>();
        readonly List<MvOptionCard> profileCards = new List<MvOptionCard>();

        readonly MvMediaPicker mediaPicker;
        readonly Label photoError;

        readonly Label statusLabel;
        readonly VisualElement pendingSection;
        readonly VisualElement pendingList;

        IVisualElementScheduledItem exitTimer;
        Texture2D photoTexture;
        string photoTexturePath;
        bool statusIsError;
        bool subscribed;
        bool showBackButton;

        /// <summary>
        /// Raised with an <see cref="AppRoutes"/> key when the screen wants to be
        /// somewhere else — today only after a submit the queue took
        /// (<see cref="ExitRoute"/>). The host maps it, exactly as it does for
        /// Início and Praias.
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>The header's back button was tapped. Never raised while
        /// <see cref="ShowBackButton"/> is false, which is its default.</summary>
        public event Action BackRequested;

        /// <summary>
        /// Whether the 40dp back button V2 draws in the header is rendered.
        ///
        /// <para><b>Default false, and that is the decision.</b> V2's Tela 11/12
        /// header carries a back arrow, but Avistamentos is one of the four
        /// bottom-bar destinations: <see cref="MdRouter.Navigate"/> clears the back
        /// stack when it enters a tab, so at this point there is nothing to go
        /// back TO. A button that did nothing would be broken, and one that
        /// jumped to another tab would invent a history the app does not keep —
        /// neither of the other two tab roots (Início, Praias) draws one either.
        /// The nav bar is how you leave.</para>
        ///
        /// <para>The property exists rather than the button being deleted because
        /// §8.6 reuses this exact header for SOS, which IS a pushed sub-screen and
        /// does need it: that screen sets this true and listens to
        /// <see cref="BackRequested"/>.</para>
        /// </summary>
        public bool ShowBackButton
        {
            get => showBackButton;
            set
            {
                showBackButton = value;
                header.NavigationIcon = value ? "arrow_back" : "";
            }
        }

        public ReportScreen(ReportViewModel viewModel)
        {
            vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            AddToClassList("mv-report");
            pickingMode = PickingMode.Ignore;

            // Opaque page surface; the transparent root above it keeps the
            // navigation-bar strip unpainted and tappable (HomeScreen's contract).
            content = new VisualElement();
            content.AddToClassList("mv-report__content");
            Add(content);

            // Header 390x72: back (hidden, above) + title, 1px bottom rule. It sits
            // OUTSIDE the ScrollView — V2 scrolls only the form under it.
            header = new MdTopAppBar { Variant = MdTopAppBarVariant.Small, Title = ReportViewModel.ScreenTitle };
            header.AddToClassList("mv-report__header");
            header.NavigationIcon = "";
            header.NavigationClicked += () => BackRequested?.Invoke();
            content.Add(header);

            scroll = new AppScrollView();
            scroll.AddToClassList("mv-report__scroll");
            content.Add(scroll);

            // Form Fields Scroll: V, gap 24, padding 20/16/100/16. The 100dp bottom
            // is nav-bar clearance the shell already reserves, so only a tail is kept.
            var body = new VisualElement();
            body.AddToClassList("mv-report__body");
            scroll.Add(body);

            // Where the report will be filed. NOT in the Figma frame — the form has
            // no beach picker at all, so without this line the user cannot tell
            // which beach (or none) the sighting is being attached to, and
            // ReportViewModel.BeachText exists precisely to be shown.
            body.Add(BuildBeachLine(out beachLabel));

            speciesSection = BuildChipSection(
                ReportViewModel.SpeciesSectionTitle, vm.SpeciesOptions, speciesChips, vm.SelectSpecies);
            speciesSection.name = "species-section";  // the one section that can vanish
            body.Add(speciesSection);
            body.Add(BuildChipSection(
                ReportViewModel.SizeSectionTitle, vm.SizeOptions, sizeChips, vm.SelectSize));
            body.Add(BuildBehaviourSection());
            body.Add(BuildPhotoSection(out mediaPicker, out photoError));
            body.Add(BuildIdentitySection());
            body.Add(BuildProfileSection());
            body.Add(BuildSubmitSection(out statusLabel));
            body.Add(pendingSection = BuildPendingSection(out pendingList));

            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Unsubscribe();
                CancelExit();
                ReleasePhotoTexture();
            });

            // First paint without subscribing: OnEnter owns the subscription, but a
            // screen must never render stale rows if it is shown before its first
            // OnEnter (or by a host that only calls it on later visits).
            Render();
        }

        // ---- IAppScreen -----------------------------------------------------

        public string Key => AppRoutes.Avistamentos;

        public VisualElement Root => this;

        public void OnEnter()
        {
            if (!subscribed)
            {
                vm.Changed += Render;
                subscribed = true;
            }
            // Re-reads the pending feed and drops the previous visit's status line.
            // It deliberately does NOT touch the form, so a half-filled report
            // survives a trip to another tab.
            statusIsError = false;
            CancelExit();
            vm.Refresh();
            Render();
            scroll.scrollOffset = Vector2.zero;
        }

        public void OnExit()
        {
            Unsubscribe();
            // The user left under their own steam; do not yank them somewhere else
            // a second later.
            CancelExit();
        }

        /// <summary>
        /// Safe-area insets in panel units, handled exactly as HomeScreen does:
        /// top/left/right are padding on the opaque content container (so the page
        /// surface still paints under the status bar / notch instead of leaving a
        /// transparent strip), bottom is the navigation-bar reservation on the
        /// transparent root.
        /// </summary>
        public void SetEdgeInsets(float top, float left, float right, float bottom)
        {
            content.style.paddingTop = top;
            content.style.paddingLeft = left;
            content.style.paddingRight = right;
            style.paddingBottom = bottom;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            vm.Changed -= Render;
            subscribed = false;
        }

        // ---- Beach context --------------------------------------------------

        static VisualElement BuildBeachLine(out Label label)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("mv-report__beach");

            var icon = new MdIcon { Icon = "location_on" };
            icon.AddToClassList("mv-report__beach-icon");
            row.Add(icon);

            label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("md-typescale-label-medium");
            label.AddToClassList("mv-report__beach-label");
            row.Add(label);
            return row;
        }

        // ---- Chip sections (species, size) ----------------------------------

        /// <summary>
        /// A section label over a wrapping chip row. Both chip groups are single
        /// select with a de-selectable current choice — which is the ViewModel's
        /// rule, not the chip's: these are <see cref="MdChipKind.Assist"/> chips,
        /// so a tap only ever raises <c>Clicked</c> and the selection comes back
        /// from <see cref="Render"/>. A filter chip would flip itself first and
        /// then disagree with the ViewModel about what happened.
        /// </summary>
        static VisualElement BuildChipSection(
            string title, IReadOnlyList<ReportOption> options, List<MdChip> into, Action<int> onSelect)
        {
            var section = MakeSection(title);
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("mv-report__chip-row");

            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                var chip = new MdChip { Kind = MdChipKind.Assist, Text = options[i].Label };
                chip.AddToClassList("mv-report__chip");
                chip.Clicked += () => onSelect(index);
                into.Add(chip);
                row.Add(chip);
            }

            section.Add(row);
            return section;
        }

        // ---- Behaviour checklist --------------------------------------------

        VisualElement BuildBehaviourSection()
        {
            var section = MakeSection(ReportViewModel.BehaviourSectionTitle);
            section.Add(BuildBehaviourCard());
            return section;
        }

        /// <summary>
        /// The 4×52dp checklist card. The row is the screen's — MdCheckbox ships
        /// no label on purpose, because V2 puts the text at the far end of the row
        /// rather than beside the box.
        ///
        /// <para>The binding compares before toggling. <c>ValueChanged</c> fires on
        /// a programmatic set as well as on a tap (MdChip's contract, kept), so a
        /// plain <c>ToggleBehaviour</c> handler would invert the ViewModel every
        /// time <see cref="Render"/> pushed a box back to its real value — the
        /// classic two-way echo, and it would have made a form reset silently
        /// re-check everything.</para>
        /// </summary>
        VisualElement BuildBehaviourCard()
        {
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("mv-report__card");
            card.AddToClassList("mv-report__behaviour-card");

            var options = vm.BehaviourOptions;
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                // Pickable, unlike every other row on this screen: the whole 52dp
                // strip is the toggle. Only the 48dp box at the far right answered
                // before, which on a 358dp row means the label — the part the thumb
                // actually aims at — did nothing.
                var row = new VisualElement();
                row.AddToClassList("mv-report__behaviour-row");
                // USS has no :last-child, so the divider is removed by a modifier.
                if (i == options.Count - 1) row.AddToClassList("mv-report__behaviour-row--last");

                var label = new Label(options[i].Label) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("md-typescale-body-large");
                label.AddToClassList("mv-report__behaviour-label");
                row.Add(label);

                var box = new MdCheckbox();
                box.ValueChanged += isChecked =>
                {
                    if (isChecked != vm.IsBehaviourSelected(index)) vm.ToggleBehaviour(index);
                };
                behaviourBoxes.Add(box);
                row.Add(box);

                // Routed through the box rather than straight to the ViewModel, so a
                // row tap and a box tap are the same single path. It cannot
                // double-fire: MdCheckbox's own Clickable stops the pointer events at
                // the box, so they never bubble up to this manipulator.
                row.AddManipulator(new Clickable(box.Toggle));

                card.Add(row);
            }
            return card;
        }

        // ---- Photo ----------------------------------------------------------

        VisualElement BuildPhotoSection(out MvMediaPicker picker, out Label error)
        {
            var section = MakeSection(ReportViewModel.PhotoSectionTitle);

            // One photo this slice (Decision D6): the cap is the component's, so
            // the "add more" tile simply is not offered once a photo is attached
            // and replacing one is remove-then-add.
            picker = new MvMediaPicker
            {
                MaxItems = ReportViewModel.MaxPhotos,
                EmptyTitle = ReportViewModel.PhotoEmptyActionText,
                HintText = ReportViewModel.PhotoLimitText,
            };
            picker.AddRequested += vm.PickPhoto;
            // The index is ignored: with MaxItems = 1 there is exactly one tile,
            // and RemovePhoto is the ViewModel's whole vocabulary for removal.
            picker.RemoveRequested += _ => vm.RemovePhoto();
            section.Add(picker);

            error = new Label { pickingMode = PickingMode.Ignore };
            error.AddToClassList("md-typescale-body-small");
            error.AddToClassList("mv-report__error");
            error.style.display = DisplayStyle.None;
            section.Add(error);

            return section;
        }

        // ---- Identity -------------------------------------------------------

        /// <summary>
        /// Two optional fields. They are bound straight to the ViewModel's plain
        /// properties, which deliberately raise nothing when written: a
        /// <c>Changed</c> per keystroke would re-enter <see cref="Render"/> mid-edit
        /// and the caret would jump. Nothing ever writes <c>Value</c> back either —
        /// the ViewModel never changes these behind the user's back (a submit
        /// keeps the identity; only <c>ClearIdentity</c> drops it, and no surface
        /// on this screen calls it).
        /// </summary>
        VisualElement BuildIdentitySection()
        {
            var section = MakeSection(
                ReportViewModel.IdentitySectionTitle, ReportViewModel.IdentitySectionOptionalNote);

            var name = MakeIdentityField(ReportViewModel.NamePlaceholder, "person");
            name.ValueChanged += value => vm.ReporterName = value;
            section.Add(name);

            var email = MakeIdentityField(ReportViewModel.EmailPlaceholder, "mail");
            email.AddToClassList("mv-report__field--gutter"); // USS has no `gap`.
            email.ValueChanged += value => vm.ReporterEmail = value;
            section.Add(email);

            return section;
        }

        /// <summary>
        /// A 44dp white field with a trailing 20dp glyph. V2 draws a plain
        /// placeholder that vanishes on the first keystroke; MdTextField's label
        /// floats up onto the border instead, which is the M3 behaviour the design
        /// system already ships and keeps the field's meaning visible while typing
        /// — the deliberate difference from the frame.
        /// </summary>
        static MdTextField MakeIdentityField(string placeholder, string icon)
        {
            var field = new MdTextField
            {
                Variant = MdTextFieldVariant.Outlined,
                LabelText = placeholder,
            };
            field.AddToClassList("mv-report__field");

            var glyph = new MdIcon { Icon = icon, pickingMode = PickingMode.Ignore };
            glyph.AddToClassList("mv-report__field-icon");
            field.Add(glyph);
            return field;
        }

        // ---- Profile --------------------------------------------------------

        /// <summary>
        /// The two 174×93 cards. <see cref="MvOptionCard"/> raises
        /// <see cref="MvOptionCard.Clicked"/> and never toggles itself, so
        /// exclusivity is one line here: tell the ViewModel, and repaint both cards
        /// from <see cref="ReportViewModel.SelectedProfileIndex"/>.
        /// </summary>
        VisualElement BuildProfileSection()
        {
            var section = MakeSection(ReportViewModel.ProfileSectionTitle);

            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("mv-report__profile-row");

            var options = vm.ProfileOptions;
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                var card = new MvOptionCard { Icon = ProfileIcon(options[i].Key), Text = options[i].Label };
                if (i > 0) card.AddToClassList("mv-report__profile-card--gutter"); // USS has no `gap`.
                card.Clicked += () => vm.SelectProfile(index);
                profileCards.Add(card);
                row.Add(card);
            }

            section.Add(row);
            return section;
        }

        /// <summary>V2's Flaticon `user` / `compass` → the shipped Material Symbols
        /// equivalents (§3.7 option (a)).</summary>
        static string ProfileIcon(string profileKey) =>
            profileKey == ReportViewModel.ProfileKeyGuide ? "explore" : "person";

        // ---- Submit ---------------------------------------------------------

        VisualElement BuildSubmitSection(out Label status)
        {
            var section = new VisualElement { pickingMode = PickingMode.Ignore };
            section.AddToClassList("mv-report__section");
            section.AddToClassList("mv-report__submit-section");

            var button = new MdButton
            {
                Variant = MdButtonVariant.Filled,
                Text = ReportViewModel.SubmitLabel,
            };
            button.AddToClassList("mv-report__submit");
            // The button stays enabled with no photo: tapping it is how the user
            // learns what is missing (ReportViewModel.ValidationText).
            button.Clicked += OnSubmit;
            section.Add(button);

            status = new Label { pickingMode = PickingMode.Ignore };
            status.AddToClassList("md-typescale-body-small");
            status.AddToClassList("mv-report__status");
            status.style.display = DisplayStyle.None;
            section.Add(status);

            return section;
        }

        /// <summary>
        /// The three endings, in the order they can happen:
        /// <list type="bullet">
        /// <item><b>No photo</b> — <c>Submit</c> returns false without raising
        /// <c>Submitted</c> and leaves <c>LastSubmitAccepted</c> true. Nothing was
        /// attempted; the user stays and reads why.</item>
        /// <item><b>Refused</b> — the photo could not be copied or the queue is
        /// full. <c>LastSubmitAccepted</c> is false and the form is deliberately
        /// left filled, so the screen stays too: navigating away would drop a
        /// report nobody is going to deliver, and the user has something to do
        /// about it (try again). This is not "blocking on infrastructure" — there
        /// is nothing in flight to wait for.</item>
        /// <item><b>Accepted</b> — the report is durable on disk and the queue owns
        /// it from here. Show the status briefly, then leave; nothing on this
        /// screen waits for the upload, ever.</item>
        /// </list>
        ///
        /// <para>This reads <see cref="ReportViewModel.Submit"/>'s return value
        /// rather than subscribing to <c>Submitted</c>, because the two refusals
        /// have to be told apart and only the pair (return value,
        /// <c>LastSubmitAccepted</c>) does that — the event fires for one of them
        /// and not the other. The screen is the only caller of Submit, so nothing
        /// is missed by not listening.</para>
        /// </summary>
        void OnSubmit()
        {
            bool accepted = vm.Submit();   // raises Changed → Render() with the new status
            statusIsError = !accepted;     // both refusals read as errors, the success does not
            RenderStatus();
            if (accepted) ScheduleExit();
        }

        /// <summary>
        /// Test seam: runs the submit button's handler. EditMode has no panel, so
        /// the button's <c>Clickable</c> cannot be driven with a synthetic pointer
        /// event, and <see cref="OnSubmit"/> — which decides the tone of the status
        /// line and whether the screen leaves at all — is the part worth pinning.
        /// </summary>
        internal void SubmitForTests() => OnSubmit();

        /// <summary>The repaint a retry tap triggers. EditMode has no panel, so the
        /// tap itself is a ViewModel call and this is its other half.</summary>
        internal void RenderPendingForTests() => RenderPending();

        void ScheduleExit()
        {
            CancelExit();
            // Detached (EditMode tests): there is no panel to show the status on and
            // no scheduler to run, so the exit is immediate rather than lost.
            if (panel == null)
            {
                NavigationRequested?.Invoke(ExitRoute);
                return;
            }
            exitTimer = schedule.Execute(() => NavigationRequested?.Invoke(ExitRoute))
                .StartingIn(SubmitExitDelayMs);
        }

        void CancelExit()
        {
            exitTimer?.Pause();
            exitTimer = null;
        }

        // ---- Pending feed ---------------------------------------------------

        VisualElement BuildPendingSection(out VisualElement list)
        {
            var section = new VisualElement { pickingMode = PickingMode.Ignore };
            section.AddToClassList("mv-report__section");
            section.AddToClassList("mv-report__pending-section");

            // Tela 11 draws a 1dp inset rule between the button and the feed (Tela
            // 12 does not — the frames disagree); it belongs to the feed, so it
            // hides with it.
            var divider = new VisualElement { pickingMode = PickingMode.Ignore };
            divider.AddToClassList("mv-report__divider");
            section.Add(divider);

            var title = new Label(ReportViewModel.PendingSectionTitle) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("md-typescale-title-small");
            title.AddToClassList("mv-report__section-title");
            section.Add(title);

            list = new VisualElement { pickingMode = PickingMode.Ignore };
            list.AddToClassList("mv-report__pending-list");
            section.Add(list);

            return section;
        }

        /// <summary>
        /// One feed card: species (or "Avistamento") + the state pill, over the
        /// "Hoje, 9:32 · Baía do Sueste" caption. Every string is already formatted
        /// by <see cref="ReportFormatter"/>; the screen only picks the pill's
        /// colour.
        /// </summary>
        VisualElement MakePendingCard(ReportPendingRow row)
        {
            // Position (not Ignore) only when the card carries the retry button:
            // an inert card must keep letting taps fall through to the scroll view.
            var card = new VisualElement
            {
                pickingMode = row.CanRetry ? PickingMode.Position : PickingMode.Ignore,
            };
            card.AddToClassList("mv-report__card");
            card.AddToClassList("mv-report__pending-card");
            if (row.State == SightingState.Failed)
                card.AddToClassList("mv-report__pending-card--failed");

            var titleRow = new VisualElement { pickingMode = PickingMode.Ignore };
            titleRow.AddToClassList("mv-report__pending-title-row");

            var title = new Label(row.TitleText) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("mv-report__pending-title");
            titleRow.Add(title);

            var pill = new MvTag
            {
                Text = row.StatusText,
                Size = MvTagSize.Small,
                Variant = PillVariant(row.State),
            };
            pill.AddToClassList("mv-report__pending-pill");
            titleRow.Add(pill);
            card.Add(titleRow);

            var caption = new Label(row.CaptionText) { pickingMode = PickingMode.Ignore };
            caption.AddToClassList("md-typescale-body-medium");
            caption.AddToClassList("mv-report__pending-caption");
            card.Add(caption);

            // A permanently-failed report is otherwise a dead end: the queue will
            // never touch it again and the user has no way to say "try again".
            // Only Failed rows get this — the other three states are already being
            // retried, so a button there would promise what is already happening.
            if (row.CanRetry)
            {
                string id = row.Id;
                // Outlined, not Text: `primary` in this palette is near-black navy,
                // so a text button on a white card is indistinguishable from the
                // caption above it. The border and the glyph make it an affordance.
                var retry = new MdButton
                {
                    Variant = MdButtonVariant.Outlined,
                    Icon = "refresh",
                    Text = row.RetryText,
                };
                retry.AddToClassList("mv-report__pending-retry");
                retry.Clicked += () =>
                {
                    vm.RetryPending(id);
                    RenderPending();
                };
                card.Add(retry);
            }

            return card;
        }

        /// <summary>
        /// Row state → pill colour. V2 only ever draws the grey "Pendente" pill,
        /// which is right for a report that is simply waiting; the other three are
        /// not that. Retrying and "sem conexão" take the warning role (something is
        /// in the way, it will resolve), and <see cref="SightingState.Failed"/>
        /// takes the error role — a submission that will never be delivered must
        /// not look like one that is merely queued, which is the whole reason
        /// <c>ISightingReports.ListFailed</c> is in the feed at all.
        /// </summary>
        internal static MvTagVariant PillVariant(SightingState state)
        {
            switch (state)
            {
                case SightingState.Retrying:
                case SightingState.WaitingForNetwork:
                    return MvTagVariant.Warning;
                case SightingState.Failed:
                    return MvTagVariant.Error;
                default:
                    return MvTagVariant.Neutral;
            }
        }

        // ---- Shared section shell -------------------------------------------

        /// <summary>Section = a 15/700 label over its content (§8.5: "each section
        /// V, gap 10, label Inter 700 15").</summary>
        static VisualElement MakeSection(string title) => MakeSection(title, null);

        /// <summary>
        /// The same section shell with a second, quieter string after the heading —
        /// V2's "Se identifique (opcional)".
        ///
        /// <para>In Figma that is ONE text node carrying mixed character styling: the
        /// words in the heading role, the parenthetical in the muted body role. A UI
        /// Toolkit Label has a single style, so it becomes two labels in a row. Both
        /// strings still come from the ViewModel — the screen only decides where they
        /// sit, which is the whole point of splitting it rather than leaving the
        /// parenthetical to inherit 15/700 navy.</para>
        ///
        /// <para>Nothing here styles anything: the row's direction, its baseline
        /// alignment, the gap and the note's muted colour are all
        /// <c>.mv-report__section-heading</c> / <c>.mv-report__section-optional</c>
        /// in the stylesheet. This method only decides which strings exist and where
        /// they sit in the tree.</para>
        /// </summary>
        static VisualElement MakeSection(string title, string optionalNote)
        {
            var section = new VisualElement { pickingMode = PickingMode.Ignore };
            section.AddToClassList("mv-report__section");

            var heading = new Label(title) { pickingMode = PickingMode.Ignore };
            heading.AddToClassList("md-typescale-title-small");
            heading.AddToClassList("mv-report__section-title");

            if (string.IsNullOrEmpty(optionalNote))
            {
                section.Add(heading);
                return section;
            }

            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("mv-report__section-heading");
            row.Add(heading);

            var note = new Label(optionalNote) { pickingMode = PickingMode.Ignore };
            note.AddToClassList("md-typescale-body-medium");
            note.AddToClassList("mv-report__section-optional");
            row.Add(note);

            section.Add(row);
            return section;
        }

        // ---- Rendering ------------------------------------------------------

        void Render()
        {
            beachLabel.text = vm.BeachText;

            // An empty catalog (no AnimalDefs) is a real state: the section is not
            // built into the layout at all rather than showing a heading over
            // nothing — the same rule the Praia detalhe blocks follow.
            speciesSection.style.display = vm.HasSpeciesOptions ? DisplayStyle.Flex : DisplayStyle.None;

            for (int i = 0; i < speciesChips.Count; i++)
                speciesChips[i].Selected = i == vm.SelectedSpeciesIndex;
            for (int i = 0; i < sizeChips.Count; i++)
                sizeChips[i].Selected = i == vm.SelectedSizeIndex;
            for (int i = 0; i < behaviourBoxes.Count; i++)
                behaviourBoxes[i].Checked = vm.IsBehaviourSelected(i);
            for (int i = 0; i < profileCards.Count; i++)
                profileCards[i].Selected = i == vm.SelectedProfileIndex;

            RenderPhoto();
            RenderStatus();
            RenderPending();
        }

        void RenderPhoto()
        {
            // The picker is inert while the OS gallery is open — the user is in
            // another app, and a second AddRequested would be a no-op anyway.
            mediaPicker.SetEnabled(!vm.IsPickingPhoto);

            string path = vm.PhotoPath;
            if (path != photoTexturePath)
            {
                ReleasePhotoTexture();
                photoTexturePath = path;
                photoTexture = LoadTexture(path);
                // A tile is shown whenever the ViewModel holds a photo, even if the
                // file could not be decoded: the report HAS a photo and the tile is
                // the only way to remove it. An empty thumb beats a picker that
                // claims nothing is attached while Submit happily sends it.
                mediaPicker.SetItems(vm.HasPhoto
                    ? new[] { new MvMediaPickerItem(photoTexture) }
                    : null);
            }

            bool hasError = vm.HasPhotoError;
            photoError.text = hasError ? vm.PhotoErrorText : "";
            photoError.style.display = hasError ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void RenderStatus()
        {
            bool has = vm.HasStatus;
            statusLabel.text = has ? vm.StatusText : "";
            statusLabel.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            statusLabel.EnableInClassList("mv-report__status--error", has && statusIsError);
        }

        void RenderPending()
        {
            var rows = vm.PendingRows;
            // No rows = no section. The design has no empty state for the feed
            // (Tela 11 shows it holding a card), and a heading over nothing would
            // read as a delivery that went missing.
            pendingSection.style.display = rows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            pendingList.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                var card = MakePendingCard(rows[i]);
                if (i > 0) card.AddToClassList("mv-report__pending-card--gutter"); // USS has no `gap`.
                pendingList.Add(card);
            }
        }

        // ---- Photo texture --------------------------------------------------

        /// <summary>
        /// Decodes the picked file for the thumbnail. The ViewModel stays
        /// engine-free, so loading is the screen's job; the bytes are read
        /// read-only and never written back — the upload path uploads the ORIGINAL
        /// file and any re-encode here would be invisible to it (CLAUDE.md, "EXIF
        /// is load-bearing").
        /// </summary>
        static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (!File.Exists(path)) return null;
                var texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                if (texture.LoadImage(File.ReadAllBytes(path), markNonReadable: true)) return texture;
                DestroyTexture(texture);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ReportScreen] Could not load the photo thumbnail: {e.Message}");
            }
            return null;
        }

        void ReleasePhotoTexture()
        {
            DestroyTexture(photoTexture);
            photoTexture = null;
            photoTexturePath = null;
        }

        /// <summary>Destroy is illegal in edit mode (the screenshot harness and the
        /// EditMode tests both run there), DestroyImmediate is illegal at runtime.</summary>
        static void DestroyTexture(Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
