using System.Collections.Generic;
using UnityEngine;

namespace T2U.Kit
{
    /// <summary>
    /// CSS flexbox (one line, no wrap) for a uGUI container, written by the UI layout importer from ui.json `layout`.
    /// The web page places children by flexbox: when one is hidden or a text grows, its neighbours move. This component
    /// does the same for the children that have a <see cref="UiFlexItem"/>; the others are left alone.
    /// At rest it puts every child exactly where the web measured it (checked by Tools ▸ T2U Kit ▸ Check UI Prefabs).
    /// Supported: direction, justify-content (start/center/end/space-between/-around/-evenly), align-items and
    /// align-self (start/center/end; stretch keeps the measured size), gap, padding, margins, order, flex-grow, a text
    /// that sets its item's width, and a container whose size follows its content (<see cref="fitMain"/>).
    /// A text that wraps (white-space normal) in a column sets its item's width (up to the column) and height.
    /// Not supported: wrapping of items, flex-shrink (a row whose content grows past its container overflows), baseline.
    /// Units are design units (the canvas reference resolution); web axes (y down).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiFlex : MonoBehaviour
    {
        public enum Justify { Start, Center, End, SpaceBetween, SpaceAround, SpaceEvenly }
        public enum Align { Start, Center, End }

        public bool row = true;
        public Justify justify;
        public Align align;
        /// <summary>CSS gap on the main axis.</summary>
        public float gap;
        /// <summary>Padding (+ border) in design units: x top, y right, z bottom, w left.</summary>
        public Vector4 pad;
        /// <summary>The container's size on the main axis follows its content (web: width/height auto; a scrolling list's content).</summary>
        public bool fitMain;

        bool _dirty = true, _laying;
        readonly List<UiFlexItem> _items = new List<UiFlexItem>();
        static readonly List<UiFlex> Tmp = new List<UiFlex>();

        public void SetDirty()
        {
            if (_laying) return;
            _dirty = true;
            // a container that fits its content changes size with it: its own container must follow
            if (fitMain && transform.parent != null && GetComponent<UiFlexItem>() != null) transform.parent.GetComponent<UiFlex>()?.SetDirty();
        }

        void OnEnable() => _dirty = true;
        void OnRectTransformDimensionsChange() => SetDirty();

        void LateUpdate()
        {
            if (!_dirty)
                for (int i = 0; i < transform.childCount; i++)
                {
                    var it = transform.GetChild(i).GetComponent<UiFlexItem>();
                    if (it != null && it.isActiveAndEnabled && it.TextChanged()) { _dirty = true; break; }
                }
            if (_dirty) Layout();
        }

        /// <summary>Lays out every shown container under <paramref name="root"/> now (inner ones first). For tools and tests.</summary>
        public static void LayoutAll(Transform root)
        {
            root.GetComponentsInChildren(true, Tmp);
            // deepest last in the list → first here; hidden containers do nothing (as at runtime: no LateUpdate)
            for (int i = Tmp.Count - 1; i >= 0; i--) if (Tmp[i].isActiveAndEnabled) Tmp[i].Layout();
            Tmp.Clear();
        }

        static Justify ParseJustify(string s)
        {
            switch (s)
            {
                case "center": return Justify.Center;
                case "flex-end": case "end": case "right": return Justify.End;
                case "space-between": return Justify.SpaceBetween;
                case "space-around": return Justify.SpaceAround;
                case "space-evenly": return Justify.SpaceEvenly;
                default: return Justify.Start; // normal, flex-start, start, left, stretch
            }
        }

        public static Align ParseAlign(string s)
        {
            switch (s)
            {
                case "center": return Align.Center;
                case "flex-end": case "end": case "self-end": return Align.End;
                default: return Align.Start; // normal, stretch, flex-start, start, baseline
            }
        }

        public void Set(string direction, string justifyContent, string alignItems)
        {
            row = !direction.StartsWith("column");
            justify = ParseJustify(justifyContent);
            align = ParseAlign(alignItems);
        }

        /// <summary>Places the children now.</summary>
        public void Layout()
        {
            _dirty = false;
            _laying = true;
            try { DoLayout(); }
            finally { _laying = false; }
        }

        void DoLayout()
        {
            _items.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                var it = transform.GetChild(i).GetComponent<UiFlexItem>();
                if (it != null && it.gameObject.activeSelf) _items.Add(it);
            }
            _items.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.dom.CompareTo(b.dom));
            // children that are containers fitting their content first: their size is part of ours
            foreach (var it in _items)
            {
                var f = it.GetComponent<UiFlex>();
                if (f != null && f.fitMain && f._dirty) f.Layout();
            }

