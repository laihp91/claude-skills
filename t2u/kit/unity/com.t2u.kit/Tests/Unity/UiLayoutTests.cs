using NUnit.Framework;
using UnityEngine;

namespace T2U.Kit.Tests
{
    /// <summary>UiFlex against CSS flexbox arithmetic, and UiAnim's timing functions (the parts the UI check cannot see:
    /// what happens when a child is hidden at runtime).</summary>
    public class UiLayoutTests
    {
        GameObject _root;

        [TearDown] public void Done() { if (_root != null) Object.DestroyImmediate(_root); }

        RectTransform Box(Transform parent, string name, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); // top-left, like the importer's "left/top" anchoring
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static float Left(RectTransform rt) => rt.anchoredPosition.x;
        static float Top(RectTransform rt) => -rt.anchoredPosition.y;

        UiFlex Row(float w, float h, string justify, float gap, params float[] widths)
        {
            _root = new GameObject("root", typeof(RectTransform));
            var c = Box(_root.transform, "row", w, h);
            var f = c.gameObject.AddComponent<UiFlex>();
            f.Set("row", justify, "center");
            f.gap = gap;
            for (int i = 0; i < widths.Length; i++)
            {
                var it = Box(c, "i" + i, widths[i], 20).gameObject.AddComponent<UiFlexItem>();
                it.dom = i; it.restSize = new Vector2(widths[i], 20);
            }
            f.Layout();
            return f;
        }

        RectTransform Item(UiFlex f, int i) => (RectTransform)f.transform.GetChild(i);

        [Test]
        public void CenterRowRecentresWhenAChildIsHidden()
        {
            var f = Row(300, 40, "center", 10, 50, 50, 50);
            // 3 x 50 + 2 x 10 = 170, free 130 → starts at 65
            Assert.AreEqual(65, Left(Item(f, 0)), 0.01f);
            Assert.AreEqual(125, Left(Item(f, 1)), 0.01f);
            Assert.AreEqual(10, Top(Item(f, 0)), 0.01f); // centred on the cross axis: (40 - 20) / 2
            Item(f, 1).gameObject.SetActive(false);
            f.Layout();
            // 2 x 50 + 10 = 110, free 190 → 95 and 155
            Assert.AreEqual(95, Left(Item(f, 0)), 0.01f);
            Assert.AreEqual(155, Left(Item(f, 2)), 0.01f);
        }

        [Test]
        public void SpaceBetweenAndGrow()
        {
            var f = Row(300, 40, "space-between", 0, 50, 50, 50);
            Assert.AreEqual(0, Left(Item(f, 0)), 0.01f);
            Assert.AreEqual(125, Left(Item(f, 1)), 0.01f);
            Assert.AreEqual(250, Left(Item(f, 2)), 0.01f);
            Item(f, 1).GetComponent<UiFlexItem>().grow = 1;
            f.Layout();
            Assert.AreEqual(200, Item(f, 1).rect.width, 0.01f);
            Assert.AreEqual(250, Left(Item(f, 2)), 0.01f);
        }

        [Test]
        public void ContainerThatFitsItsContentShrinks()
        {
            var f = Row(170, 40, "center", 10, 50, 50, 50);
            f.fitMain = true;
            Item(f, 2).gameObject.SetActive(false);
            f.Layout();
            Assert.AreEqual(110, ((RectTransform)f.transform).rect.width, 0.01f);
        }

        [Test]
        public void TimingFunctions()
        {
            Assert.AreEqual(0.5f, UiAnim.Bezier.Parse("linear").Ease(0.5f), 1e-4f);
            Assert.AreEqual(0.5f, UiAnim.Bezier.Parse("ease-in-out").Ease(0.5f), 1e-3f);
            // ease at 0.5 ≈ 0.8024 (Chrome)
            Assert.AreEqual(0.8024f, UiAnim.Bezier.Parse("ease").Ease(0.5f), 2e-3f);
            // overshoot curves go past 1, like the web's panelIn
            Assert.Greater(UiAnim.Bezier.Parse("cubic-bezier(0.2, 1.2, 0.4, 1)").Ease(0.6f), 1f);
            CollectionAssert.AreEqual(new[] { "a 1s cubic-bezier(0.2, 1.2, 0.4, 1) 1 normal", "b 2s ease 1 normal" },
                UiAnim.SplitTop("a 1s cubic-bezier(0.2, 1.2, 0.4, 1) 1 normal, b 2s ease 1 normal"));
        }
    }
}
