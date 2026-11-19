using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MimirsVault
{
    internal enum Category { All, Weapons, Armor, Tools, Food, Potions, Materials, Ammo, Trophies, Misc }

    /// <summary>
    /// The Item Sorter window, built from Valheim's own UI pieces (wood panel, buttons, fonts) via Jotunn.
    /// One window is shared by all sorters; it shows the data of whichever sorter the player has open.
    /// It can be dragged by its background, and its position is remembered in the config.
    /// </summary>
    internal static class SorterPanel
    {
        private const float Width = 440f;
        private const float Pad = 16f;
        private const float RowHeight = 36f;

        private class Row
        {
            public GameObject Root;
            public Image Background;
            public Image Icon;
            public Text Name;
            public Text Count;
            public Button TakeButton;
            public SorterLogic.Entry Entry;
        }

        private static SorterBehaviour _owner;
        private static GameObject _panel;
        private static RectTransform _panelRect;
        private static Text _subtitle;
        private static Text _hint;
        private static Text _empty;
        private static InputField _search;
        private static Button _tabTake;
        private static Button _tabDeposit;
        private static Button _sortNow;
        private static Button _selectAll;
        private static Button _clear;
        private static Button _depositSelected;
        private static RectTransform _content;
        private static ScrollRect _scroll;
        private static readonly Dictionary<Category, Button> FilterButtons = new Dictionary<Category, Button>();
        private static readonly List<Row> Rows = new List<Row>();

        private static bool _depositTab;
        private static Category _category = Category.All;
        private static readonly HashSet<string> Selected = new HashSet<string>();
        private static string _shownSignature;
        private static Vector2 _shownAt;

        /// <summary>True while the search box has keyboard focus; game input is blocked meanwhile.</summary>
        internal static bool SearchFocused => _panel != null && _panel.activeSelf && _search != null && _search.isFocused;

        internal static void Show(SorterBehaviour owner) => Guard(() => ShowInternal(owner));

        internal static void Hide(SorterBehaviour owner) => Guard(() => HideInternal(owner));

        internal static void DataChanged(SorterBehaviour owner) => Guard(() =>
        {
            if (_owner == owner && _panel != null && _panel.activeSelf) Rebuild();
        });

        private static bool _loggedFailure;

        /// <summary>
        /// A UI error must never break the sorter itself or leave a half-built window on screen:
        /// log it once and throw the window away, so it is rebuilt from scratch next time.
        /// </summary>
        private static void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Jotunn.Logger.LogError($"Item Sorter window failed: {e}");
                }
                DestroyPanel();
            }
        }

        private static void DestroyPanel()
        {
            if (_panel != null) Object.Destroy(_panel);
            _panel = null;
            _owner = null;
            FilterButtons.Clear();
            Rows.Clear();
        }

        private static void ShowInternal(SorterBehaviour owner)
        {
            if (!EnsureCreated()) return;
            _owner = owner;
            _depositTab = false;
            _category = Category.All;
            _search.text = "";
            Selected.Clear();
            _shownSignature = null;
            _panelRect.anchoredPosition = ClampOnScreen(SavedOrDefaultPosition());
            _shownAt = _panelRect.anchoredPosition;
            _panel.SetActive(true);
            _scroll.verticalNormalizedPosition = 1f;
            Rebuild();
        }

        private static void HideInternal(SorterBehaviour owner)
        {
            if (_owner != owner) return;
            _owner = null;
            if (_panel == null) return;
            // Only remember the position once the player has actually dragged the window.
            if (_panel.activeSelf && Vector2.Distance(_panelRect.anchoredPosition, _shownAt) > 1f) SavePosition();
            if (_search != null) _search.DeactivateInputField();
            _panel.SetActive(false);
        }

        // ---------------------------------------------------------------- building the window

        private static bool EnsureCreated()
        {
            if (_panel != null) return true; // Unity null-check also catches a destroyed canvas
            var front = GUIManager.CustomGUIFront;
            if (front == null) return false;
            FilterButtons.Clear();
            Rows.Clear();

            var gui = GUIManager.Instance;
            float height = PanelHeight();
            _panel = gui.CreateWoodpanel(front.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Width, height, true);
            _panel.name = "MimirsVault_SorterPanel";
            _panelRect = (RectTransform)_panel.transform;
            _panelRect.pivot = new Vector2(0f, 1f);

            float inner = Width - 2 * Pad;
            float y = 12f;

            var title = AddText("Item Sorter", gui.NorseBold, 30, gui.ValheimOrange, TextAnchor.MiddleCenter);
            Place(title.rectTransform, Pad, y, inner, 36);
            var reset = AddButton("Reset", 14, ResetPosition);
            Place((RectTransform)reset.transform, Width - Pad - 64, y + 4, 64, 26);
            y += 36;

            _subtitle = AddText("", gui.AveriaSerifBold, 15, gui.ValheimBeige, TextAnchor.MiddleCenter);
            Place(_subtitle.rectTransform, Pad, y, inner, 20);
            y += 28;

            float half = (inner - 8) / 2;
            _tabTake = AddButton("Take from chests", 16, () => SetTab(false));
            Place((RectTransform)_tabTake.transform, Pad, y, half, 32);
            _tabDeposit = AddButton("Deposit from inventory", 16, () => SetTab(true));
            Place((RectTransform)_tabDeposit.transform, Pad + half + 8, y, half, 32);
            y += 40;

            _search = gui.CreateInputField(_panel.transform, Vector2.zero, Vector2.zero, Vector2.zero,
                InputField.ContentType.Standard, "Search items...", 16, inner, 32).GetComponent<InputField>();
            Place((RectTransform)_search.transform, Pad, y, inner, 32);
            _search.onValueChanged.AddListener(_ => Guard(() => Rebuild()));
            y += 40;

            var categories = (Category[])Enum.GetValues(typeof(Category));
            float gap = 4f;
            float filterWidth = (inner - 4 * gap) / 5;
            for (int i = 0; i < categories.Length; i++)
            {
                var category = categories[i];
                var button = AddButton(category.ToString(), 13, () => SetCategory(category));
                Place((RectTransform)button.transform, Pad + (i % 5) * (filterWidth + gap), y + (i / 5) * 30, filterWidth, 26);
                FilterButtons[category] = button;
            }
            y += 64;

            _sortNow = AddButton("Sort now", 15, () => _owner?.SortNow(true));
            Place((RectTransform)_sortNow.transform, Pad, y, 100, 28);
            _selectAll = AddButton("Select all", 15, () => SelectAll(Filtered(_owner?.Carried)));
            Place((RectTransform)_selectAll.transform, Pad, y, 100, 28);
            _clear = AddButton("Clear", 15, () => { Selected.Clear(); Rebuild(true); });
            Place((RectTransform)_clear.transform, Pad + 106, y, 70, 28);
            _depositSelected = AddButton("Sort selected (0)", 15, DepositSelected);
            Place((RectTransform)_depositSelected.transform, Pad + 182, y, inner - 182, 28);
            _hint = AddText("", gui.AveriaSerif, 13, gui.ValheimBeige, TextAnchor.MiddleLeft);
            Place(_hint.rectTransform, Pad + 108, y, inner - 108, 28);
            y += 36;

            float listHeight = height - y - Pad;
            var scrollView = gui.CreateScrollView(_panel.transform, false, true, 8f, 10f,
                gui.ValheimScrollbarHandleColorBlock, new Color(0.16f, 0.10f, 0.06f, 0.8f), inner, listHeight);
            Place((RectTransform)scrollView.transform, Pad, y, inner, listHeight);
            // Jotunn wraps the scroll view in its own small canvas; the ScrollRect sits on a child.
            _scroll = scrollView.GetComponentInChildren<ScrollRect>(true);
            _scroll.scrollSensitivity = 30f;
            _content = _scroll.content;

            var layout = _content.GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 2f;
            layout.padding = new RectOffset(4, 12, 4, 4);
            var fitter = _content.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _empty = AddText("", gui.AveriaSerifBold, 16, gui.ValheimBeige, TextAnchor.MiddleCenter);
            Place(_empty.rectTransform, Pad, y + 20, inner, 30);

            _panel.SetActive(false);
            return true;
        }

        private static Text AddText(string text, Font font, int size, Color color, TextAnchor alignment, Transform parent = null)
        {
            var go = GUIManager.Instance.CreateText(text, parent ?? _panel.transform, Vector2.zero, Vector2.zero, Vector2.zero,
                font, size, color, true, Color.black, 100f, 20f, false);
            var t = go.GetComponent<Text>();
            t.alignment = alignment;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static Button AddButton(string text, int size, Action onClick, Transform parent = null)
        {
            var go = GUIManager.Instance.CreateButton(text, parent ?? _panel.transform, Vector2.zero, Vector2.zero, Vector2.zero, 100f, 30f);
            var label = go.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontSize = size;
                label.resizeTextForBestFit = false;
            }
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(() => Guard(onClick));
            return button;
        }

        /// <summary>Positions a child by its top-left corner, in pixels from the parent's top-left.</summary>
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static Row CreateRow()
        {
            var gui = GUIManager.Instance;
            var root = new GameObject("Row", typeof(RectTransform));
            root.transform.SetParent(_content, false);
            var element = root.AddComponent<LayoutElement>();
            element.preferredHeight = RowHeight;
            element.minHeight = RowHeight;

            var row = new Row { Root = root, Background = root.AddComponent<Image>() };
            row.Background.color = Color.clear;

            // Clicking a row selects it on the Deposit tab.
            var rowButton = root.AddComponent<Button>();
            rowButton.transition = Selectable.Transition.None;
            rowButton.onClick.AddListener(() => Guard(() => OnRowClicked(row)));

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(root.transform, false);
            row.Icon = iconGo.AddComponent<Image>();
            row.Icon.preserveAspect = true;
            row.Icon.raycastTarget = false;
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(4f, 0f);
            iconRect.sizeDelta = new Vector2(30f, 30f);

            row.Name = AddText("", gui.AveriaSerifBold, 16, gui.ValheimBeige, TextAnchor.MiddleLeft, root.transform);
            Stretch(row.Name.rectTransform, 42f, 130f);
            row.Count = AddText("", gui.AveriaSerifBold, 16, gui.ValheimYellow, TextAnchor.MiddleRight, root.transform);
            Stretch(row.Count.rectTransform, -1f, 70f, 56f);

            row.TakeButton = AddButton("Take", 14, () => OnTakeClicked(row), root.transform);
            var takeRect = (RectTransform)row.TakeButton.transform;
            takeRect.anchorMin = takeRect.anchorMax = new Vector2(1f, 0.5f);
            takeRect.pivot = new Vector2(1f, 0.5f);
            takeRect.anchoredPosition = new Vector2(-2f, 0f);
            takeRect.sizeDelta = new Vector2(62f, 28f);
            return row;
        }

        /// <summary>
        /// Stretches a text across the row. With <paramref name="width"/> it is instead a fixed-width
        /// column ending <paramref name="right"/> pixels from the right edge.
        /// </summary>
        private static void Stretch(RectTransform rt, float left, float right, float width = 0f)
        {
            if (width > 0f)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-right, 0f);
                rt.sizeDelta = new Vector2(width, RowHeight);
                return;
            }
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(-right, 0f);
        }

        // ---------------------------------------------------------------- state

        private static void SetTab(bool deposit)
        {
            _depositTab = deposit;
            _scroll.verticalNormalizedPosition = 1f;
            Rebuild(true);
        }

        private static void SetCategory(Category category)
        {
            _category = category;
            _scroll.verticalNormalizedPosition = 1f;
            Rebuild(true);
        }

        private static List<SorterLogic.Entry> Filtered(IEnumerable<SorterLogic.Entry> entries)
        {
            if (entries == null) return new List<SorterLogic.Entry>();
            string search = (_search != null ? _search.text : "").Trim();
            return entries
                .Where(e => _category == Category.All || SorterLogic.CategoryOf(e.Sample) == _category)
                .Where(e => search.Length == 0 || e.DisplayName.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .ToList();
        }

        private static void Rebuild(bool force = false)
        {
            if (_owner == null || _panel == null) return;
            var gui = GUIManager.Instance;

            var source = _depositTab ? _owner.Carried : _owner.Stored;
            Selected.IntersectWith(_owner.Carried.Select(e => e.Key));
            var shown = Filtered(source);

            string signature = string.Join("|", new[]
            {
                _depositTab.ToString(), _category.ToString(), _search.text,
                string.Join(",", shown.Select(e => e.Key + ":" + e.Total)),
                string.Join(",", Selected.OrderBy(k => k)),
            });
            if (!force && signature == _shownSignature) return;
            _shownSignature = signature;

            _subtitle.text = $"{_owner.InRange.Count} chest{(_owner.InRange.Count == 1 ? "" : "s")} in range";
            Highlight(_tabTake, !_depositTab);
            Highlight(_tabDeposit, _depositTab);
            foreach (var pair in FilterButtons) Highlight(pair.Value, pair.Key == _category);

            _sortNow.gameObject.SetActive(!_depositTab);
            _selectAll.gameObject.SetActive(_depositTab);
            _clear.gameObject.SetActive(_depositTab);
            _depositSelected.gameObject.SetActive(_depositTab);
            _depositSelected.interactable = Selected.Count > 0;
            _depositSelected.GetComponentInChildren<Text>().text = $"Sort selected ({Selected.Count})";
            _hint.gameObject.SetActive(!_depositTab);
            _hint.text = "Take: 1 stack   Shift: all   Ctrl: 1";

            while (Rows.Count < shown.Count) Rows.Add(CreateRow());
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                bool used = i < shown.Count;
                row.Root.SetActive(used);
                if (!used) continue;

                var entry = shown[i];
                row.Entry = entry;
                row.Icon.sprite = entry.Sample.GetIcon();
                row.Icon.enabled = row.Icon.sprite != null;
                row.Name.text = entry.DisplayName;
                row.Count.text = entry.Total.ToString(CultureInfo.InvariantCulture);
                row.TakeButton.gameObject.SetActive(!_depositTab);
                bool selected = _depositTab && Selected.Contains(entry.Key);
                row.Background.color = selected ? new Color(gui.ValheimOrange.r, gui.ValheimOrange.g, gui.ValheimOrange.b, 0.3f) : Color.clear;
                row.Name.color = selected ? gui.ValheimOrange : gui.ValheimBeige;
            }

            _empty.gameObject.SetActive(shown.Count == 0);
            _empty.text = source.Count == 0
                ? (_depositTab ? "You're not carrying anything." : "No items in range.")
                : "No matches.";
        }

        private static void Highlight(Button button, bool on)
        {
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = on ? GUIManager.Instance.ValheimOrange : GUIManager.Instance.ValheimBeige;
        }

        // ---------------------------------------------------------------- clicks

        private static bool Shift => ZInput.GetKey(KeyCode.LeftShift, false) || ZInput.GetKey(KeyCode.RightShift, false);
        private static bool Ctrl => ZInput.GetKey(KeyCode.LeftControl, false) || ZInput.GetKey(KeyCode.RightControl, false);

        private static void OnTakeClicked(Row row)
        {
            if (_owner == null || row.Entry == null) return;
            int amount = Mathf.Max(1, row.Entry.Sample.m_shared.m_maxStackSize);
            if (Shift) amount = row.Entry.Total;
            else if (Ctrl) amount = 1;
            _owner.Take(row.Entry, amount);
        }

        private static void OnRowClicked(Row row)
        {
            if (!_depositTab || row.Entry == null) return;
            if (Shift) SelectAll(Filtered(_owner?.Carried));
            else if (!Selected.Remove(row.Entry.Key)) Selected.Add(row.Entry.Key);
            Rebuild(true);
        }

        /// <summary>Selects every entry shown, or clears them if they are all selected already.</summary>
        private static void SelectAll(List<SorterLogic.Entry> shown)
        {
            if (shown.All(e => Selected.Contains(e.Key))) foreach (var e in shown) Selected.Remove(e.Key);
            else foreach (var e in shown) Selected.Add(e.Key);
            Rebuild(true);
        }

        private static void DepositSelected()
        {
            if (_owner == null || Selected.Count == 0) return;
            _owner.Deposit(Selected.ToList());
            Selected.Clear();
            Rebuild(true);
        }

        // ---------------------------------------------------------------- position

        private static float PanelHeight()
        {
            var canvas = (RectTransform)GUIManager.CustomGUIFront.transform;
            return Mathf.Clamp(canvas.rect.height - 220f, 420f, 640f);
        }

        private static Vector2 SavedOrDefaultPosition()
        {
            var parts = Plugin.PanelPosition.Value.Split(';');
            if (parts.Length == 2 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                return new Vector2(x, y);
            }
            return DefaultPosition();
        }

        /// <summary>
        /// Default spot: the free column between the chest/quick-slot panels and the crafting panel,
        /// top-aligned with the chest panel.
        /// </summary>
        private static Vector2 DefaultPosition()
        {
            var canvas = (RectTransform)GUIManager.CustomGUIFront.transform;
            var gui = InventoryGui.instance;
            if (gui != null && gui.m_crafting != null && gui.m_container != null &&
                TryToLocal(gui.m_crafting, 1, canvas, out var craftingTopLeft) &&
                TryToLocal(gui.m_container, 1, canvas, out var containerTopLeft))
            {
                return new Vector2(craftingTopLeft.x - 12f - Width, containerTopLeft.y);
            }
            return new Vector2(-Width / 2f, _panelRect.rect.height / 2f);
        }

        /// <summary>Converts one corner (0 bottom-left, 1 top-left, ...) of a vanilla GUI element into our canvas.</summary>
        private static bool TryToLocal(RectTransform element, int corner, RectTransform canvas, out Vector2 local)
        {
            var corners = new Vector3[4];
            element.GetWorldCorners(corners);
            var sourceCanvas = element.GetComponentInParent<Canvas>();
            var sourceCamera = sourceCanvas != null && sourceCanvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? sourceCanvas.rootCanvas.worldCamera : null;
            var screen = RectTransformUtility.WorldToScreenPoint(sourceCamera, corners[corner]);

            var ourCanvas = canvas.GetComponentInParent<Canvas>();
            var ourCamera = ourCanvas != null && ourCanvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? ourCanvas.rootCanvas.worldCamera : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, ourCamera, out local);
        }

        private static Vector2 ClampOnScreen(Vector2 topLeft)
        {
            var canvas = ((RectTransform)GUIManager.CustomGUIFront.transform).rect;
            float h = _panelRect.rect.height;
            float x = Mathf.Clamp(topLeft.x, canvas.xMin, canvas.xMax - Width);
            float y = Mathf.Clamp(topLeft.y, canvas.yMin + h, canvas.yMax);
            return new Vector2(x, y);
        }

        private static void SavePosition()
        {
            var p = _panelRect.anchoredPosition;
            Plugin.PanelPosition.Value = p.x.ToString("0", CultureInfo.InvariantCulture) + ";" +
                                         p.y.ToString("0", CultureInfo.InvariantCulture);
        }

        private static void ResetPosition()
        {
            Plugin.PanelPosition.Value = "";
            _panelRect.anchoredPosition = ClampOnScreen(DefaultPosition());
            _shownAt = _panelRect.anchoredPosition;
        }
    }
}
