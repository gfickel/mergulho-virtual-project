using System;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    public enum MdTextFieldVariant
    {
        Filled,
        Outlined,
    }

    /// <summary>
    /// M3 text field (filled / outlined) with floating label, supporting text,
    /// and error state. Wraps a Unity <see cref="TextField"/> for the actual
    /// editing (incl. the mobile touchscreen keyboard) and restyles it via USS.
    /// The label floats while the field is focused or non-empty
    /// (--floating class); focus is tracked with a --focused class in C#
    /// because USS has no :focus-within and focus lands on the inner input.
    /// Setting a non-empty <see cref="ErrorText"/> enters the error state and
    /// replaces the supporting text until cleared.
    /// </summary>
    [UxmlElement]
    public partial class MdTextField : VisualElement
    {
        public const string UssClassName = "md-text-field";
        public const string ContainerClassName = "md-text-field__container";
        public const string LabelClassName = "md-text-field__label";
        public const string InputClassName = "md-text-field__input";
        public const string SupportingClassName = "md-text-field__supporting";
        public const string FloatingClassName = "md-text-field--floating";
        public const string FocusedClassName = "md-text-field--focused";
        public const string ErrorClassName = "md-text-field--error";

        static readonly string[] VariantClassNames =
        {
            "md-text-field--filled",
            "md-text-field--outlined",
        };

        readonly VisualElement _container;
        readonly Label _label;
        readonly TextField _input;
        readonly Label _supporting;
        MdTextFieldVariant _variant;
        string _supportingText = "";
        string _errorText = "";

        /// <summary>Raised whenever the value changes — typing or programmatic
        /// <see cref="Value"/> set (same-value sets are ignored).</summary>
        public event Action<string> ValueChanged;

        [UxmlAttribute("variant")]
        public MdTextFieldVariant Variant
        {
            get => _variant;
            set
            {
                RemoveFromClassList(VariantClassNames[(int)_variant]);
                _variant = value;
                AddToClassList(VariantClassNames[(int)_variant]);
            }
        }

        [UxmlAttribute("label")]
        public string LabelText
        {
            get => _label.text;
            set => _label.text = value ?? "";
        }

        [UxmlAttribute("value")]
        public string Value
        {
            get => _input.value;
            set
            {
                string next = value ?? "";
                if (_input.value == next)
                    return;
                _input.SetValueWithoutNotify(next);
                UpdateFloating();
                ValueChanged?.Invoke(next);
            }
        }

        [UxmlAttribute("supporting-text")]
        public string SupportingText
        {
            get => _supportingText;
            set
            {
                _supportingText = value ?? "";
                UpdateSupporting();
            }
        }

        /// <summary>Non-empty = error state (colors + shown as supporting text).</summary>
        [UxmlAttribute("error-text")]
        public string ErrorText
        {
            get => _errorText;
            set
            {
                _errorText = value ?? "";
                EnableInClassList(ErrorClassName, _errorText.Length > 0);
                UpdateSupporting();
            }
        }

        public bool IsError => _errorText.Length > 0;

        public MdTextField()
        {
            AddToClassList(UssClassName);
            AddToClassList(VariantClassNames[(int)_variant]);

            _container = new VisualElement { name = "container" };
            _container.AddToClassList(ContainerClassName);

            _label = new Label { name = "label", pickingMode = PickingMode.Ignore };
            _label.AddToClassList(LabelClassName);

            _input = new TextField { name = "input" };
            _input.AddToClassList(InputClassName);
            _input.RegisterValueChangedCallback(evt =>
            {
                UpdateFloating();
                ValueChanged?.Invoke(evt.newValue);
            });

            _supporting = new Label { name = "supporting", pickingMode = PickingMode.Ignore };
            _supporting.AddToClassList(SupportingClassName);
            _supporting.style.display = DisplayStyle.None;

            _container.Add(_input);
            _container.Add(_label);
            Add(_container);
            Add(_supporting);

            // Clicking anywhere on the box (incl. the label overlay area)
            // should start editing, like a native M3 field.
            _container.RegisterCallback<ClickEvent>(_ => _input.Focus());

            // FocusIn/OutEvent bubble up from the inner input.
            RegisterCallback<FocusInEvent>(_ =>
            {
                AddToClassList(FocusedClassName);
                UpdateFloating();
            });
            RegisterCallback<FocusOutEvent>(_ =>
            {
                RemoveFromClassList(FocusedClassName);
                UpdateFloating();
            });
        }

        void UpdateFloating()
        {
            bool floating = _input.value.Length > 0 || ClassListContains(FocusedClassName);
            EnableInClassList(FloatingClassName, floating);
        }

        void UpdateSupporting()
        {
            string text = IsError ? _errorText : _supportingText;
            _supporting.text = text;
            _supporting.style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
