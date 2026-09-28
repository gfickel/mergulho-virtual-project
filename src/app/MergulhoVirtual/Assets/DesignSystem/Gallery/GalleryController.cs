using System.Linq;
using MergulhoVirtual.DesignSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem.Gallery
{
    /// <summary>
    /// The component gallery — every component, every variant, every state,
    /// with a light/dark toggle. Runs in the editor for fast iteration and in
    /// debug device builds for real touch/DPI verification.
    /// The catalog is built in code; add a Build*Section method per component.
    /// </summary>
    public class GalleryController : MonoBehaviour
    {
        [SerializeField] UIDocument document;
        [SerializeField] ThemeStyleSheet lightTheme;
        [SerializeField] ThemeStyleSheet darkTheme;
        [SerializeField] StyleSheet galleryStyles;

        ThemeStyleSheet _originalTheme;
        MdIconButton _themeToggle;
        readonly System.Collections.Generic.List<TokenSwatch> _swatches = new();
        readonly System.Collections.Generic.List<Texture2D> _demoTextures = new();
        bool _dark;

        void OnEnable()
        {
            _originalTheme = document.panelSettings.themeStyleSheet;
            var root = document.rootVisualElement;
            root.Clear();
            _swatches.Clear();
            DestroyDemoTextures();
            root.style.flexGrow = 1;
            if (galleryStyles != null)
                root.styleSheets.Add(galleryStyles);

            var safeArea = new SafeAreaElement();
            safeArea.AddToClassList("gallery-root");
            root.Add(safeArea);

            safeArea.Add(BuildHeader());

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gallery-scroll");
            safeArea.Add(scroll);

            BuildColorsSection(scroll);
            BuildTypographySection(scroll);
            BuildButtonsSection(scroll);
            BuildIconButtonsSection(scroll);
            BuildChipsSection(scroll);
            BuildFabSection(scroll);
            BuildProgressSection(scroll);
            BuildCardsSection(scroll);
            BuildListItemsSection(scroll);
            BuildTextFieldsSection(scroll);
            BuildDropdownSection(scroll);
            BuildTopAppBarSection(scroll);
            BuildNavigationBarSection(scroll);
            BuildDialogSection(scroll);
            BuildSnackbarSection(scroll);
            BuildBottomSheetSection(scroll);
            BuildSparklineSection(scroll);
            BuildTagsSection(scroll);
            BuildAlertBarSection(scroll);
            BuildNumberedListSection(scroll);
            BuildHeroHeaderSection(scroll);
            BuildMediaCarouselSection(scroll);
            BuildIconsSection(scroll);
        }

        void OnDestroy()
        {
            DestroyDemoTextures();
            // Don't leave the (persistent) PanelSettings asset pointing at the
            // toggled theme after leaving play mode in the editor.
            if (document != null && document.panelSettings != null && _originalTheme != null)
                document.panelSettings.themeStyleSheet = _originalTheme;
        }

        VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("gallery-header");

            var title = new Label("Design System");
            title.AddToClassList("md-typescale-headline-small");
            title.AddToClassList("gallery-title");
            header.Add(title);

            _themeToggle = new MdIconButton { Icon = "dark_mode", Variant = MdIconButtonVariant.Tonal };
            _themeToggle.Clicked += ToggleTheme;
            header.Add(_themeToggle);
            return header;
        }

        void ToggleTheme()
        {
            _dark = !_dark;
            document.panelSettings.themeStyleSheet = _dark ? darkTheme : lightTheme;
            _themeToggle.Icon = _dark ? "light_mode" : "dark_mode";
            foreach (var swatch in _swatches)
                swatch.Refresh();
        }

        // ---- Sections -----------------------------------------------------

        static VisualElement Section(VisualElement parent, string title, string description)
        {
            var section = new VisualElement();
            section.AddToClassList("gallery-section");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("md-typescale-title-large");
            titleLabel.AddToClassList("gallery-section__title");
            section.Add(titleLabel);

            var desc = new Label(description);
            desc.AddToClassList("md-typescale-body-small");
            desc.AddToClassList("gallery-section__desc");
            section.Add(desc);

            var row = new VisualElement();
            row.AddToClassList("gallery-row");
            section.Add(row);

            parent.Add(section);
            return row;
        }

        void BuildColorsSection(VisualElement parent)
        {
            var row = Section(parent, "Color tokens", "Resolved from the active theme — toggle dark mode above.");
            // Each role needs a matching .gallery-swatch__color--<role> rule in Gallery.uss.
            string[] roles =
            {
                "primary", "on-primary", "primary-container", "on-primary-container",
                "secondary-container", "on-secondary-container",
                "tertiary-container", "on-tertiary-container",
                "surface", "on-surface", "surface-variant", "on-surface-variant",
                "surface-container-low", "surface-container", "surface-container-high",
                "outline", "outline-variant", "error", "error-container",
                "inverse-surface", "inverse-primary",
            };
            foreach (var role in roles)
            {
                var swatch = new TokenSwatch(role);
                _swatches.Add(swatch);
                row.Add(swatch);
            }
        }

        void BuildTypographySection(VisualElement parent)
        {
            var row = Section(parent, "Typography", "Type scale — Inter, retuned to the V2 design.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.FlexStart;
            string[] styles =
            {
                "display-small", "headline-medium", "headline-small", "title-large",
                "title-medium", "title-small", "body-large", "body-medium", "body-small",
                "label-large", "label-medium", "label-small",
            };
            foreach (var s in styles)
            {
                var sample = new Label($"{s} — Mergulho Virtual");
                sample.AddToClassList($"md-typescale-{s}");
                row.Add(sample);
            }
        }

        void BuildButtonsSection(VisualElement parent)
        {
            var row = Section(parent, "MdButton", "Filled / tonal / outlined / text · with icon · disabled. Tap to count clicks.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.FlexStart;

            var clicks = new Label("Clicks: 0");
            clicks.AddToClassList("md-typescale-label-medium");
            int count = 0;

            foreach (MdButtonVariant variant in System.Enum.GetValues(typeof(MdButtonVariant)))
            {
                var variantRow = new VisualElement();
                variantRow.AddToClassList("gallery-row");

                var plain = new MdButton { Variant = variant, Text = variant.ToString() };
                plain.Clicked += () => clicks.text = $"Clicks: {++count}";

                var withIcon = new MdButton { Variant = variant, Text = "Avistar", Icon = "photo_camera" };
                withIcon.Clicked += () => clicks.text = $"Clicks: {++count}";

                var disabled = new MdButton { Variant = variant, Text = "Disabled" };
                disabled.SetEnabled(false);

                variantRow.Add(plain);
                variantRow.Add(withIcon);
                variantRow.Add(disabled);
                row.Add(variantRow);
            }
            row.Add(clicks);
        }

        void BuildIconButtonsSection(VisualElement parent)
        {
            var row = Section(parent, "MdIconButton", "Standard / filled / tonal / outlined · disabled.");
            foreach (MdIconButtonVariant variant in System.Enum.GetValues(typeof(MdIconButtonVariant)))
            {
                row.Add(new MdIconButton { Variant = variant, Icon = "favorite" });
                var disabled = new MdIconButton { Variant = variant, Icon = "favorite" };
                disabled.SetEnabled(false);
                row.Add(disabled);
            }
        }

        void BuildChipsSection(VisualElement parent)
        {
            var row = Section(parent, "MdChip", "Assist (with icon) and filter chips — filters toggle on tap.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.FlexStart;

            var assistRow = new VisualElement();
            assistRow.AddToClassList("gallery-row");
            assistRow.Add(new MdChip { Kind = MdChipKind.Assist, Text = "Tábua de marés", Icon = "waves" });
            assistRow.Add(new MdChip { Kind = MdChipKind.Assist, Text = "Como chegar", Icon = "map" });
            var disabledAssist = new MdChip { Kind = MdChipKind.Assist, Text = "Disabled", Icon = "block" };
            disabledAssist.SetEnabled(false);
            assistRow.Add(disabledAssist);
            row.Add(assistRow);

            var filterRow = new VisualElement();
            filterRow.AddToClassList("gallery-row");
            var status = new Label("Selecionadas: —");
            status.AddToClassList("md-typescale-label-medium");
            string[] beaches = { "Sancho", "Cacimba do Padre", "Sueste", "Porto" };
            var chips = beaches.Select(b => new MdChip { Kind = MdChipKind.Filter, Text = b }).ToList();
            foreach (var chip in chips)
            {
                chip.SelectedChanged += _ =>
                {
                    var selected = chips.Where(c => c.Selected).Select(c => c.Text).ToList();
                    status.text = "Selecionadas: " + (selected.Count > 0 ? string.Join(", ", selected) : "—");
                };
                filterRow.Add(chip);
            }
            row.Add(filterRow);
            row.Add(status);
        }

        void BuildFabSection(VisualElement parent)
        {
            var row = Section(parent, "MdFab", "Colors primary/secondary/tertiary/surface · sizes small/regular/large · disabled.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.FlexStart;

            var colorRow = new VisualElement();
            colorRow.AddToClassList("gallery-row");
            foreach (MdFabColor color in System.Enum.GetValues(typeof(MdFabColor)))
                colorRow.Add(new MdFab { Color = color, Icon = "photo_camera" });
            row.Add(colorRow);

            var sizeRow = new VisualElement();
            sizeRow.AddToClassList("gallery-row");
            sizeRow.Add(new MdFab { Size = MdFabSize.Small, Icon = "add" });
            sizeRow.Add(new MdFab { Size = MdFabSize.Regular, Icon = "add" });
            sizeRow.Add(new MdFab { Size = MdFabSize.Large, Icon = "add" });
            var disabled = new MdFab { Icon = "add_a_photo" };
            disabled.SetEnabled(false);
            sizeRow.Add(disabled);
            row.Add(sizeRow);
        }

        void BuildProgressSection(VisualElement parent)
        {
            var row = Section(parent, "MdProgressIndicator", "Linear + circular · determinate (−/+ changes value) and indeterminate.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.FlexStart;

            var linear = new MdLinearProgress { Value = 0.65f };
            var circular = new MdCircularProgress { Value = 0.65f };
            var valueLabel = new Label("65%");
            valueLabel.AddToClassList("md-typescale-label-medium");
            float value = 0.65f;
            void SetValue(float v)
            {
                value = Mathf.Clamp01(v);
                linear.Value = value;
                circular.Value = value;
                valueLabel.text = $"{Mathf.RoundToInt(value * 100)}%";
            }

            var determinateRow = new VisualElement();
            determinateRow.AddToClassList("gallery-row");
            var minus = new MdIconButton { Icon = "remove", Variant = MdIconButtonVariant.Tonal };
            minus.Clicked += () => SetValue(value - 0.1f);
            var plus = new MdIconButton { Icon = "add", Variant = MdIconButtonVariant.Tonal };
            plus.Clicked += () => SetValue(value + 0.1f);
            linear.style.width = 240;
            linear.style.alignSelf = Align.Auto;
            determinateRow.Add(minus);
            determinateRow.Add(plus);
            determinateRow.Add(linear);
            determinateRow.Add(circular);
            determinateRow.Add(valueLabel);
            row.Add(determinateRow);

            var indeterminateRow = new VisualElement();
            indeterminateRow.AddToClassList("gallery-row");
            var indeterminateLinear = new MdLinearProgress { Indeterminate = true };
            indeterminateLinear.style.width = 240;
            indeterminateLinear.style.alignSelf = Align.Auto;
            indeterminateRow.Add(indeterminateLinear);
            indeterminateRow.Add(new MdCircularProgress { Indeterminate = true });
            row.Add(indeterminateRow);
        }

        void BuildCardsSection(VisualElement parent)
        {
            var row = Section(parent, "MdCard", "Elevated / filled / outlined — container only, content is slotted children.");
            foreach (MdCardVariant variant in System.Enum.GetValues(typeof(MdCardVariant)))
            {
                var card = new MdCard { Variant = variant };
                card.style.width = 220;

                // MdCard is a container only, so this demo content owns its own
                // rhythm. Spelled out because Label metrics are zeroed app-wide in
                // Tokens/_typography.uss — nothing may lean on Unity's default
                // Label padding/margin for spacing any more.
                var title = new Label(variant.ToString());
                title.AddToClassList("md-typescale-title-medium");
                title.style.marginBottom = 4;
                card.Add(title);

                var body = new Label("Tubarão-limão avistado na Praia do Sancho.");
                body.AddToClassList("md-typescale-body-medium");
                body.style.whiteSpace = WhiteSpace.Normal;
                body.style.marginBottom = 8;
                card.Add(body);

                var action = new MdButton { Variant = MdButtonVariant.Text, Text = "Ver mais" };
                card.Add(action);

                row.Add(card);
            }
        }

        void BuildListItemsSection(VisualElement parent)
        {
            var row = Section(parent, "MdListItem", "1/2/3-line · leading icon or image · trailing icon · disabled. Tap to select.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.FlexStart;

            var status = new Label("Selecionado: —");
            status.AddToClassList("md-typescale-label-medium");

            var list = new VisualElement();
            list.style.width = 360;

            void Wire(MdListItem item)
            {
                item.Clicked += () => status.text = $"Selecionado: {item.Headline}";
                list.Add(item);
            }

            Wire(new MdListItem
            {
                Headline = "Praia do Sancho",
                LeadingIcon = "beach_access",
                TrailingIcon = "chevron_right",
            });

            // Leading image: a runtime placeholder texture stands in for the
            // beach/animal photos real screens will pass via SetLeadingImage.
            var photo = new Texture2D(1, 1);
            photo.SetPixel(0, 0, new Color(0f, 0.55f, 0.62f));
            photo.Apply();
            var withImage = new MdListItem
            {
                Headline = "Tubarão-limão",
                SupportingText = "Negaprion brevirostris · até 3,4 m",
                TrailingIcon = "chevron_right",
            };
            withImage.SetLeadingImage(photo);
            Wire(withImage);

            Wire(new MdListItem
            {
                Headline = "Cacimba do Padre",
                SupportingText = "Mar de dentro. Faixa de areia extensa com o Morro Dois Irmãos ao fundo; ondas fortes no verão.",
                ThreeLine = true,
                LeadingIcon = "waves",
                TrailingIcon = "chevron_right",
            });

            var disabled = new MdListItem
            {
                Headline = "Indisponível",
                SupportingText = "Fechada para visitação",
                LeadingIcon = "block",
            };
            disabled.SetEnabled(false);
            list.Add(disabled);

            row.Add(list);
            row.Add(status);
        }

        void BuildTextFieldsSection(VisualElement parent)
        {
            var row = Section(parent, "MdTextField", "Filled / outlined · floating label · supporting text · error · disabled. Type to see the live value.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.FlexStart;

            var live = new Label("Valor: —");
            live.AddToClassList("md-typescale-label-medium");

            var filled = new MdTextField
            {
                Variant = MdTextFieldVariant.Filled,
                LabelText = "Espécie",
                SupportingText = "Nome popular ou científico",
            };
            filled.style.width = 280;
            filled.ValueChanged += v => live.text = $"Valor: {(v.Length > 0 ? v : "—")}";

            var outlined = new MdTextField
            {
                Variant = MdTextFieldVariant.Outlined,
                LabelText = "Observações",
            };
            outlined.style.width = 280;

            var error = new MdTextField
            {
                Variant = MdTextFieldVariant.Filled,
                LabelText = "Praia",
                Value = "Atalaia",
                ErrorText = "Praia fechada para visitação",
            };
            error.style.width = 280;

            var disabled = new MdTextField
            {
                Variant = MdTextFieldVariant.Filled,
                LabelText = "Data",
                Value = "14/09/2026",
            };
            disabled.style.width = 280;
            disabled.SetEnabled(false);

            var fieldsRow = new VisualElement();
            fieldsRow.AddToClassList("gallery-row");
            fieldsRow.Add(filled);
            fieldsRow.Add(outlined);
            fieldsRow.Add(error);
            fieldsRow.Add(disabled);
            row.Add(fieldsRow);
            row.Add(live);
        }

        void BuildDropdownSection(VisualElement parent)
        {
            var row = Section(parent, "MdDropdown", "Exposed dropdown menu over the shared overlay layer — tap to open, tap outside to dismiss.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.FlexStart;

            var status = new Label("Praia: —");
            status.AddToClassList("md-typescale-label-medium");

            var dropdown = new MdDropdown { LabelText = "Praia" };
            dropdown.style.width = 280;
            dropdown.SetChoices(new[]
            {
                "Automático (GPS)", "Praia do Sancho", "Cacimba do Padre", "Baía do Sueste",
                "Praia do Porto", "Praia da Conceição", "Praia do Boldró", "Praia do Leão",
            });
            dropdown.SelectionChanged += i =>
                status.text = "Praia: " + (i >= 0 ? dropdown.Value : "—");

            var disabled = new MdDropdown { LabelText = "Espécie" };
            disabled.style.width = 280;
            disabled.SetChoices(new[] { "Tubarão-limão" });
            disabled.Index = 0;
            disabled.SetEnabled(false);

            var fieldsRow = new VisualElement();
            fieldsRow.AddToClassList("gallery-row");
            fieldsRow.Add(dropdown);
            fieldsRow.Add(disabled);
            row.Add(fieldsRow);
            row.Add(status);
        }

        void BuildTopAppBarSection(VisualElement parent)
        {
            var row = Section(parent, "MdTopAppBar", "Small / center-aligned / medium / large · nav icon + trailing actions. Static (no collapse yet).");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.Stretch;

            var status = new Label("Última ação: —");
            status.AddToClassList("md-typescale-label-medium");

            MdTopAppBar Bar(MdTopAppBarVariant variant, string title)
            {
                var bar = new MdTopAppBar { Variant = variant, Title = title, NavigationIcon = "arrow_back" };
                bar.NavigationClicked += () => status.text = $"Última ação: voltar ({variant})";
                bar.AddAction("search", () => status.text = $"Última ação: buscar ({variant})");
                bar.AddAction("more_vert", () => status.text = $"Última ação: mais ({variant})");
                bar.style.width = 380;
                bar.style.marginBottom = 12;
                return bar;
            }

            row.Add(Bar(MdTopAppBarVariant.Small, "Praias"));
            row.Add(Bar(MdTopAppBarVariant.CenterAligned, "Mergulho Virtual"));
            row.Add(Bar(MdTopAppBarVariant.Medium, "Animais marinhos"));
            row.Add(Bar(MdTopAppBarVariant.Large, "Fernando de Noronha"));

            // No-nav-icon variant: title starts at the leading edge.
            var noNav = new MdTopAppBar { Variant = MdTopAppBarVariant.Small, Title = "Sem navegação" };
            noNav.style.width = 380;
            row.Add(noNav);
            row.Add(status);
        }

        void BuildNavigationBarSection(VisualElement parent)
        {
            var row = Section(parent, "MdNavigationBar",
                "The V2 bottom bar (DESIGN_IMPLEMENTATION.md §4): inverse-surface container with a " +
                "navy top border, amber active pill, inactive destinations at white 50%.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.Stretch;

            var status = new Label("Destino: Início");
            status.AddToClassList("md-typescale-label-medium");

            // V2's four destinations. Icons are the Material Symbols stand-ins agreed in
            // DESIGN_IMPLEMENTATION.md §3.7 option (a):
            //   fi-sr-home -> home · compass -> explore · fi-sr-map-marker -> location_on
            //
            // TODO(D7): "Avistamentos" is drawn with a CUSTOM SHARK-FIN glyph in the Figma
            // (the node misleadingly named "heart"). Material Symbols has NO equivalent --
            // see Decision D7 and the TODO at the end of
            // tools/design_system/material_symbols_icons.txt. `help` below is a deliberate
            // PLACEHOLDER: a question mark reads as "asset missing" instead of quietly
            // shipping a wrong-but-plausible icon (waves / surfing / scuba_diving were all
            // considered and rejected). Swap it the moment the designer exports the fin.
            const string avistamentosIconPlaceholderD7 = "help";

            var destinations = new[]
            {
                new MdNavDestination("home", "Início"),
                new MdNavDestination("explore", "Mergulho"),
                new MdNavDestination("location_on", "Praias"),
                new MdNavDestination(avistamentosIconPlaceholderD7, "Avistamentos"),
            };
            var bar = new MdNavigationBar();
            bar.SetDestinations(destinations);
            bar.SelectedIndex = 0;
            bar.SelectionChanged += i =>
                status.text = "Destino: " + (i >= 0 ? destinations[i].Label : "—");
            bar.style.width = 390;   // V2 frame width; 1 USS px = 1 dp = 1 Figma pt
            bar.style.marginBottom = 12;

            row.Add(bar);
            row.Add(status);

            // Layout check: V2 uses four destinations, but the component is specced for
            // 3-5 and the USS must not overflow at either end (space-between + fixed 64dp
            // tabs). Shown here so a regression is visible rather than theoretical.
            var spread = new Label("3 e 5 destinos — verificação de layout (o design usa 4)");
            spread.AddToClassList("md-typescale-label-medium");
            spread.style.marginTop = 12;
            spread.style.marginBottom = 8;
            row.Add(spread);

            var three = new MdNavigationBar();
            three.SetDestinations(new[] { destinations[0], destinations[1], destinations[2] });
            three.SelectedIndex = 1;
            three.style.width = 390;
            three.style.marginBottom = 12;
            row.Add(three);

            var five = new MdNavigationBar();
            five.SetDestinations(new[]
            {
                destinations[0], destinations[1], destinations[2], destinations[3],
                new MdNavDestination("info", "Sobre"),
            });
            five.SelectedIndex = 4;
            five.style.width = 390;
            row.Add(five);
        }

        void BuildDialogSection(VisualElement parent)
        {
            var row = Section(parent, "MdDialog", "Modal dialog on the shared overlay — confirm/dismiss actions, optional icon. Scrim tap dismisses.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.FlexStart;

            var status = new Label("Resultado: —");
            status.AddToClassList("md-typescale-label-medium");

            var basic = new MdButton { Variant = MdButtonVariant.Tonal, Text = "Duas ações" };
            basic.Clicked += () => MdDialog.Open(basic,
                "Descartar registro?",
                "As informações preenchidas e a foto selecionada serão perdidas.",
                "Descartar", () => status.text = "Resultado: descartado",
                "Cancelar", () => status.text = "Resultado: cancelado");

            var withIcon = new MdButton { Variant = MdButtonVariant.Tonal, Text = "Com ícone" };
            withIcon.Clicked += () => MdDialog.Open(withIcon,
                "Enviar avistamento?",
                "A foto será enviada quando houver conexão com a internet.",
                "Enviar", () => status.text = "Resultado: enviado",
                "Cancelar", () => status.text = "Resultado: cancelado",
                icon: "add_a_photo");

            var single = new MdButton { Variant = MdButtonVariant.Tonal, Text = "Uma ação" };
            single.Clicked += () => MdDialog.Open(single,
                "Sem conexão",
                "O avistamento ficará na fila e será enviado automaticamente.",
                "Entendi", () => status.text = "Resultado: entendi");

            var buttonsRow = new VisualElement();
            buttonsRow.AddToClassList("gallery-row");
            buttonsRow.Add(basic);
            buttonsRow.Add(withIcon);
            buttonsRow.Add(single);
            row.Add(buttonsRow);
            row.Add(status);
        }

        void BuildSnackbarSection(VisualElement parent)
        {
            var row = Section(parent, "MdSnackbar", "Bottom-docked brief message — auto-dismisses in 4 s; a new one replaces the current.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.FlexStart;

            var status = new Label("Ação: —");
            status.AddToClassList("md-typescale-label-medium");

            var plain = new MdButton { Variant = MdButtonVariant.Tonal, Text = "Mensagem" };
            plain.Clicked += () => MdSnackbar.Show(plain, "Avistamento enviado.");

            var withAction = new MdButton { Variant = MdButtonVariant.Tonal, Text = "Com ação" };
            withAction.Clicked += () => MdSnackbar.Show(withAction,
                "Sem conexão — avistamento na fila.",
                "Tentar agora", () => status.text = "Ação: tentar agora");

            var buttonsRow = new VisualElement();
            buttonsRow.AddToClassList("gallery-row");
            buttonsRow.Add(plain);
            buttonsRow.Add(withAction);
            row.Add(buttonsRow);
            row.Add(status);
        }

        void BuildBottomSheetSection(VisualElement parent)
        {
            var row = Section(parent, "MdBottomSheet", "Modal bottom sheet on the shared overlay — drag handle + slotted content. Scrim tap dismisses.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.FlexStart;

            var status = new Label("Estado: —");
            status.AddToClassList("md-typescale-label-medium");

            MdButton MakeOpener(string text, bool showHandle)
            {
                var opener = new MdButton { Variant = MdButtonVariant.Tonal, Text = text };
                opener.Clicked += () =>
                {
                    var sheet = MdBottomSheet.Open(opener);
                    sheet.ShowHandle = showHandle;
                    sheet.Closed += () => status.text = "Estado: fechado";

                    var title = new Label("Tubarão-martelo");
                    title.AddToClassList("md-typescale-headline-small");
                    sheet.Add(title);

                    var body = new Label(
                        "O tubarão-martelo (Sphyrna mokarran) usa a cabeça em forma " +
                        "de martelo para detectar presas enterradas na areia. Em " +
                        "Fernando de Noronha é avistado com frequência na Baía dos " +
                        "Golfinhos e no Sueste.");
                    body.AddToClassList("md-typescale-body-medium");
                    body.style.whiteSpace = WhiteSpace.Normal;
                    body.style.marginTop = 8;
                    sheet.Add(body);

                    var close = new MdButton { Variant = MdButtonVariant.Text, Text = "Fechar" };
                    close.Clicked += sheet.Close;
                    close.style.alignSelf = Align.FlexEnd;
                    close.style.marginTop = 16;
                    sheet.Add(close);

                    status.text = "Estado: aberto";
                };
                return opener;
            }

            var buttonsRow = new VisualElement();
            buttonsRow.AddToClassList("gallery-row");
            buttonsRow.Add(MakeOpener("Abrir", showHandle: true));
            buttonsRow.Add(MakeOpener("Sem alça", showHandle: false));
            row.Add(buttonsRow);
            row.Add(status);
        }

        void BuildSparklineSection(VisualElement parent)
        {
            var row = Section(parent, "MdSparkline", "Painter2D tide curve, token-colored — fill + line, dashed baseline (Nível Médio), extremum dots + labels.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.Stretch;

            // Synthetic 24h semidiurnal tide around the published Nível Médio.
            var heights = new float[25];
            for (int i = 0; i < heights.Length; i++)
                heights[i] = 1.28f + 1.05f * Mathf.Sin((i - 3.5f) / 12.4f * 2f * Mathf.PI);

            var sparkline = new MdSparkline
            {
                Baseline = 1.28f,
                ExtremumLabelFormatter = (i, isHigh) => (isHigh ? "▲ " : "▼ ") + $"{i:00}:00",
            };
            sparkline.SetSamples(heights);
            sparkline.style.height = 120;
            sparkline.style.maxWidth = 420;
            row.Add(sparkline);
        }

        // ---- Mv* components (DESIGN_IMPLEMENTATION.md §6) --------------------

        void BuildTagsSection(VisualElement parent)
        {
            var row = Section(parent, "MvTag",
                "Non-interactive pill badge · 6 variants × 2 size rungs. `on-image` is a translucent navy layer under its own label — check it over the photo strip.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.FlexStart;

            foreach (MvTagSize size in System.Enum.GetValues(typeof(MvTagSize)))
            {
                var sizeRow = new VisualElement();
                sizeRow.AddToClassList("gallery-row");
                var caption = new Label(size.ToString());
                caption.AddToClassList("md-typescale-label-small");
                sizeRow.Add(caption);
                foreach (MvTagVariant variant in System.Enum.GetValues(typeof(MvTagVariant)))
                    sizeRow.Add(new MvTag { Text = variant.ToString(), Variant = variant, Size = size });
                row.Add(sizeRow);
            }

            // The four real V2 tags, at the rung each one snaps to.
            var realRow = new VisualElement();
            realRow.AddToClassList("gallery-row");
            realRow.Add(new MvTag { Text = "Risco: Baixo", Variant = MvTagVariant.Success });
            realRow.Add(new MvTag { Text = "Pendente", Variant = MvTagVariant.Neutral, Size = MvTagSize.Small });
            realRow.Add(new MvTag { Text = "Área de berçário", Variant = MvTagVariant.OnImage, Size = MvTagSize.Small });
            realRow.Add(new MvTag { Text = "Ambiente recifal", Variant = MvTagVariant.OnImage });
            row.Add(realRow);

            // on-image over an actual photo — the only way to see the 0.8 fill work.
            var plate = new VisualElement();
            plate.style.height = 88;
            plate.style.width = 320;
            plate.style.flexDirection = FlexDirection.Row;
            plate.style.alignItems = Align.Center;
            plate.style.paddingLeft = 12;
            plate.style.borderTopLeftRadius = 16;
            plate.style.borderTopRightRadius = 16;
            plate.style.borderBottomLeftRadius = 16;
            plate.style.borderBottomRightRadius = 16;
            plate.style.overflow = Overflow.Hidden;
            var plateImage = new Image { scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
            plateImage.image = DemoPhoto(new Color(0.09f, 0.42f, 0.55f), new Color(0.85f, 0.88f, 0.62f));
            plateImage.style.position = Position.Absolute;
            plateImage.style.left = 0;
            plateImage.style.top = 0;
            plateImage.style.right = 0;
            plateImage.style.bottom = 0;
            plate.Add(plateImage);
            var onImage = new MvTag { Text = "Mar de fora", Variant = MvTagVariant.OnImage };
            var onImage2 = new MvTag { Text = "Área do parque", Variant = MvTagVariant.OnImage };
            onImage2.style.marginLeft = 8;
            plate.Add(onImage);
            plate.Add(onImage2);
            row.Add(plate);
        }

        void BuildAlertBarSection(VisualElement parent)
        {
            var row = Section(parent, "MvAlertBar",
                "Icon + wrapping text on a tinted outlined bar · warning (V2's lifeguard notice) / info / success / error · icon optional.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.Stretch;

            row.Add(new MvAlertBar { Text = "Salva-vidas: Das 08h às 17h", Icon = "medical_services" });
            row.Add(new MvAlertBar { Severity = MvAlertBarSeverity.Info, Icon = "info", Text = "Maré vazante até as 14h." });
            row.Add(new MvAlertBar { Severity = MvAlertBarSeverity.Success, Icon = "check_circle", Text = "Avistamento enviado." });
            row.Add(new MvAlertBar { Severity = MvAlertBarSeverity.Error, Icon = "wifi_off", Text = "Sem conexão — o envio ficará na fila." });
            row.Add(new MvAlertBar { Icon = "", Text = "Sem ícone." });
            row.Add(new MvAlertBar
            {
                Text = "Texto longo para provar a quebra de linha: mantenha distância de segurança " +
                       "ao avistar animais marinhos na praia ou durante o nado, e respeite a sinalização.",
            });
        }

        void BuildNumberedListSection(VisualElement parent)
        {
            var row = Section(parent, "MvNumberedList",
                "Numbered primary circles + body rows ('Dicas de convivência') · items from code · start-number · empty list collapses.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.Stretch;

            var card = new MdCard { Variant = MdCardVariant.Outlined };
            var tips = new MvNumberedList();
            tips.SetItems(new[]
            {
                "Mantenha distância de segurança ao avistar animais marinhos na praia ou durante o nado.",
                "Evite movimentos bruscos e não tente tocar nos tubarões ou tartarugas em alimentação.",
                "Respeite as sinalizações de conservação e não descarte lixo de nenhuma espécie na praia.",
            });
            card.Add(tips);
            row.Add(card);

            var continued = new MvNumberedList { StartNumber = 8 };
            continued.SetItems(new[] { "Continua a partir de 8.", "Nono item." });
            continued.style.marginTop = 12;
            row.Add(continued);

            var empty = new MvNumberedList();
            empty.SetItems(null);
            row.Add(empty);
            var emptyNote = new Label("(an empty MvNumberedList is above this line and contributes no height)");
            emptyNote.AddToClassList("md-typescale-body-small");
            row.Add(emptyNote);
        }

        void BuildHeroHeaderSection(VisualElement parent)
        {
            var row = Section(parent, "MvHeroHeader",
                "Cover photo + scrim + optional back button / selector pill / badge row. Every overlay is independent; no image = no scrim (the AR case).");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.Stretch;

            var status = new Label("—");
            status.AddToClassList("md-typescale-label-medium");

            // 1. Praia detalhe: everything on.
            var full = new MvHeroHeader
            {
                ShowBackButton = true,
                ShowSelector = true,
                SelectorText = "Baía do Sueste",
            };
            full.SetImage(DemoPhoto(new Color(0.05f, 0.35f, 0.52f), new Color(0.62f, 0.85f, 0.83f)));
            full.SetBadges(new[] { "Mar de fora", "Área do parque" });
            full.BackClicked += () => status.text = "BackClicked";
            full.SelectorClicked += () => status.text = "SelectorClicked";
            row.Add(full);

            var fullCaption = new Label("340dp · image + scrim 0.35 + back + selector + 2 badges");
            fullCaption.AddToClassList("md-typescale-body-small");
            row.Add(fullCaption);
            row.Add(status);

            // 2. Praias landing: compact, badges only.
            var compact = new MvHeroHeader { Compact = true };
            compact.SetImage(DemoPhoto(new Color(0.10f, 0.45f, 0.60f), new Color(0.95f, 0.90f, 0.70f)));
            compact.SetBadges(new[] { "Ambiente recifal", "Área de berçário" });
            compact.style.marginTop = 12;
            row.Add(compact);
            var compactCaption = new Label("240dp (--compact) · image + scrim 0.25 + badges, no controls");
            compactCaption.AddToClassList("md-typescale-body-small");
            row.Add(compactCaption);

            // 3. AR: controls over a live camera — no image, so no scrim.
            var arPlate = new VisualElement();
            arPlate.style.marginTop = 12;
            arPlate.style.backgroundColor = new Color(0.03f, 0.10f, 0.16f);
            var overlay = new MvHeroHeader
            {
                Compact = true,
                ShowBackButton = true,
                ShowSelector = true,
                SelectorText = "Praia do Sancho",
            };
            overlay.TopInset = 24;
            arPlate.Add(overlay);
            row.Add(arPlate);
            var arCaption = new Label("no image -> no scrim · TopInset 24 pushes the controls down without moving the photo");
            arCaption.AddToClassList("md-typescale-body-small");
            row.Add(arCaption);

            // 4. Selector alone, and back alone — each overlay is independent.
            var selectorOnly = new MvHeroHeader { Compact = true, ShowSelector = true, SelectorText = "Só o seletor" };
            selectorOnly.SetImage(DemoPhoto(new Color(0.20f, 0.30f, 0.45f), new Color(0.75f, 0.80f, 0.85f)));
            selectorOnly.style.marginTop = 12;
            row.Add(selectorOnly);

            var backOnly = new MvHeroHeader { Compact = true, ShowBackButton = true };
            backOnly.SetImage(DemoPhoto(new Color(0.35f, 0.25f, 0.20f), new Color(0.90f, 0.82f, 0.70f)));
            backOnly.style.marginTop = 12;
            row.Add(backOnly);
            var soloCaption = new Label("selector without back · back without selector · neither would hide the whole top row");
            soloCaption.AddToClassList("md-typescale-body-small");
            row.Add(soloCaption);
        }

        void BuildMediaCarouselSection(VisualElement parent)
        {
            var row = Section(parent, "MvMediaCarousel",
                "Horizontal 160x171 photo cards ('Galeria de avistamentos') · scrollers hidden in C# (USS cannot) · tap raises ItemClicked.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.Stretch;

            string[] species = { "Tubarão-limão", "Tartaruga-verde", "Raia Pintada", "Tubarão-tigre", "Barracuda", "Arraia-jamanta" };
            string[] credits = { "Foto: Bianca Rangel", "Foto: Marcos Lima", "Foto: Ana Clara", "Foto: Pedro Sá", "", "Foto: Lu Menezes" };
            var items = new System.Collections.Generic.List<MvMediaItem>();
            for (int i = 0; i < species.Length; i++)
            {
                float t = i / (float)(species.Length - 1);
                items.Add(new MvMediaItem(species[i], credits[i],
                    DemoPhoto(Color.Lerp(new Color(0.05f, 0.30f, 0.50f), new Color(0.10f, 0.55f, 0.45f), t),
                              Color.Lerp(new Color(0.75f, 0.90f, 0.85f), new Color(0.95f, 0.88f, 0.60f), t))));
            }

            var status = new Label("Tap a card…");
            status.AddToClassList("md-typescale-label-medium");

            var carousel = new MvMediaCarousel();
            carousel.SetItems(items);
            carousel.ItemClicked += i => status.text = $"ItemClicked({i}) = {species[i]}";
            row.Add(carousel);
            row.Add(status);

            var oneItem = new MvMediaCarousel();
            oneItem.SetItems(new[] { new MvMediaItem("Um só", "Foto: —", items[0].Texture) });
            oneItem.style.marginTop = 12;
            row.Add(oneItem);

            var empty = new MvMediaCarousel();
            empty.SetItems(null);
            row.Add(empty);
            var emptyNote = new Label("(an empty MvMediaCarousel is above this line — it hides itself and takes no height)");
            emptyNote.AddToClassList("md-typescale-body-small");
            row.Add(emptyNote);
        }

        /// <summary>
        /// A throwaway 2-stop gradient texture, so the photo-bearing components
        /// (MvHeroHeader, MvMediaCarousel, the on-image MvTag plate) can be seen
        /// doing their job without shipping sample art in the design system.
        /// Destroyed with the gallery.
        /// </summary>
        Texture2D DemoPhoto(Color top, Color bottom)
        {
            const int size = 64;
            var texture = new Texture2D(size, size) { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
            {
                var line = Color.Lerp(bottom, top, y / (float)(size - 1));
                for (int x = 0; x < size; x++)
                {
                    // A little cross-fade so ScaleAndCrop has something to crop.
                    texture.SetPixel(x, y, Color.Lerp(line, top, x / (float)(size - 1) * 0.25f));
                }
            }
            texture.Apply();
            _demoTextures.Add(texture);
            return texture;
        }

        void DestroyDemoTextures()
        {
            foreach (var texture in _demoTextures)
            {
                if (texture == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(texture);
                else
                    DestroyImmediate(texture);
            }
            _demoTextures.Clear();
        }

        void BuildIconsSection(VisualElement parent)
        {
            var row = Section(parent, "MdIcon", $"The shipped Material Symbols subset ({MdIconGlyphs.Map.Count} icons).");
            foreach (var name in MdIconGlyphs.Map.Keys.OrderBy(n => n))
            {
                var cell = new VisualElement();
                cell.AddToClassList("gallery-icon-cell");
                cell.Add(new MdIcon { Icon = name });
                var label = new Label(name);
                label.AddToClassList("gallery-icon-cell__name");
                cell.Add(label);
                row.Add(cell);
            }
        }
    }

    /// <summary>
    /// Shows a color token's swatch + resolved hex. The color comes from a USS
    /// rule (.gallery-swatch__color--&lt;role&gt; in Gallery.uss) because C#'s
    /// customStyle.TryGetValue cannot see inherited :root variables — USS
    /// resolves the var(), C# reads back resolvedStyle.backgroundColor.
    /// </summary>
    sealed class TokenSwatch : VisualElement
    {
        readonly VisualElement _color;
        readonly Label _hex;

        public TokenSwatch(string role)
        {
            AddToClassList("gallery-swatch");
            _color = new VisualElement();
            _color.AddToClassList("gallery-swatch__color");
            _color.AddToClassList($"gallery-swatch__color--{role}");
            var name = new Label(role);
            name.AddToClassList("gallery-swatch__name");
            _hex = new Label("…");
            _hex.AddToClassList("gallery-swatch__name");
            Add(_color);
            Add(name);
            Add(_hex);

            _color.RegisterCallback<GeometryChangedEvent>(_ => UpdateHex());
        }

        /// <summary>Re-read the hex after a theme change (style resolution is async — wait a beat).</summary>
        public void Refresh() => _color.schedule.Execute(UpdateHex).StartingIn(100);

        void UpdateHex() =>
            _hex.text = "#" + ColorUtility.ToHtmlStringRGB(_color.resolvedStyle.backgroundColor);
    }
}
