using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MimirsVault
{
    /// <summary>
    /// Lives on every Item Sorter chest. Sorts dropped-in items into nearby chests and, while the
    /// local player has it open, draws a searchable panel to take items from every chest in range
    /// or to deposit selected items from the player's inventory.
    /// </summary>
    public class SorterBehaviour : MonoBehaviour
    {
        private const string SearchControl = "MimirsVaultSearch";
        private const float RefreshInterval = 0.5f;

        /// <summary>True while the search box has keyboard focus; game input is blocked meanwhile.</summary>
        internal static bool SearchFocused;

        private enum Tab { Take, Deposit }

        private Container _container;
        private Inventory _inventory;
        private bool _wasOpen;
        private bool _sorting;
        private float _sortAt = -1f;
        private int _countAfterLastSort = -1;

        private Tab _tab = Tab.Take;
        private string _search = "";
        private Vector2 _scroll;
        private float _nextRefresh;
        private List<Container> _inRange = new List<Container>();
        private List<SorterLogic.Entry> _entries = new List<SorterLogic.Entry>();
        private List<SorterLogic.Entry> _carried = new List<SorterLogic.Entry>();
        private readonly HashSet<string> _selected = new HashSet<string>();

        private void Awake()
        {
            _container = GetComponent<Container>();
            SorterVisuals.Apply(gameObject, () => Palette.Frost);
        }

        private void Start()
        {
            _inventory = _container != null ? _container.GetInventory() : null;
            if (_inventory != null) _inventory.m_onChanged += OnInventoryChanged;
        }

        private void OnDestroy()
        {
            if (_inventory != null) _inventory.m_onChanged -= OnInventoryChanged;
            if (_wasOpen)
            {
                SearchFocused = false;
                ClickBlocker.SetVisible(false, default);
            }
        }

        private void OnInventoryChanged()
        {
            if (_sorting || !SorterLogic.IsOpenByLocalPlayer(_container)) return;
            _sortAt = Time.time + Plugin.SortDelay.Value;
            _nextRefresh = 0f;
        }

        private void Update()
        {
            if (_container == null || _inventory == null) return;

            bool open = SorterLogic.IsOpenByLocalPlayer(_container);
            if (open && !_wasOpen)
            {
                _search = "";
                _scroll = Vector2.zero;
                _selected.Clear();
                _nextRefresh = 0f;
            }
            else if (!open && _wasOpen)
            {
                SearchFocused = false;
                if (_sortAt > 0f) SortNow(); // closed before the delay elapsed
            }
            // Only the open sorter (or the one that just closed) touches the shared blocker.
            if (open || _wasOpen) ClickBlocker.SetVisible(open, PanelRect());
            _wasOpen = open;

            if (_sortAt > 0f && Time.time >= _sortAt) SortNow();

            if (open && Time.time >= _nextRefresh) Refresh();
        }

        private void Refresh()
        {
            _nextRefresh = Time.time + RefreshInterval;
            _inRange = SorterLogic.FindContainers(transform.position, Plugin.Radius.Value, null);
            _entries = SorterLogic.Aggregate(_inRange);
            _carried = SorterLogic.Aggregate(CarriedItems());
            _selected.IntersectWith(_carried.Select(e => e.Key));
        }

        /// <summary>Everything in the player's inventory except equipped gear.</summary>
        private static IEnumerable<ItemDrop.ItemData> CarriedItems()
        {
            var player = Player.m_localPlayer;
            if (player == null) return Enumerable.Empty<ItemDrop.ItemData>();
            return player.GetInventory().GetAllItems().Where(i => !i.m_equipped);
        }

        private void SortNow(bool explicitRequest = false)
        {
            _sortAt = -1f;
            _sorting = true;
            try
            {
                // Only warn about leftovers when something new was put in, not every time the
                // player takes an item back out of a sorter that holds leftovers already.
                bool added = explicitRequest || CountItems(_inventory) > _countAfterLastSort;
                int items = SorterLogic.Sort(_container, out int chests);
                _countAfterLastSort = CountItems(_inventory);
                Report(items, chests);
                if (added && _countAfterLastSort > 0)
                {
                    Warn($"{Plural(_countAfterLastSort, "item")} stayed in the sorter: no room in nearby chests");
                }
            }
            finally
            {
                _sorting = false;
                _nextRefresh = 0f;
            }
        }

        private void DepositSelected()
        {
            var player = Player.m_localPlayer;
            if (player == null || _selected.Count == 0) return;

            var playerInv = player.GetInventory();
            var items = CarriedItems().Where(i => _selected.Contains(SorterLogic.KeyOf(i))).ToList();

            _sorting = true;
            try
            {
                int moved = SorterLogic.Distribute(playerInv, items, _container, out int chests);
                Report(moved, chests);

                // Whatever found no chest goes into the sorter itself.
                int intoSorter = 0;
                foreach (var item in items.Where(i => playerInv.GetAllItems().Contains(i)).ToList())
                {
                    intoSorter += SorterLogic.Move(playerInv, item, _inventory, item.m_stack, true, Plugin.GroupSimilarItems.Value);
                }
                _countAfterLastSort = CountItems(_inventory);

                int kept = items.Where(i => playerInv.GetAllItems().Contains(i)).Sum(i => i.m_stack);
                if (kept > 0)
                {
                    Warn($"{Plural(kept, "item")} stayed in your inventory: nearby chests and the sorter are full");
                }
                else if (intoSorter > 0)
                {
                    Warn($"{Plural(intoSorter, "item")} went into the sorter: no room in nearby chests");
                }
                _selected.Clear();
            }
            finally
            {
                _sorting = false;
                _nextRefresh = 0f;
            }
        }

        private static void Report(int items, int chests)
        {
            if (items > 0 && Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft,
                    $"Sorted {Plural(items, "item")} into {Plural(chests, "chest")}");
            }
        }

        private static void Warn(string text)
        {
            if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
        }

        private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

        private static int CountItems(Inventory inventory) => inventory.GetAllItems().Sum(i => i.m_stack);

        /// <summary>The panel's screen rect (IMGUI coordinates: origin top-left), centred on screen.</summary>
        internal static Rect PanelRect()
        {
            float width = 420f;
            float height = Mathf.Min(640f, Screen.height - 160f);
            return new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
        }

        private void OnGUI()
        {
            if (!_wasOpen) return;
            GUILayout.Window(GetInstanceID(), PanelRect(), DrawWindow, $"Item Sorter — {_inRange.Count} chests in range");
        }

        private void DrawWindow(int id)
        {
            var e = Event.current;
            if (SearchFocused && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                GUI.FocusControl(null);
                e.Use();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_tab == Tab.Take, "Take from chests", GUI.skin.button)) SwitchTab(Tab.Take);
            if (GUILayout.Toggle(_tab == Tab.Deposit, "Deposit from inventory", GUI.skin.button)) SwitchTab(Tab.Deposit);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(50f));
            GUI.SetNextControlName(SearchControl);
            _search = GUILayout.TextField(_search ?? "");
            if (GUILayout.Button("x", GUILayout.Width(24f)))
            {
                _search = "";
                GUI.FocusControl(null);
            }
            GUILayout.EndHorizontal();

            if (e.type == EventType.Repaint || e.type == EventType.Layout)
            {
                SearchFocused = GUI.GetNameOfFocusedControl() == SearchControl;
            }

            if (_tab == Tab.Take) DrawTakeTab();
            else DrawDepositTab();
        }

        private void SwitchTab(Tab tab)
        {
            if (_tab == tab) return;
            _tab = tab;
            _scroll = Vector2.zero;
        }

        private IEnumerable<SorterLogic.Entry> Filter(IEnumerable<SorterLogic.Entry> entries)
        {
            string filter = (_search ?? "").Trim();
            return filter.Length == 0
                ? entries
                : entries.Where(en => en.DisplayName.IndexOf(filter, System.StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        private void DrawTakeTab()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Sort now", GUILayout.Width(80f))) SortNow(true);
            GUILayout.Label("Take: 1 stack · Shift: all · Ctrl: 1");
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll);
            var shown = Filter(_entries).ToList();
            foreach (var entry in shown)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(34f));
                DrawIconAndName(entry);
                if (GUILayout.Button("Take", GUILayout.Width(60f), GUILayout.Height(28f)))
                {
                    int amount = Mathf.Max(1, entry.Sample.m_shared.m_maxStackSize);
                    if (Event.current.shift) amount = entry.Total;
                    else if (Event.current.control) amount = 1;
                    TakeItem(entry, amount);
                }
                GUILayout.EndHorizontal();
            }
            if (shown.Count == 0) GUILayout.Label(_entries.Count == 0 ? "No items in range." : "No matches.");
            GUILayout.EndScrollView();
        }

        private void DrawDepositTab()
        {
            var shown = Filter(_carried).ToList();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select all", GUILayout.Width(80f))) SelectAll(shown);
            if (GUILayout.Button("Clear", GUILayout.Width(60f))) _selected.Clear();
            GUI.enabled = _selected.Count > 0;
            if (GUILayout.Button($"Sort selected ({_selected.Count})")) DepositSelected();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("Click to select · Shift+click selects everything shown · equipped gear is skipped");

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var entry in shown)
            {
                bool selected = _selected.Contains(entry.Key);
                GUILayout.BeginHorizontal(GUILayout.Height(34f));
                bool now = GUILayout.Toggle(selected, "", GUILayout.Width(20f), GUILayout.Height(32f));
                DrawIconAndName(entry);
                GUILayout.EndHorizontal();

                // Clicking anywhere on the row toggles it, too.
                var rowRect = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                    rowRect.Contains(Event.current.mousePosition))
                {
                    now = !selected;
                    Event.current.Use();
                }

                if (now == selected) continue;
                if (Event.current.shift) SelectAll(shown);
                else if (now) _selected.Add(entry.Key);
                else _selected.Remove(entry.Key);
            }
            if (shown.Count == 0) GUILayout.Label(_carried.Count == 0 ? "You're not carrying anything." : "No matches.");
            GUILayout.EndScrollView();
        }

        /// <summary>Selects every entry shown, or clears them if they are all selected already.</summary>
        private void SelectAll(List<SorterLogic.Entry> shown)
        {
            if (shown.All(en => _selected.Contains(en.Key)))
            {
                foreach (var en in shown) _selected.Remove(en.Key);
            }
            else
            {
                foreach (var en in shown) _selected.Add(en.Key);
            }
        }

        private static void DrawIconAndName(SorterLogic.Entry entry)
        {
            var iconRect = GUILayoutUtility.GetRect(32f, 32f, GUILayout.Width(32f), GUILayout.Height(32f));
            var icon = entry.Sample.GetIcon();
            if (icon != null && Event.current.type == EventType.Repaint)
            {
                var tex = icon.texture;
                var r = icon.textureRect;
                GUI.DrawTextureWithTexCoords(iconRect, tex,
                    new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height));
            }
            GUILayout.Label(entry.DisplayName, GUILayout.ExpandWidth(true), GUILayout.Height(32f));
            GUILayout.Label(entry.Total.ToString(), GUILayout.Width(50f), GUILayout.Height(32f));
        }

        private void TakeItem(SorterLogic.Entry entry, int amount)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            _sorting = true; // taking from the sorter itself must not trigger a re-sort
            try
            {
                int taken = SorterLogic.Take(_inRange, entry.Sample, amount, player.GetInventory());
                if (taken == 0)
                {
                    player.Message(MessageHud.MessageType.Center, "$inventory_full");
                }
            }
            finally
            {
                _sorting = false;
                _nextRefresh = 0f;
            }
        }
    }
}
