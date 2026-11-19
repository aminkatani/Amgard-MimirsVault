using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MimirsVault
{
    /// <summary>Finding chests around a sorter and moving items between inventories.</summary>
    internal static class SorterLogic
    {
        private static readonly MethodInfo InventoryChanged = AccessTools.Method(typeof(Inventory), "Changed");
        private static readonly MethodInfo ContainerCheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static int _layerMask;

        private static int LayerMaskPieces =>
            _layerMask != 0 ? _layerMask : (_layerMask = LayerMask.GetMask("piece", "piece_nonsolid"));

        /// <summary>Player-built chests in range that the local player may open, nearest first.</summary>
        internal static List<Container> FindContainers(Vector3 center, float radius, Container exclude)
        {
            var found = new HashSet<Container>();
            foreach (var collider in Physics.OverlapSphere(center, radius, LayerMaskPieces))
            {
                var container = collider.GetComponentInParent<Container>();
                if (container != null && container != exclude && IsUsable(container))
                {
                    found.Add(container);
                }
            }
            return found.OrderBy(c => Vector3.Distance(center, c.transform.position)).ToList();
        }

        /// <summary>Smelters, kilns, windmills, spinning wheels, ... in range, nearest first.</summary>
        internal static List<Smelter> FindSmelters(Vector3 center, float radius)
        {
            var found = new HashSet<Smelter>();
            foreach (var collider in Physics.OverlapSphere(center, radius, LayerMaskPieces))
            {
                var smelter = collider.GetComponentInParent<Smelter>();
                if (smelter == null) continue;
                var nview = smelter.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid()) found.Add(smelter);
            }
            return found.OrderBy(s => Vector3.Distance(center, s.transform.position)).ToList();
        }

        internal static bool IsUsable(Container container)
        {
            var player = Player.m_localPlayer;
            if (player == null || container.GetInventory() == null) return false;

            var nview = container.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return false;

            // Only chests built by players; skips graves, dungeon chests, carts and ships.
            var piece = container.GetComponent<Piece>();
            if (piece == null || !piece.IsPlacedByPlayer()) return false;
            if (container.GetComponent<TombStone>() != null) return false;
            if (container.GetComponentInParent<Vagon>() != null || container.GetComponentInParent<Ship>() != null) return false;

            if (container.IsInUse() && !IsOpenByLocalPlayer(container)) return false;
            if (!(bool)ContainerCheckAccess.Invoke(container, new object[] { player.GetPlayerID() })) return false;
            if (!PrivateArea.CheckAccess(container.transform.position, 0f, false, false)) return false;
            return true;
        }

        internal static bool IsOpenByLocalPlayer(Container container)
        {
            return InventoryGui.instance != null && InventoryGui.IsVisible() &&
                   CurrentContainer(InventoryGui.instance) == container;
        }

        internal static readonly AccessTools.FieldRef<InventoryGui, Container> CurrentContainer =
            AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");

        internal static bool SameKind(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            return a.m_shared.m_name == b.m_shared.m_name && a.m_quality == b.m_quality &&
                   a.m_worldLevel == b.m_worldLevel;
        }

        internal static bool Holds(Inventory inventory, ItemDrop.ItemData item)
        {
            return inventory.GetAllItems().Any(i => SameKind(i, item));
        }

        /// <summary>Take ownership so our changes to the chest are saved and synced instead of overwritten.</summary>
        internal static void ClaimOwnership(Container container)
        {
            var nview = container.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner())
            {
                nview.ClaimOwnership();
            }
        }

        /// <summary>
        /// Moves up to <paramref name="amount"/> of <paramref name="item"/> from one inventory to another.
        /// Existing stacks are topped up first, then empty slots are used if allowed. With
        /// <paramref name="groupSimilar"/> the empty slot nearest to similar items is chosen. Returns the amount moved.
        /// </summary>
        internal static int Move(Inventory from, ItemDrop.ItemData item, Inventory to, int amount, bool allowEmptySlots,
            bool groupSimilar = false)
        {
            int moved = Add(to, item, Mathf.Min(amount, item.m_stack), allowEmptySlots, groupSimilar);
            if (moved > 0) from.RemoveItem(item, moved);
            return moved;
        }

        /// <summary>Puts a freshly made item (e.g. a smelter's output) into an inventory. Returns the amount added.</summary>
        internal static int AddNew(Inventory to, ItemDrop prefab, int amount, bool groupSimilar)
        {
            var template = prefab.m_itemData.Clone();
            template.m_dropPrefab = prefab.gameObject;
            template.m_stack = amount;
            template.m_quality = 1;
            template.m_worldLevel = Game.m_worldLevel;
            return Add(to, template, amount, true, groupSimilar);
        }

        /// <summary>Adds copies of <paramref name="item"/> to an inventory without touching where it came from.</summary>
        private static int Add(Inventory to, ItemDrop.ItemData item, int amount, bool allowEmptySlots, bool groupSimilar)
        {
            int remaining = amount;
            int maxStack = Mathf.Max(1, item.m_shared.m_maxStackSize);
            int moved = 0;

            if (maxStack > 1)
            {
                foreach (var stack in to.GetAllItems())
                {
                    if (remaining <= 0) break;
                    if (!SameKind(stack, item) || stack.m_stack >= maxStack) continue;
                    int n = Mathf.Min(remaining, maxStack - stack.m_stack);
                    stack.m_stack += n;
                    remaining -= n;
                    moved += n;
                }
            }

            while (remaining > 0 && allowEmptySlots)
            {
                var slot = groupSimilar ? FindSlotNearSimilar(to, item) : FindEmptySlot(to);
                if (slot.x < 0) break;
                int n = Mathf.Min(remaining, maxStack);
                var clone = item.Clone();
                clone.m_stack = n;
                if (!to.AddItem(clone, slot)) break;
                remaining -= n;
                moved += n;
            }

            if (moved > 0) NotifyChanged(to);
            return moved;
        }

        private static Vector2i FindEmptySlot(Inventory inventory)
        {
            for (int y = 0; y < inventory.GetHeight(); y++)
            {
                for (int x = 0; x < inventory.GetWidth(); x++)
                {
                    if (inventory.GetItemAt(x, y) == null) return new Vector2i(x, y);
                }
            }
            return new Vector2i(-1, -1);
        }

        /// <summary>
        /// The empty slot closest to a stack of the same item, else to an item of the same type
        /// (materials with materials, food with food, ...), else the first free slot.
        /// </summary>
        private static Vector2i FindSlotNearSimilar(Inventory inventory, ItemDrop.ItemData item)
        {
            var items = inventory.GetAllItems();
            var same = items.Where(i => SameKind(i, item)).ToList();
            var related = same.Count > 0 ? same : items.Where(i => i.m_shared.m_itemType == item.m_shared.m_itemType).ToList();

            var best = new Vector2i(-1, -1);
            int bestScore = int.MaxValue;
            for (int y = 0; y < inventory.GetHeight(); y++)
            {
                for (int x = 0; x < inventory.GetWidth(); x++)
                {
                    if (inventory.GetItemAt(x, y) != null) continue;
                    int score = related.Count > 0
                        ? related.Min(i => Mathf.Abs(i.m_gridPos.x - x) + Mathf.Abs(i.m_gridPos.y - y))
                        : y * inventory.GetWidth() + x;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = new Vector2i(x, y);
                    }
                }
            }
            return best;
        }

        private static void NotifyChanged(Inventory inventory)
        {
            var defaults = InventoryChanged.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            InventoryChanged.Invoke(inventory, defaults);
        }

        /// <summary>Distributes the sorter's contents into nearby chests. Returns the number of items moved.</summary>
        internal static int Sort(Container sorter, out int chests)
        {
            var source = sorter.GetInventory();
            return Distribute(source, source.GetAllItems().ToList(), sorter, out chests);
        }

        /// <summary>
        /// Moves <paramref name="items"/> out of <paramref name="source"/> into the chests around the sorter.
        /// Chests that already hold an item come first; otherwise (PlaceNewItems) the chest holding the most
        /// items of the same type, then the nearest one with room. Returns the number of items moved.
        /// </summary>
        internal static int Distribute(Inventory source, List<ItemDrop.ItemData> items, Container sorter, out int chests)
        {
            var targets = FindContainers(sorter.transform.position, Plugin.Radius.Value, sorter)
                .Where(c => c.GetComponent<SorterBehaviour>() == null)
                .ToList();
            bool group = Plugin.GroupSimilarItems.Value;

            int movedTotal = 0;
            var touched = new HashSet<Container>();

            foreach (var item in items)
            {
                int left = item.m_stack;

                // Pass 1: chests that already hold this item.
                foreach (var target in targets)
                {
                    if (left <= 0) break;
                    var inv = target.GetInventory();
                    if (!Holds(inv, item)) continue;
                    left -= MoveInto(target, item, left, Plugin.UseEmptySlots.Value);
                }

                // Pass 2: a new slot in some chest, preferring chests with items of the same type.
                if (left > 0 && Plugin.PlaceNewItems.Value)
                {
                    var type = item.m_shared.m_itemType;
                    var ordered = group
                        ? targets.OrderByDescending(t => t.GetInventory().GetAllItems().Count(i => i.m_shared.m_itemType == type))
                        : (IEnumerable<Container>)targets;
                    foreach (var target in ordered)
                    {
                        if (left <= 0) break;
                        left -= MoveInto(target, item, left, true);
                    }
                }
            }
            chests = touched.Count;
            return movedTotal;

            int MoveInto(Container target, ItemDrop.ItemData moving, int amount, bool emptySlots)
            {
                ClaimOwnership(target);
                int n = Move(source, moving, target.GetInventory(), amount, emptySlots, group);
                if (n > 0)
                {
                    touched.Add(target);
                    movedTotal += n;
                }
                return n;
            }
        }

        /// <summary>One line in the sorter's browse list: every stack of the same item summed across chests.</summary>
        internal class Entry
        {
            public ItemDrop.ItemData Sample;
            public string Key;
            public string DisplayName;
            public int Total;
        }

        /// <summary>Which filter button an item falls under in the sorter window.</summary>
        internal static Category CategoryOf(ItemDrop.ItemData item)
        {
            var shared = item.m_shared;
            switch (shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                    return Category.Weapons;
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Hands:
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket:
                    return Category.Armor;
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Torch:
                    return Category.Tools;
                case ItemDrop.ItemData.ItemType.Consumable:
                    bool isFood = shared.m_food > 0f || shared.m_foodStamina > 0f || shared.m_foodEitr > 0f;
                    return isFood ? Category.Food : Category.Potions;
                case ItemDrop.ItemData.ItemType.Material:
                case ItemDrop.ItemData.ItemType.Fish:
                    return Category.Materials;
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                    return Category.Ammo;
                case ItemDrop.ItemData.ItemType.Trophy:
                    return Category.Trophies;
                default:
                    return Category.Misc;
            }
        }

        internal static string KeyOf(ItemDrop.ItemData item) =>
            $"{item.m_shared.m_name}|{item.m_quality}|{item.m_worldLevel}";

        internal static List<Entry> Aggregate(IEnumerable<Container> containers) =>
            Aggregate(containers.SelectMany(c => c.GetInventory().GetAllItems()));

        internal static List<Entry> Aggregate(IEnumerable<ItemDrop.ItemData> items)
        {
            var entries = new List<Entry>();
            foreach (var item in items)
            {
                var entry = entries.FirstOrDefault(e => SameKind(e.Sample, item));
                if (entry == null)
                {
                    string name = Localization.instance.Localize(item.m_shared.m_name);
                    if (item.m_quality > 1) name += $" (level {item.m_quality})"; // Valheim's fonts have no star glyph
                    entry = new Entry { Sample = item, Key = KeyOf(item), DisplayName = name };
                    entries.Add(entry);
                }
                entry.Total += item.m_stack;
            }
            entries.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.CurrentCultureIgnoreCase));
            return entries;
        }

        /// <summary>Pulls up to <paramref name="amount"/> of an item out of the chests into the player's inventory.</summary>
        internal static int Take(IEnumerable<Container> containers, ItemDrop.ItemData kind, int amount, Inventory destination)
        {
            int taken = 0;
            foreach (var container in containers)
            {
                if (taken >= amount) break;
                var inv = container.GetInventory();
                foreach (var item in inv.GetAllItems().Where(i => SameKind(i, kind)).ToList())
                {
                    if (taken >= amount) break;
                    ClaimOwnership(container);
                    int n = Move(inv, item, destination, amount - taken, true);
                    taken += n;
                    if (n == 0) return taken; // player inventory is full
                }
            }
            return taken;
        }
    }
}
