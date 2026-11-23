using System;
using System.Collections.Generic;
using UnityEngine;

namespace T2U.Kit
{
    /// <summary>
    /// A list of rows (web: data-ui-list). The rows the web page had come with the screen; <see cref="Add"/> makes a new
    /// row from a row prefab (one per row shape, Assets/_Game/Prefabs/UI/Rows/&lt;template&gt;.prefab, made by the UI
    /// importer from the first row of that shape). Rows go into <see cref="container"/> (the list itself, or the
    /// Content of a scrolling list); a <see cref="UiFlex"/> there places them.
    /// Game code: <c>var row = refs.List("army.list").Add("army.list#0", "army.newUnit"); row.Text(".name").text = …</c>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiList : MonoBehaviour
    {
        [Serializable]
        public sealed class Template
        {
            /// <summary>"army.list#0"</summary>
            public string name;
            /// <summary>The row of the web page this shape was taken from ("army.guard").</summary>
            public string sample;
            public UiRow prefab;
        }

        public RectTransform container;
        public List<Template> templates = new List<Template>();
        int _added;

        /// <summary>Every row in the list now, in order on screen.</summary>
        public List<UiRow> Rows()
        {
            var r = new List<UiRow>();
            var c = container != null ? container : (RectTransform)transform;
            for (int i = 0; i < c.childCount; i++)
            {
                var row = c.GetChild(i).GetComponent<UiRow>();
                if (row != null) r.Add(row);
            }
            r.Sort((a, b) => Dom(a).CompareTo(Dom(b)));
            return r;
        }

        static int Dom(UiRow r)
        {
            var it = r.GetComponent<UiFlexItem>();
            return it != null ? it.dom : r.transform.GetSiblingIndex();
        }

        public Template Find(string template)
        {
            foreach (var t in templates) if (t.name == template) return t;
            throw new KeyNotFoundException($"UI list '{name}' has no row template '{template}'");
        }

        /// <summary>Makes a row from a template (by name, or the first one) at the end of the list.</summary>
        public UiRow Add(string template, string key)
        {
            var t = string.IsNullOrEmpty(template) ? templates[0] : Find(template);
            if (t.prefab == null) throw new InvalidOperationException($"UI list '{name}': template '{t.name}' has no prefab (re-import the UI)");
            var c = container != null ? container : (RectTransform)transform;
            var row = Instantiate(t.prefab, c, false);
            row.name = key;
            row.key = key;
            var it = row.GetComponent<UiFlexItem>();
            if (it != null) it.dom = 100000 + _added;
            _added++;
            return row;
        }

        /// <summary>Removes a row (made at runtime or not).</summary>
        public void Remove(UiRow row)
        {
            if (row == null) return;
            row.gameObject.SetActive(false); // the layout updates at once
            Destroy(row.gameObject);
        }
    }
}
