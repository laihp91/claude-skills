using UnityEngine;
using UnityEngine.UI;

namespace T2U.Kit
{
    /// <summary>
    /// A bar the game fills (web: data-ui-fill, the code set its width or height in %). <c>refs.Fill("army.hpFill").Set(0.4f)</c>.
    /// Two kinds, chosen by the importer to look like the web at every length:
    ///  - reveal: the full picture of the bar is uncovered from one side (a square-ended bar cut by its rounded frame);
    ///  - resize: the bar itself gets shorter and keeps its own round ends (a 9-sliced picture).
    /// </summary>
    public sealed class UiFill : MonoBehaviour
    {
        public Image image;
        public bool reveal;
        public RectTransform.Edge edge = RectTransform.Edge.Left;
        /// <summary>resize: distance from the parent's edge, and the full length, in canvas units.</summary>
        public float inset, full;
        [Range(0, 1)] public float value = 1;

        public void Set(float v)
        {
            v = Mathf.Clamp01(v);
            if (Mathf.Approximately(v, value)) return;
            value = v;
            if (reveal) { if (image != null) image.fillAmount = v; }
            else ((RectTransform)transform).SetInsetAndSizeFromParentEdge(edge, inset, full * v);
        }
    }
}
