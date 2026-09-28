using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// M3 linear progress indicator (determinate + indeterminate). The root is
    /// the 4dp track (stretches to the parent's width); __indicator is the
    /// filled bar. Determinate: set <see cref="Value"/> (0..1, clamped).
    /// Indeterminate: a scheduler-driven sweep animation (USS transitions
    /// can't loop), running only while attached to a panel.
    /// </summary>
    [UxmlElement]
    public partial class MdLinearProgress : VisualElement
    {
        public const string UssClassName = "md-linear-progress";
        public const string IndicatorClassName = "md-linear-progress__indicator";
        public const string IndeterminateClassName = "md-linear-progress--indeterminate";

        const long AnimIntervalMs = 16;
        const float CycleSeconds = 1.8f;

        readonly VisualElement _indicator;
        IVisualElementScheduledItem _anim;
        float _value;
        float _phase;
        bool _indeterminate;

        /// <summary>Progress in 0..1; clamped. Ignored while indeterminate.</summary>
        [UxmlAttribute("value")]
        public float Value
        {
            get => _value;
            set
            {
                _value = Mathf.Clamp01(value);
                if (!_indeterminate)
                    ApplyDeterminate();
            }
        }

        [UxmlAttribute("indeterminate")]
        public bool Indeterminate
        {
            get => _indeterminate;
            set
            {
                if (_indeterminate == value)
                    return;
                _indeterminate = value;
                EnableInClassList(IndeterminateClassName, _indeterminate);
                if (_indeterminate)
                {
                    StartAnim();
                }
                else
                {
                    StopAnim();
                    ApplyDeterminate();
                }
            }
        }

        public MdLinearProgress()
        {
            AddToClassList(UssClassName);
            pickingMode = PickingMode.Ignore;

            _indicator = new VisualElement { name = "indicator", pickingMode = PickingMode.Ignore };
            _indicator.AddToClassList(IndicatorClassName);
            Add(_indicator);
            ApplyDeterminate();

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (_indeterminate)
                    StartAnim();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => StopAnim());
        }

        void StartAnim()
        {
            if (_anim == null)
                _anim = schedule.Execute(OnAnimTick).Every(AnimIntervalMs);
            else
                _anim.Resume();
        }

        void StopAnim() => _anim?.Pause();

        void OnAnimTick(TimerState ts)
        {
            _phase = (_phase + ts.deltaTime / 1000f / CycleSeconds) % 1f;
            // Head races ahead, tail follows — the classic grow-then-shrink sweep.
            float head = Mathf.Clamp01(_phase * 1.6f);
            float tail = Mathf.Clamp01(_phase * 1.6f - 0.6f);
            _indicator.style.left = Length.Percent(tail * 100f);
            _indicator.style.width = Length.Percent((head - tail) * 100f);
        }

        void ApplyDeterminate()
        {
            _indicator.style.left = Length.Percent(0f);
            _indicator.style.width = Length.Percent(_value * 100f);
        }
    }

    /// <summary>
    /// M3 circular progress indicator (determinate + indeterminate). 48dp with
    /// a 4dp stroke, drawn via Painter2D: a full-circle __track arc under an
    /// __indicator arc, each stroked with its own USS-resolved `color` so both
    /// stay token-driven. Indeterminate: rotating arc with a breathing sweep,
    /// scheduler-driven while attached.
    /// </summary>
    [UxmlElement]
    public partial class MdCircularProgress : VisualElement
    {
        public const string UssClassName = "md-circular-progress";
        public const string TrackClassName = "md-circular-progress__track";
        public const string IndicatorClassName = "md-circular-progress__indicator";
        public const string IndeterminateClassName = "md-circular-progress--indeterminate";

        const long AnimIntervalMs = 16;
        const float CycleSeconds = 1.5f;
        const float StartAngleDeg = -90f; // 12 o'clock

        readonly Arc _track;
        readonly Arc _indicator;
        IVisualElementScheduledItem _anim;
        float _value;
        float _phase;
        bool _indeterminate;

        /// <summary>Progress in 0..1; clamped. Ignored while indeterminate.</summary>
        [UxmlAttribute("value")]
        public float Value
        {
            get => _value;
            set
            {
                _value = Mathf.Clamp01(value);
                if (!_indeterminate)
                    ApplyDeterminate();
            }
        }

        [UxmlAttribute("indeterminate")]
        public bool Indeterminate
        {
            get => _indeterminate;
            set
            {
                if (_indeterminate == value)
                    return;
                _indeterminate = value;
                EnableInClassList(IndeterminateClassName, _indeterminate);
                if (_indeterminate)
                {
                    StartAnim();
                }
                else
                {
                    StopAnim();
                    ApplyDeterminate();
                }
            }
        }

        public MdCircularProgress()
        {
            AddToClassList(UssClassName);
            pickingMode = PickingMode.Ignore;

            _track = new Arc { name = "track", StartAngle = 0f, SweepAngle = 360f };
            _track.AddToClassList(TrackClassName);

            _indicator = new Arc { name = "indicator" };
            _indicator.AddToClassList(IndicatorClassName);

            Add(_track);
            Add(_indicator);
            ApplyDeterminate();

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (_indeterminate)
                    StartAnim();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => StopAnim());
        }

        void StartAnim()
        {
            if (_anim == null)
                _anim = schedule.Execute(OnAnimTick).Every(AnimIntervalMs);
            else
                _anim.Resume();
        }

        void StopAnim() => _anim?.Pause();

        void OnAnimTick(TimerState ts)
        {
            _phase = (_phase + ts.deltaTime / 1000f / CycleSeconds) % 1f;
            // Rotate while the sweep breathes between short and long.
            _indicator.StartAngle = StartAngleDeg + _phase * 720f;
            _indicator.SweepAngle = Mathf.Lerp(30f, 270f, 0.5f * (1f - Mathf.Cos(_phase * 2f * Mathf.PI)));
            _indicator.MarkDirtyRepaint();
        }

        void ApplyDeterminate()
        {
            _indicator.StartAngle = StartAngleDeg;
            _indicator.SweepAngle = _value * 360f;
            _indicator.MarkDirtyRepaint();
        }

        /// <summary>An absolutely-positioned arc stroked with the element's own
        /// USS-resolved `color` (so track/indicator colors stay in USS).</summary>
        internal sealed class Arc : VisualElement
        {
            public const float StrokeWidth = 4f;

            public float StartAngle;
            public float SweepAngle;

            public Arc()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += OnGenerateVisualContent;
            }

            void OnGenerateVisualContent(MeshGenerationContext ctx)
            {
                if (SweepAngle <= 0f)
                    return;
                var rect = contentRect;
                float radius = Mathf.Min(rect.width, rect.height) * 0.5f - StrokeWidth * 0.5f;
                if (radius <= 0f)
                    return;
                var painter = ctx.painter2D;
                painter.lineWidth = StrokeWidth;
                painter.lineCap = LineCap.Round;
                painter.strokeColor = resolvedStyle.color;
                painter.BeginPath();
                painter.Arc(rect.center, radius, StartAngle, StartAngle + SweepAngle);
                painter.Stroke();
            }
        }
    }
}
