using TMPro;
using UnityEngine;

namespace T2U.Kit
{
    /// <summary>
    /// A child that flows inside a <see cref="UiFlex"/> container (web: a flex item that is not absolutely positioned).
    /// Written by the UI layout importer from ui.json `flow`; elements without this component are not moved by the
    /// container (absolute children, the "Skin"/"Text"/"Image" helper children).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiFlexItem : MonoBehaviour
    {
        /// <summary>CSS flex-grow: share of the free space this item takes.</summary>
        public float grow;
        /// <summary>CSS order (lower first), then <see cref="dom"/>.</summary>
        public int order;
        /// <summary>Place in the web page (the importer reorders siblings for z-index, so the sibling index is not it).</summary>
        public int dom;
        /// <summary>CSS self-alignment on the cross axis ("" = the container's align-items).</summary>
        public string alignSelf = "";
        /// <summary>Margins in design units: x top, y right, z bottom, w left.</summary>
        public Vector4 margin;
        /// <summary>The room it takes among its siblings, measured on the web at rest (design units).</summary>
        public Vector2 restSize;
        /// <summary>
        /// The web draws it with a transform (scale, translate): its box on screen is not the room it takes. Then
        /// <see cref="restSize"/> is the room, the element keeps its own (drawn) size, and its centre sits
        /// <see cref="drawnOffset"/> (web axes) from the centre of its room.
        /// </summary>
        public bool transformed;
        public Vector2 drawnOffset;
        /// <summary>A text whose width decides this item's width (web: width auto): the item grows and shrinks with it.</summary>
        public TMP_Text hugText;
        /// <summary>The width <see cref="hugText"/> had at rest, measured by TextMesh Pro when the prefab was made.</summary>
        public float restTextWidth;
        /// <summary>
        /// <see cref="hugText"/> wraps onto several lines (web: white-space normal) in a column: the item is as wide as
        /// its text up to the room the column gives it (<see cref="wrapFill"/>: always that room, web align stretch)
        /// and as tall as its lines. Without it a box measured around a short resting text ("-") keeps that width and
        /// a long text runs one letter per line.
        /// </summary>
        public bool wrapText;
        public bool wrapFill;
        /// <summary>The height <see cref="hugText"/> had at rest (unbounded width), measured by TextMesh Pro when the prefab was made.</summary>
        public float restTextHeight;

        string _lastText;

        UiFlex Container => transform.parent != null ? transform.parent.GetComponent<UiFlex>() : null;

        /// <summary>Width of the item now that its text may have changed (rest width + how much the text grew).</summary>
        public float HugWidth()
        {
            if (hugText == null) return restSize.x;
            _lastText = hugText.text;
            float now = hugText.GetPreferredValues(hugText.text, float.PositiveInfinity, float.PositiveInfinity).x;
            return Mathf.Max(0, restSize.x + now - restTextWidth);
        }

        /// <summary>Size of a wrapping item (<see cref="wrapText"/>) for a column that gives it <paramref name="room"/> across.</summary>
        const float WrapSlack = 1f;

        public Vector2 WrapSize(float room)
        {
            _lastText = hugText.text;
            float padW = restSize.x - restTextWidth, padH = restSize.y - restTextHeight;
            // at least WrapSlack around the text: a box exactly as wide as its one line can be a hair narrower after
            // rounding, and TMP then breaks the line
            float w = wrapFill ? room : Mathf.Min(room, hugText.GetPreferredValues(hugText.text, float.PositiveInfinity, float.PositiveInfinity).x + Mathf.Max(padW, WrapSlack));
            float h = hugText.GetPreferredValues(hugText.text, Mathf.Max(1f, w - Mathf.Max(0, padW)), float.PositiveInfinity).y + padH;
            return new Vector2(Mathf.Max(0, w), Mathf.Max(0, h));
        }

        /// <summary>True when the text changed since the container last measured it.</summary>
        public bool TextChanged() => hugText != null && !string.Equals(_lastText, hugText.text);

        void OnEnable() => Container?.SetDirty();
        void OnDisable() => Container?.SetDirty();
        void OnRectTransformDimensionsChange() => Container?.SetDirty();
    }
}
