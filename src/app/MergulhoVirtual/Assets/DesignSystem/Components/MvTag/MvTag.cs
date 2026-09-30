using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>Semantic color of an <see cref="MvTag"/>.</summary>
    public enum MvTagVariant
    {
        /// <summary>Grey pill for a non-semantic state ("Pendente").</summary>
        Neutral,
        /// <summary>Green pill for a good/safe state ("Risco: Baixo").</summary>
        Success,
        /// <summary>Amber pill for a caution state.</summary>
        Warning,
        /// <summary>Red pill for a bad state.</summary>
        Error,
        /// <summary>Navy pill on a light surface.</summary>
        Primary,
        /// <summary>Translucent navy pill for use ON a photo (hero badges,
        /// the "Área de berçário" badge over the species image).</summary>
        OnImage,
    }

    /// <summary>Padding/type rung of an <see cref="MvTag"/>.</summary>
    public enum MvTagSize
    {
        /// <summary>4/10 padding, 11px label — V2's "Pendente" chip.</summary>
        Small,
        /// <summary>6/12 padding, 12px label — V2's hero badges and risk pill.</summary>
        Medium,
    }

    /// <summary>
    /// A small, non-interactive pill badge: risk level, environment tags,
    /// "Área de berçário", "Pendente".
    /// <para>
    /// <b>Deliberately not an MdChip.</b> Chips are interactive by contract
    /// (assist chips raise <c>Clicked</c>, filter chips toggle and grow a
    /// leading check, both carry a state layer and a 48dp touch target). A tag
    /// is a read-only label: no state layer, no focus, no touch target, picking
    /// ignored all the way down. Making it a chip variant would mean a chip
    /// that silently disables half of its own contract.
    /// </para>
    /// <para>
    /// The fill lives on an absolutely-positioned <c>__surface</c> child rather
    /// than on the root, so <see cref="MvTagVariant.OnImage"/> can be a
    /// translucent navy WITHOUT fading its own text: <c>opacity</c> applies to
    /// an element and all its descendants, and a literal <c>rgba()</c> fill is
    /// banned by TokenDisciplineTests. One token + one opacity on a layer under
    /// the label is the only way to get both.
    /// </para>
    /// <para>
    /// <b>The corner radius is set from code, not USS.</b> A tag is the one pill
    /// in the library with no authored height — it is padding plus the label
    /// face's line box — and a stadium requires a radius of exactly half the
    /// height, which USS cannot compute. See <see cref="MdShape.KeepStadium"/>
    /// and <c>Tokens/_shape.uss</c>; <c>corner-full</c> would draw a lens here.
    /// </para>
    /// </summary>
    [UxmlElement]
    public partial class MvTag : VisualElement
    {
        public const string UssClassName = "mv-tag";
        public const string SurfaceClassName = "mv-tag__surface";
        public const string LabelClassName = "mv-tag__label";

        static readonly string[] VariantClassNames =
        {
            "mv-tag--neutral",
            "mv-tag--success",
            "mv-tag--warning",
            "mv-tag--error",
            "mv-tag--primary",
            "mv-tag--on-image",
        };

        static readonly string[] SizeClassNames =
        {
            "mv-tag--small",
            "mv-tag--medium",
        };

        readonly Label _label;
        MvTagVariant _variant = MvTagVariant.Neutral;
        MvTagSize _size = MvTagSize.Medium;

        [UxmlAttribute("text")]
        public string Text
        {
            get => _label.text;
            set => _label.text = value ?? "";
        }

        [UxmlAttribute("variant")]
        public MvTagVariant Variant
        {
            get => _variant;
            set
            {
                RemoveFromClassList(VariantClassNames[(int)_variant]);
                _variant = value;
                AddToClassList(VariantClassNames[(int)_variant]);
            }
        }

        [UxmlAttribute("size")]
        public MvTagSize Size
        {
            get => _size;
            set
            {
                RemoveFromClassList(SizeClassNames[(int)_size]);
                _size = value;
                AddToClassList(SizeClassNames[(int)_size]);
            }
        }

        public MvTag()
        {
            AddToClassList(UssClassName);
            AddToClassList(VariantClassNames[(int)_variant]);
            AddToClassList(SizeClassNames[(int)_size]);
            pickingMode = PickingMode.Ignore;

            var surface = new VisualElement { name = "surface", pickingMode = PickingMode.Ignore };
            surface.AddToClassList(SurfaceClassName);

            _label = new Label { name = "label", pickingMode = PickingMode.Ignore };
            _label.AddToClassList(LabelClassName);

            Add(surface);
            Add(_label);

            // Height is content-driven, so the stadium radius has to follow it.
            MdShape.KeepStadium(this);
        }
    }
}
