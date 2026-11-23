using UnityEngine;
using UnityEngine.UI;

namespace T2U.Kit
{
    /// <summary>
    /// The web game's frame (main.js layout(): <c>w = min(screenW, h · world.w / world.h)</c>): never wider than
    /// <see cref="maxAspect"/> (width / height). On a wider screen (a tablet) the game sits in the middle with bars left
    /// and right: the world camera draws into that frame (<c>Camera.rect</c>), the canvas scales to the frame's height
    /// and every full-width screen under it is inset to the frame. A taller screen keeps the whole width, as on the web.
    /// Put it on the UI canvas; the bars are what is behind (a camera that clears to black, no picture).
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class AspectFrame : MonoBehaviour
    {
        [Tooltip("The camera that draws the world (its viewport becomes the frame).")]
        public Camera worldCamera;
        [Tooltip("Widest frame, width / height (web: world.w / world.h).")]
        public float maxAspect = 9f / 16f;

        CanvasScaler _scaler;
        float _baseMatch = -1;
        Vector2Int _last;

        void OnEnable() => Apply();

        void Update()
        {
            if (Screen.width != _last.x || Screen.height != _last.y) Apply();
        }

        /// <summary>Lays the frame out for the screen now (also called by tools that capture at another size).</summary>
        public void Apply() => Apply(Screen.width, Screen.height);

        public void Apply(int screenW, int screenH)
        {
            _last = new Vector2Int(screenW, screenH);
            if (screenW <= 0 || screenH <= 0) return;
            if (_scaler == null) _scaler = GetComponent<CanvasScaler>();
            if (_scaler != null && _baseMatch < 0) _baseMatch = _scaler.matchWidthOrHeight;
            float sw = screenW, sh = screenH;
            bool wide = sw / sh > maxAspect + 1e-4f;
            float fw = wide ? sh * maxAspect : sw;
            if (worldCamera != null) worldCamera.rect = wide ? new Rect((1 - fw / sw) / 2, 0, fw / sw, 1) : new Rect(0, 0, 1, 1);
            float inset = 0;
            if (_scaler != null)
            {
                // a wide screen: scale to the height, so the frame is reference-width units across
                _scaler.matchWidthOrHeight = wide ? 1 : _baseMatch;
                var refRes = _scaler.referenceResolution;
                float scale = wide ? sh / refRes.y : sw / refRes.x;
                if (wide && scale > 0) inset = (sw - fw) / 2 / scale;
            }
            foreach (Transform t in transform)
            {
                var r = t as RectTransform;
                if (r == null || r.anchorMin.x != 0 || r.anchorMax.x != 1) continue; // only full-width screens
                r.offsetMin = new Vector2(inset, r.offsetMin.y);
                r.offsetMax = new Vector2(-inset, r.offsetMax.y);
            }
        }
    }
}
