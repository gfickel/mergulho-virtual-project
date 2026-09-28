using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// A Material Symbols icon. Set <see cref="Icon"/> to a name from the
    /// shipped subset (tools/design_system/material_symbols_icons.txt);
    /// unknown names render nothing and warn in the editor.
    /// Size/color follow font-size/color, so parents style it like text.
    /// </summary>
    [UxmlElement]
    public partial class MdIcon : TextElement
    {
        public const string UssClassName = "md-icon";

        string _icon = "";

        [UxmlAttribute("icon")]
        public string Icon
        {
            get => _icon;
            set
            {
                _icon = value ?? "";
                if (_icon.Length == 0)
                {
                    text = "";
                }
                else if (MdIconGlyphs.Map.TryGetValue(_icon, out var glyph))
                {
                    text = glyph;
                }
                else
                {
                    text = "";
#if UNITY_EDITOR
                    UnityEngine.Debug.LogWarning(
                        $"MdIcon: unknown icon '{_icon}'. Add it to " +
                        "tools/design_system/material_symbols_icons.txt and rerun the subset script.");
#endif
                }
            }
        }

        public MdIcon()
        {
            AddToClassList(UssClassName);
            pickingMode = PickingMode.Ignore;
        }
    }
}
