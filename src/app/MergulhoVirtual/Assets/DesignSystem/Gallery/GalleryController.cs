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
        bool _dark;

        void OnEnable()
        {
            _originalTheme = document.panelSettings.themeStyleSheet;
            var root = document.rootVisualElement;
            root.Clear();
            _swatches.Clear();
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
            BuildIconsSection(scroll);
        }

        void OnDestroy()
        {
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
            var row = Section(parent, "Typography", "M3 type scale (Roboto).");
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

                var title = new Label(variant.ToString());
                title.AddToClassList("md-typescale-title-medium");
                card.Add(title);

                var body = new Label("Tubarão-limão avistado na Praia do Sancho.");
                body.AddToClassList("md-typescale-body-medium");
                body.style.whiteSpace = WhiteSpace.Normal;
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
            var row = Section(parent, "MdNavigationBar", "The app's future BottomNav — tap a destination to move the active indicator.");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.Stretch;

            var status = new Label("Destino: AR");
            status.AddToClassList("md-typescale-label-medium");

            var destinations = new[]
            {
                new MdNavDestination("view_in_ar", "AR"),
                new MdNavDestination("beach_access", "Praias"),
                new MdNavDestination("scuba_diving", "Animais"),
                new MdNavDestination("add_a_photo", "Registrar"),
                new MdNavDestination("info", "Sobre"),
            };
            var bar = new MdNavigationBar();
            bar.SetDestinations(destinations);
            bar.SelectedIndex = 0;
            bar.SelectionChanged += i =>
                status.text = "Destino: " + (i >= 0 ? destinations[i].Label : "—");
            bar.style.width = 380;
            bar.style.marginBottom = 12;

            row.Add(bar);
            row.Add(status);
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
