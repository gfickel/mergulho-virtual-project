using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MergulhoVirtual.DesignSystem
{
    /// <summary>
    /// One image in an <see cref="MvMediaPicker"/>. Carries either a
    /// <see cref="Sprite"/> or a <see cref="Texture2D"/> — a photo just decoded
    /// from the gallery arrives as a texture, a bundled placeholder as a sprite —
    /// and whichever is non-null is shown. Same contract as
    /// <see cref="MvMediaItem"/>; kept separate because a picker tile has no
    /// title and no caption, and a shared type would carry two fields that can
    /// never render.
    /// </summary>
    public sealed class MvMediaPickerItem
    {
        public readonly Sprite Sprite;
        public readonly Texture2D Texture;

        public MvMediaPickerItem(Sprite sprite) => Sprite = sprite;
        public MvMediaPickerItem(Texture2D texture) => Texture = texture;
    }

    /// <summary>
    /// The "Adicionar foto ou vídeo" control (§8.5): a dashed call-to-action box
    /// while empty, and a card of square thumbnails — each with a remove button,
    /// plus an "add more" tile — once it holds something.
    ///
    /// <para>
    /// <b>It never touches the gallery or the filesystem.</b> The component
    /// raises <see cref="AddRequested"/> and <see cref="RemoveRequested"/>; the
    /// screen answers them (via <c>Assets/Scripts/Photo/GalleryPicker.cs</c> and
    /// its ViewModel's list) and calls <see cref="SetItems"/> with the new
    /// truth. It does not even mutate its own list on a remove tap, for the same
    /// reason <see cref="MvOptionCard"/> does not toggle itself: the ViewModel
    /// owns the list, and a component that edits a copy of it becomes a second
    /// authority that can disagree.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="MaxItems"/> is a first-class cap, not a screen-side guard.</b>
    /// 0 (the default) means unlimited; any positive number hides the "add more"
    /// tile once the grid reaches it, so the affordance to exceed the cap is
    /// simply absent. Slice 3 ships with <c>MaxItems = 1</c> (Decision D6 —
    /// single photo now, multi-photo as its own slice): the user then sees the
    /// dashed box, taps once, and gets a card holding one thumbnail with its
    /// remove button and no add tile. Replacing the photo is remove-then-add.
    /// Handing the picker MORE items than the cap renders all of them and warns
    /// in the editor rather than silently dropping data the caller owns.
    /// </para>
    /// </summary>
    [UxmlElement]
    public partial class MvMediaPicker : VisualElement
    {
        public const string UssClassName = "mv-media-picker";
        public const string EmptyClassName = "mv-media-picker__empty";
        public const string EmptyIconClassName = "mv-media-picker__empty-icon";
        public const string EmptyTitleClassName = "mv-media-picker__empty-title";
        public const string HintClassName = "mv-media-picker__hint";
        public const string CardClassName = "mv-media-picker__card";
        public const string GridClassName = "mv-media-picker__grid";
        public const string TileClassName = "mv-media-picker__tile";
        public const string ThumbClassName = "mv-media-picker__thumb";
        public const string RemoveClassName = "mv-media-picker__remove";
        public const string RemoveFillClassName = "mv-media-picker__remove-fill";
        public const string RemoveIconClassName = "mv-media-picker__remove-icon";
        public const string AddClassName = "mv-media-picker__add";
        public const string AddIconClassName = "mv-media-picker__add-icon";
        public const string DashClassName = "mv-media-picker__dash";

        const string DefaultEmptyTitle = "Selecione arquivos do dispositivo";
        const string DefaultHint = "Limite de tamanho: 20MB";

        // Figma strokeDashes: [6, 6] on `Dashed Upload Box`, [4, 4] on `Add More`.
        const float EmptyDash = 6f;
        const float AddDash = 4f;

        readonly List<MvMediaPickerItem> _items = new();
        readonly VisualElement _empty;
        readonly Label _emptyTitle;
        readonly Label _emptyHint;
        readonly VisualElement _card;
        readonly VisualElement _grid;
        readonly VisualElement _add;
        readonly Label _cardHint;
        int _maxItems;

        /// <summary>The user asked to add media — from the empty box or the
        /// "add more" tile. The screen opens the picker and calls
        /// <see cref="SetItems"/> with the result.</summary>
        public event Action AddRequested;

        /// <summary>The user tapped a tile's remove button; the argument is that
        /// tile's index in the list last given to <see cref="SetItems"/>.</summary>
        public event Action<int> RemoveRequested;

        public int ItemCount => _items.Count;

        /// <summary>True when the grid is full and the "add more" tile is hidden.
        /// Always false while <see cref="MaxItems"/> is 0 (unlimited).</summary>
        public bool AtCapacity => _maxItems > 0 && _items.Count >= _maxItems;

        /// <summary>Maximum number of items the UI offers to collect.
        /// 0 (default) = unlimited. Slice 3 sets 1.</summary>
        [UxmlAttribute("max-items")]
        public int MaxItems
        {
            get => _maxItems;
            set
            {
                int clamped = Mathf.Max(0, value);
                if (_maxItems == clamped)
                    return;
                _maxItems = clamped;
                Refresh();
            }
        }

        /// <summary>Call-to-action line in the empty state.</summary>
        [UxmlAttribute("empty-title")]
        public string EmptyTitle
        {
            get => _emptyTitle.text;
            set => _emptyTitle.text = value ?? "";
        }

        /// <summary>Supporting line shown under the call to action AND under the
        /// grid — V2 keeps the size limit visible in both states.</summary>
        [UxmlAttribute("hint")]
        public string HintText
        {
            get => _cardHint.text;
            set
            {
                string text = value ?? "";
                _cardHint.text = text;
                _emptyHint.text = text;
            }
        }

        public MvMediaPicker()
        {
            AddToClassList(UssClassName);

            // ---- Empty state: one big dashed button ----
            _empty = new VisualElement { name = "empty", focusable = true };
            _empty.AddToClassList(EmptyClassName);

            var emptyState = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            emptyState.AddToClassList("md-state-layer");
            _empty.Add(emptyState);
            _empty.Add(MakeDash(EmptyDash));

            var plus = new MdIcon { name = "plus", Icon = "add" };
            plus.AddToClassList(EmptyIconClassName);
            _empty.Add(plus);

            _emptyTitle = new Label(DefaultEmptyTitle) { name = "empty-title", pickingMode = PickingMode.Ignore };
            _emptyTitle.AddToClassList(EmptyTitleClassName);
            _empty.Add(_emptyTitle);

            _emptyHint = new Label(DefaultHint) { name = "empty-hint", pickingMode = PickingMode.Ignore };
            _emptyHint.AddToClassList(HintClassName);
            _empty.Add(_emptyHint);

            _empty.AddManipulator(new Clickable(() => AddRequested?.Invoke()));
            Add(_empty);

            // ---- Filled state: a card holding the grid + the hint ----
            _card = new VisualElement { name = "card", pickingMode = PickingMode.Ignore };
            _card.AddToClassList(CardClassName);

            _grid = new VisualElement { name = "grid", pickingMode = PickingMode.Ignore };
            _grid.AddToClassList(GridClassName);
            _card.Add(_grid);

            _add = BuildAddTile();
            _grid.Add(_add);

            _cardHint = new Label(DefaultHint) { name = "hint", pickingMode = PickingMode.Ignore };
            _cardHint.AddToClassList(HintClassName);
            _card.Add(_cardHint);
            Add(_card);

            Refresh();
        }

        /// <summary>
        /// Replaces the grid. Null or empty shows the dashed empty state.
        /// The list is copied, so the caller may keep mutating its own.
        /// </summary>
        public void SetItems(IReadOnlyList<MvMediaPickerItem> items)
        {
            _items.Clear();
            int count = items?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                if (items[i] != null)
                    _items.Add(items[i]);
            }
#if UNITY_EDITOR
            if (_maxItems > 0 && _items.Count > _maxItems)
            {
                Debug.LogWarning(
                    $"MvMediaPicker: {_items.Count} items given but MaxItems is {_maxItems}. " +
                    "All of them are rendered (the caller owns the list) and the 'add more' tile " +
                    "stays hidden — enforce the cap where the items are collected.");
            }
#endif
            Refresh();
        }

        void Refresh()
        {
            bool empty = _items.Count == 0;
            _empty.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            _card.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;

            // Rebuild the tiles, keeping the add tile (it is the grid's last child).
            for (int i = _grid.childCount - 2; i >= 0; i--)
                _grid.RemoveAt(i);
            for (int i = 0; i < _items.Count; i++)
                _grid.Insert(i, BuildTile(_items[i], i));

            _add.style.display = AtCapacity ? DisplayStyle.None : DisplayStyle.Flex;
        }

        VisualElement BuildTile(MvMediaPickerItem item, int index)
        {
            var tile = new VisualElement { name = $"tile-{index}", pickingMode = PickingMode.Ignore };
            tile.AddToClassList(TileClassName);

            var thumb = new Image
            {
                name = "thumb",
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.ScaleAndCrop,
            };
            thumb.AddToClassList(ThumbClassName);
            if (item.Texture != null)
                thumb.image = item.Texture;
            else
                thumb.sprite = item.Sprite;
            tile.Add(thumb);

            var remove = new VisualElement { name = "remove", focusable = true };
            remove.AddToClassList(RemoveClassName);

            // The translucent disc is its own layer, not a background-color: the
            // design's #000 @ 25% has no token (scrim carries no alpha), and
            // `opacity` on the button itself would fade the glyph with it. Same
            // fix as MvTag's __surface. It therefore precedes the state layer.
            var fill = new VisualElement { name = "remove-fill", pickingMode = PickingMode.Ignore };
            fill.AddToClassList(RemoveFillClassName);
            remove.Add(fill);

            var removeState = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            removeState.AddToClassList("md-state-layer");
            remove.Add(removeState);

            var removeIcon = new MdIcon { name = "remove-icon", Icon = "close" };
            removeIcon.AddToClassList(RemoveIconClassName);
            remove.Add(removeIcon);

            int captured = index;
            remove.AddManipulator(new Clickable(() => RemoveRequested?.Invoke(captured)));
            tile.Add(remove);
            return tile;
        }

        VisualElement BuildAddTile()
        {
            var add = new VisualElement { name = "add", focusable = true };
            add.AddToClassList(TileClassName);
            add.AddToClassList(AddClassName);

            var stateLayer = new VisualElement { name = "state-layer", pickingMode = PickingMode.Ignore };
            stateLayer.AddToClassList("md-state-layer");
            add.Add(stateLayer);
            add.Add(MakeDash(AddDash));

            var icon = new MdIcon { name = "add-icon", Icon = "add_circle" };
            icon.AddToClassList(AddIconClassName);
            add.Add(icon);

            add.AddManipulator(new Clickable(() => AddRequested?.Invoke()));
            return add;
        }

        static MvDashedOutline MakeDash(float dash)
        {
            var outline = new MvDashedOutline(dash, dash) { name = "dash" };
            outline.AddToClassList(DashClassName);
            MdOverlay.FillParent(outline);
            return outline;
        }
    }

    /// <summary>
    /// A dashed rounded-rectangle outline, painted with Painter2D because
    /// <b>UI Toolkit USS has no <c>border-style</c></b> — width, color and radius
    /// are all it exposes, so a dashed border cannot be declared. This is the
    /// MdSparkline / MdCircularProgress.Arc pattern: an internal, picking-ignored
    /// layer that draws itself in its own USS-resolved <c>color</c> and takes its
    /// corner radius from its own USS-resolved <c>border-radius</c>, so both
    /// remain the designer's to retune and TokenDiscipline still holds.
    /// <para>
    /// Internal to <see cref="MvMediaPicker"/>, which is its only caller (twice:
    /// the empty box at 6/6 and the "add more" tile at 4/4). Promote it to a
    /// public component the day a second component needs a dashed edge — the same
    /// rule §6 applies to MvSpeedDialRow.
    /// </para>
    /// </summary>
    internal sealed class MvDashedOutline : VisualElement
    {
        const int SegmentsPerCorner = 8;
        const float LineWidth = 1f;

        readonly float _dash;
        readonly float _gap;

        public MvDashedOutline(float dash, float gap)
        {
            _dash = Mathf.Max(0.5f, dash);
            _gap = Mathf.Max(0.5f, gap);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Paint;
        }

        void Paint(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width < 2f || rect.height < 2f)
                return;

            // Strokes are centred on the path, so inset by half a line width or
            // the outer half is clipped away by the parent's overflow: hidden.
            float inset = LineWidth * 0.5f;
            float x0 = rect.xMin + inset, y0 = rect.yMin + inset;
            float x1 = rect.xMax - inset, y1 = rect.yMax - inset;
            float radius = Mathf.Clamp(resolvedStyle.borderTopLeftRadius - inset,
                0f, Mathf.Min(x1 - x0, y1 - y0) * 0.5f);

            var path = BuildOutline(x0, y0, x1, y1, radius);
            var painter = ctx.painter2D;
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = LineWidth;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            StrokeDashed(painter, path);
            painter.Stroke();
        }

        /// <summary>
        /// The rounded rectangle as a closed polyline, walked clockwise from the
        /// start of the top edge. Angles use screen convention (y down):
        /// 0 = right, 90 = down, 180 = left, 270 = up.
        /// </summary>
        static List<Vector2> BuildOutline(float x0, float y0, float x1, float y1, float r)
        {
            var pts = new List<Vector2> { new(x0 + r, y0) };
            pts.Add(new Vector2(x1 - r, y0));
            AppendArc(pts, new Vector2(x1 - r, y0 + r), r, 270f, 360f);
            pts.Add(new Vector2(x1, y1 - r));
            AppendArc(pts, new Vector2(x1 - r, y1 - r), r, 0f, 90f);
            pts.Add(new Vector2(x0 + r, y1));
            AppendArc(pts, new Vector2(x0 + r, y1 - r), r, 90f, 180f);
            pts.Add(new Vector2(x0, y0 + r));
            AppendArc(pts, new Vector2(x0 + r, y0 + r), r, 180f, 270f);
            return pts;
        }

        static void AppendArc(List<Vector2> pts, Vector2 center, float r, float from, float to)
        {
            if (r <= 0f)
                return;
            for (int i = 1; i <= SegmentsPerCorner; i++)
            {
                float a = Mathf.Lerp(from, to, i / (float)SegmentsPerCorner) * Mathf.Deg2Rad;
                pts.Add(new Vector2(center.x + Mathf.Cos(a) * r, center.y + Mathf.Sin(a) * r));
            }
        }

        /// <summary>
        /// Walks the polyline emitting alternating on/off runs, so the dash phase
        /// carries across corners instead of restarting on every edge.
        /// </summary>
        void StrokeDashed(Painter2D painter, List<Vector2> pts)
        {
            bool on = true;
            float remaining = _dash;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 a = pts[i - 1], b = pts[i];
                float length = Vector2.Distance(a, b);
                if (length <= Mathf.Epsilon)
                    continue;
                float travelled = 0f;
                while (travelled < length)
                {
                    float step = Mathf.Min(remaining, length - travelled);
                    if (on)
                    {
                        painter.MoveTo(Vector2.Lerp(a, b, travelled / length));
                        painter.LineTo(Vector2.Lerp(a, b, (travelled + step) / length));
                    }
                    travelled += step;
                    remaining -= step;
                    if (remaining <= 0.0001f)
                    {
                        on = !on;
                        remaining = on ? _dash : _gap;
                    }
                }
            }
        }
    }
}