            var me = (RectTransform)transform;
            int main = row ? 0 : 1, cross = 1 - main;
            // padding on the main axis: start/end; on the cross axis: start/end (pad = top, right, bottom, left)
            float pMs = row ? pad.w : pad.x, pMe = row ? pad.y : pad.z, pCs = row ? pad.x : pad.w, pCe = row ? pad.z : pad.y;
            int n = _items.Count;
            var size = new float[n]; var crossSize = new float[n];
            float used = 0, grow = 0;
            float innerCross0 = me.rect.size[cross] - pCs - pCe;
            for (int i = 0; i < n; i++)
            {
                var it = _items[i];
                var rt = (RectTransform)it.transform;
                float cur = rt.rect.size[main];
                if (!row && it.wrapText && it.hugText != null && !it.transformed)
                {
                    // a wrapping text in a column: as wide as its text (up to the column), as tall as its lines
                    var ws = it.WrapSize(innerCross0 - MStart(it, cross) - MEnd(it, cross));
                    size[i] = ws.y; crossSize[i] = ws.x;
                    if (Mathf.Abs(rt.rect.width - ws.x) > 0.001f) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ws.x);
                    used += size[i] + MStart(it, main) + MEnd(it, main);
                    continue;
                }
                // a growing item starts from its measured size (its grown size is ours to change); a text item from its text
                size[i] = it.transformed ? it.restSize[main] : it.hugText != null && row ? it.HugWidth() : it.grow > 0 ? it.restSize[main] : cur;
                crossSize[i] = it.transformed ? it.restSize[cross] : rt.rect.size[cross];
                used += size[i] + MStart(it, main) + MEnd(it, main);
                grow += it.grow;
            }
            if (n > 1) used += gap * (n - 1);

            if (fitMain && Mathf.Abs(me.rect.size[main] - (used + pMs + pMe)) > 0.001f)
                me.SetSizeWithCurrentAnchors((RectTransform.Axis)main, used + pMs + pMe);
            float inner = me.rect.size[main] - pMs - pMe, innerCross = me.rect.size[cross] - pCs - pCe;
            float free = inner - used;
            if (free > 0 && grow > 0)
            {
                for (int i = 0; i < n; i++) size[i] += free * _items[i].grow / grow;
                free = 0;
            }
            float lead = 0, between = 0;
            switch (justify)
            {
                case Justify.Center: lead = free / 2; break;
                case Justify.End: lead = free; break;
                case Justify.SpaceBetween: if (n > 1 && free > 0) between = free / (n - 1); break;
                case Justify.SpaceAround: if (free > 0) { between = free / n; lead = between / 2; } else lead = free / 2; break;
                case Justify.SpaceEvenly: if (free > 0) { between = free / (n + 1); lead = between; } else lead = free / 2; break;
            }

            // where the container's top-left corner is, in its own local space (Unity: y up)
            var r = me.rect;
            float pos = pMs + lead;
            for (int i = 0; i < n; i++)
            {
                var it = _items[i];
                var rt = (RectTransform)it.transform;
                pos += MStart(it, main);
                if (!it.transformed && Mathf.Abs(rt.rect.size[main] - size[i]) > 0.001f) rt.SetSizeWithCurrentAnchors((RectTransform.Axis)main, size[i]);
                var al = string.IsNullOrEmpty(it.alignSelf) || it.alignSelf == "auto" ? align : ParseAlign(it.alignSelf);
                float cs = MStart(it, cross), ce = MEnd(it, cross);
                float c = al == Align.Center ? (innerCross - crossSize[i] - cs - ce) / 2 + cs : al == Align.End ? innerCross - crossSize[i] - ce : cs;
                c += pCs;
                // desired top-left of the item in web axes from the container's top-left
                float wx = row ? pos : c, wy = row ? c : pos;
                var ir = rt.rect;
                if (it.transformed)
                {
                    // the room is placed by flexbox; the drawn box keeps its size around the room's centre (+ offset)
                    float rw = row ? size[i] : crossSize[i], rh = row ? crossSize[i] : size[i];
                    wx += rw / 2 + it.drawnOffset.x - ir.width / 2;
                    wy += rh / 2 + it.drawnOffset.y - ir.height / 2;
                }
                // its top-left now, in the container's local space
                Vector2 lp = rt.localPosition;
                float nowLeft = lp.x + ir.xMin, nowTop = lp.y + ir.yMax;
                float wantLeft = r.xMin + wx, wantTop = r.yMax - wy;
                var d = new Vector2(wantLeft - nowLeft, wantTop - nowTop);
                if (d.sqrMagnitude > 1e-8f) rt.anchoredPosition += d;
                pos += size[i] + MEnd(it, main) + gap + between;
            }
        }

        // margin = top, right, bottom, left
        static float MStart(UiFlexItem it, int axis) => axis == 0 ? it.margin.w : it.margin.x;
        static float MEnd(UiFlexItem it, int axis) => axis == 0 ? it.margin.y : it.margin.z;
    }
}
