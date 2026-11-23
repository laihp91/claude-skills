using UnityEngine;

namespace T2U.Kit
{
    /// <summary>
    /// env(safe-area-inset-top/bottom) of the web CSS: pushes a full-stretch container down (top) or up (bottom) by the
    /// part of the device's notch / home indicator that overlaps the game area. Only vertical insets (portrait game).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaPadding : MonoBehaviour
    {
        [SerializeField] bool _top = true;
        [SerializeField] bool _bottom = true;
        [SerializeField] Canvas _canvas;

        Rect _lastSafe;
        Vector2Int _lastScreen;

        void OnEnable() => Apply();

        void Update()
        {
            if (Screen.safeArea != _lastSafe || _lastScreen.x != Screen.width || _lastScreen.y != Screen.height) Apply();
        }

        void Apply()
        {
            _lastSafe = Screen.safeArea;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            var cam = _canvas != null ? _canvas.worldCamera : null;
            // game area in screen pixels (letter/pillar-boxed by CameraFitter)
            Rect area = cam != null ? cam.pixelRect : new Rect(0, 0, Screen.width, Screen.height);
            float insetTop = Mathf.Max(0, area.yMax - _lastSafe.yMax);
            float insetBottom = Mathf.Max(0, _lastSafe.yMin - area.yMin);
            float scale = _canvas != null && _canvas.scaleFactor > 0 ? _canvas.scaleFactor : 1;
            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMax = new Vector2(0, _top ? -insetTop / scale : 0);
            rt.offsetMin = new Vector2(0, _bottom ? insetBottom / scale : 0);
        }
    }
}
