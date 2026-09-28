using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    public enum MdCardVariant
    {
        Elevated,
        Filled,
        Outlined,
    }

    /// <summary>
    /// M3 card (elevated / filled / outlined). Container only — content is
    /// slotted children; the card imposes shape, surface color, and padding.
    /// UI Toolkit has no box-shadow, so "elevated" uses tone-based elevation
    /// (surface-container colors), the same strategy M3 dark theme uses.
    /// Not interactive by itself — put an MdButton/etc. inside, or wrap it.
    /// </summary>
    [UxmlElement]
    public partial class MdCard : VisualElement
    {
        public const string UssClassName = "md-card";

        static readonly string[] VariantClassNames =
        {
            "md-card--elevated",
            "md-card--filled",
            "md-card--outlined",
        };

        MdCardVariant _variant;

        [UxmlAttribute("variant")]
        public MdCardVariant Variant
        {
            get => _variant;
            set
            {
                RemoveFromClassList(VariantClassNames[(int)_variant]);
                _variant = value;
                AddToClassList(VariantClassNames[(int)_variant]);
            }
        }

        public MdCard()
        {
            AddToClassList(UssClassName);
            AddToClassList(VariantClassNames[(int)_variant]);
        }
    }
}
