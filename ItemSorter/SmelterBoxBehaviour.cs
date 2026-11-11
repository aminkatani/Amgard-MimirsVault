using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ItemSorter
{
    /// <summary>
    /// Lives on every Smelter Box. Feeds ore and fuel from the box into nearby smelters (smelter, blast furnace,
    /// kiln, windmill, spinning wheel, ...) and, through <see cref="SmelterSpawnPatch"/>, catches what they produce.
    /// </summary>
    public class SmelterBoxBehaviour : MonoBehaviour
    {
        internal static readonly List<SmelterBoxBehaviour> All = new List<SmelterBoxBehaviour>();

        private static readonly Func<Smelter, float> GetFuel =
            AccessTools.MethodDelegate<Func<Smelter, float>>(AccessTools.Method(typeof(Smelter), "GetFuel"));
        private static readonly Func<Smelter, int> GetQueueSize =
            AccessTools.MethodDelegate<Func<Smelter, int>>(AccessTools.Method(typeof(Smelter), "GetQueueSize"));
        private static readonly Func<Smelter, string, bool> IsItemAllowed =
            AccessTools.MethodDelegate<Func<Smelter, string, bool>>(
                AccessTools.Method(typeof(Smelter), "IsItemAllowed", new[] { typeof(string) }));

        private Container _container;
        private ZNetView _nview;
        private VisualHandle _visuals;
        private float _nextFeed;
        private bool _active;

        internal Container Container => _container;

        private void Awake()
        {
            _container = GetComponent<Container>();
            _nview = GetComponent<ZNetView>();
            _visuals = SorterVisuals.Apply(gameObject, () => Palette.Ember);
        }

        private void OnEnable() => All.Add(this);

        private void OnDisable() => All.Remove(this);

        private void Update()
        {
            _visuals?.Tick(_active);

            if (_nview == null || !_nview.IsValid() || _container == null || _container.GetInventory() == null) return;
            if (Time.time < _nextFeed) return;
            _nextFeed = Time.time + Plugin.FeedInterval.Value;

            var smelters = SorterLogic.FindSmelters(transform.position, Plugin.SmelterRadius.Value);
            _active = smelters.Any(s => s.IsActive());

            // Only the client that owns the box feeds, so items aren't fed twice in multiplayer.
            if (!_nview.IsOwner()) return;
            if (_container.IsInUse() && !SorterLogic.IsOpenByLocalPlayer(_container)) return;
            Feed(smelters);
        }

        /// <summary>Adds at most one ore and one fuel per smelter, so materials spread evenly across them.</summary>
        private void Feed(List<Smelter> smelters)
        {
            var inventory = _container.GetInventory();
            var neverFeed = new HashSet<string>(
                Plugin.NeverFeed.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var smelter in smelters)
            {
                var nview = smelter.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                if (smelter.m_fuelItem != null && smelter.m_maxFuel > 0 && GetFuel(smelter) < smelter.m_maxFuel - 1)
                {
                    string fuelName = smelter.m_fuelItem.m_itemData.m_shared.m_name;
                    var fuel = inventory.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == fuelName);
                    if (fuel != null)
                    {
                        inventory.RemoveItem(fuel, 1);
                        nview.InvokeRPC("RPC_AddFuel");
                    }
                }

                if (smelter.m_maxOre > 0 && GetQueueSize(smelter) < smelter.m_maxOre)
                {
                    var ore = inventory.GetAllItems().FirstOrDefault(i =>
                        i.m_dropPrefab != null && !neverFeed.Contains(i.m_dropPrefab.name) &&
                        IsItemAllowed(smelter, i.m_dropPrefab.name));
                    if (ore != null)
                    {
                        string oreName = ore.m_dropPrefab.name;
                        inventory.RemoveItem(ore, 1);
                        nview.InvokeRPC("RPC_AddOre", oreName, false);
                    }
                }
            }
        }

        /// <summary>
        /// Stores a smelter's output in the nearest Smelter Box with room, then the next one, and so on.
        /// Returns how many fitted; the caller drops the rest on the ground as usual.
        /// </summary>
        internal static int Store(Vector3 smelterPosition, ItemDrop output, int amount)
        {
            float radius = Plugin.SmelterRadius.Value;
            var boxes = All
                .Where(b => b != null && b._container != null &&
                            Vector3.Distance(b.transform.position, smelterPosition) <= radius &&
                            SorterLogic.IsUsable(b._container))
                .OrderBy(b => Vector3.Distance(b.transform.position, smelterPosition))
                .ToList();

            int stored = 0;
            foreach (var box in boxes)
            {
                if (stored >= amount) break;
                SorterLogic.ClaimOwnership(box._container);
                stored += SorterLogic.AddNew(box._container.GetInventory(), output, amount - stored,
                    Plugin.GroupSimilarItems.Value);
            }
            return stored;
        }
    }

    /// <summary>Catches finished bars, coal, flour, ... before the smelter drops them on the ground.</summary>
    [HarmonyPatch(typeof(Smelter), "Spawn")]
    internal static class SmelterSpawnPatch
    {
        private static readonly Func<Smelter, string, Smelter.ItemConversion> GetItemConversion =
            AccessTools.MethodDelegate<Func<Smelter, string, Smelter.ItemConversion>>(
                AccessTools.Method(typeof(Smelter), "GetItemConversion"));

        // Spawn(string ore, int stack): runs only on the client that owns the smelter.
        private static bool Prefix(Smelter __instance, string __0, ref int __1)
        {
            if (!Plugin.CatchOutput.Value || __1 <= 0) return true;

            var conversion = GetItemConversion(__instance, __0);
            if (conversion == null || conversion.m_to == null) return true;

            int stored = SmelterBoxBehaviour.Store(__instance.transform.position, conversion.m_to, __1);
            if (stored <= 0) return true;
            if (stored < __1)
            {
                __1 -= stored; // every box is full: drop only what's left
                return true;
            }

            var at = __instance.m_outputPoint != null ? __instance.m_outputPoint : __instance.transform;
            __instance.m_produceEffects.Create(at.position, at.rotation);
            return false;
        }
    }
}
