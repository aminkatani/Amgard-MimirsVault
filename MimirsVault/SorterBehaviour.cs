using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MimirsVault
{
    /// <summary>
    /// Lives on every Item Sorter chest. Sorts dropped-in items into nearby chests and, while the
    /// local player has it open, feeds <see cref="SorterPanel"/> with everything stored in range.
    /// </summary>
    public class SorterBehaviour : MonoBehaviour
    {
        private const float RefreshInterval = 0.5f;

        private Container _container;
        private Inventory _inventory;
        private bool _wasOpen;
        private bool _sorting;
        private float _sortAt = -1f;
        private int _countAfterLastSort = -1;
        private float _nextRefresh;

        internal List<Container> InRange { get; private set; } = new List<Container>();
        internal List<SorterLogic.Entry> Stored { get; private set; } = new List<SorterLogic.Entry>();
        internal List<SorterLogic.Entry> Carried { get; private set; } = new List<SorterLogic.Entry>();

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
            if (_wasOpen) SorterPanel.Hide(this);
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
                Refresh();
                SorterPanel.Show(this);
            }
            else if (!open && _wasOpen)
            {
                SorterPanel.Hide(this);
                if (_sortAt > 0f) SortNow(); // closed before the delay elapsed
            }
            _wasOpen = open;

            if (_sortAt > 0f && Time.time >= _sortAt) SortNow();

            if (open && Time.time >= _nextRefresh)
            {
                Refresh();
                SorterPanel.DataChanged(this);
            }
        }

        private void Refresh()
        {
            _nextRefresh = Time.time + RefreshInterval;
            InRange = SorterLogic.FindContainers(transform.position, Plugin.Radius.Value, null);
            Stored = SorterLogic.Aggregate(InRange);
            Carried = SorterLogic.Aggregate(CarriedItems());
        }

        /// <summary>Everything in the player's inventory except equipped gear.</summary>
        private static IEnumerable<ItemDrop.ItemData> CarriedItems()
        {
            var player = Player.m_localPlayer;
            if (player == null) return Enumerable.Empty<ItemDrop.ItemData>();
            return player.GetInventory().GetAllItems().Where(i => !i.m_equipped);
        }

        internal void SortNow(bool explicitRequest = false)
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

        internal void Deposit(ICollection<string> keys)
        {
            var player = Player.m_localPlayer;
            if (player == null || keys.Count == 0) return;

            var playerInv = player.GetInventory();
            var items = CarriedItems().Where(i => keys.Contains(SorterLogic.KeyOf(i))).ToList();

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
            }
            finally
            {
                _sorting = false;
                _nextRefresh = 0f;
            }
        }

        internal void Take(SorterLogic.Entry entry, int amount)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            _sorting = true; // taking from the sorter itself must not trigger a re-sort
            try
            {
                int taken = SorterLogic.Take(InRange, entry.Sample, amount, player.GetInventory());
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
    }
}
