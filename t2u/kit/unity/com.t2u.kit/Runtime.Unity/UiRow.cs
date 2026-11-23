using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace T2U.Kit
{
    /// <summary>
    /// One row of a list (web: a child of a data-ui-list element). Its elements are found by the part of their name
    /// after the row's own name: in row "army.guard", "army.guard.lv" is ".lv" and the row itself is "".
    /// Rows that were on the web page come with the screen (their states live in the screen's UiStates); rows made at
    /// runtime with <see cref="UiList.Add"/> carry their own UiStates (state hosts named by the same suffixes).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiRow : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string suffix;
            public RectTransform rect;
            /// <summary>data-ui-button id of the web row this one was made from ("" if not a button).</summary>
            public string button;
        }

        /// <summary>The row's name (web: the data-ui of the row element; for runtime rows: the key given to Add).</summary>
        public string key;
        /// <summary>The list template it was made from ("army.list#0").</summary>
        public string template;
        public List<Entry> entries = new List<Entry>();

        Dictionary<string, RectTransform> _map;

        public RectTransform Get(string suffix = "")
        {
            if (_map == null)
            {
                _map = new Dictionary<string, RectTransform>();
                foreach (var e in entries) _map[e.suffix] = e.rect;
            }
            return _map.TryGetValue(suffix, out var r) ? r : throw new KeyNotFoundException($"UI row '{key}' has no element '{suffix}'");
        }

        public TMP_Text Text(string suffix) => Get(suffix).GetComponentInChildren<TMP_Text>(true);
        public Image Image(string suffix) => Get(suffix).GetComponent<Image>();
        public UiFill Fill(string suffix) => Get(suffix).GetComponent<UiFill>();

        public void Show(string suffix, bool on)
        {
            var go = Get(suffix).gameObject;
            if (go.activeSelf != on) go.SetActive(on);
        }

        /// <summary>Switches a state of an element of this row (web: classList.toggle(state, on)).</summary>
        public void State(string suffix, string state, bool on)
        {
            var own = GetComponent<UiStates>();
            if (own != null) { own.Set(suffix, state, on); return; }
            var screen = GetComponentInParent<UiRefs>(true);
            if (screen != null) screen.State(key + suffix, state, on);
        }

        /// <summary>Calls <paramref name="onClick"/> with the suffix of the button tapped (".buy", "" for the row itself).</summary>
        public void OnClick(Action<string> onClick)
        {
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.button)) continue;
                var b = e.rect.GetComponent<Button>();
                if (b == null) continue;
                var s = e.suffix;
                b.onClick.AddListener(() => onClick(s));
            }
        }
    }
}
