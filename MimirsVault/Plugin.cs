using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace MimirsVault
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "Amgard.MimirsVault";
        public const string PluginName = "MimirsVault";
        public const string PluginVersion = "0.4.0";
        // Never rename these: worlds store pieces by prefab name, and a renamed prefab
        // would make Valheim delete every box already built.
        public const string SorterPrefabName = "piece_itemsorter";
        public const string SmelterBoxPrefabName = "piece_smelterbox";

        internal static ConfigEntry<float> Radius;
        internal static ConfigEntry<bool> UseEmptySlots;
        internal static ConfigEntry<bool> PlaceNewItems;
        internal static ConfigEntry<bool> GroupSimilarItems;
        internal static ConfigEntry<float> SortDelay;
        internal static ConfigEntry<bool> Sparkles;
        internal static ConfigEntry<bool> Glow;
        internal static ConfigEntry<float> SmelterRadius;
        internal static ConfigEntry<float> FeedInterval;
        internal static ConfigEntry<string> NeverFeed;
        internal static ConfigEntry<bool> CatchOutput;

        private void Awake()
        {
            Radius = Config.Bind("General", "Radius", 20f,
                new ConfigDescription("Radius in meters around the sorter that is searched for chests.",
                    new AcceptableValueRange<float>(2f, 50f)));
            UseEmptySlots = Config.Bind("General", "UseEmptySlots", true,
                "When the stacks in a chest that already holds an item are full, put the rest into an empty slot of that same chest.");
            PlaceNewItems = Config.Bind("General", "PlaceNewItems", true,
                "Items that no nearby chest holds yet get a new slot in a chest (preferring chests with items of the same type). If false they stay in the sorter.");
            GroupSimilarItems = Config.Bind("General", "GroupSimilarItems", true,
                "Put new stacks in the free slot next to the same or similar items instead of the first free slot.");
            SortDelay = Config.Bind("General", "SortDelay", 0.5f,
                new ConfigDescription("Seconds to wait after the last item is dropped in before sorting.",
                    new AcceptableValueRange<float>(0f, 5f)));
            Sparkles = Config.Bind("Visuals", "Sparkles", true, "Floating sparkles around the sorter.");
            Glow = Config.Bind("Visuals", "Glow", true, "Soft light around the boxes.");

            SmelterRadius = Config.Bind("SmelterBox", "Radius", 10f,
                new ConfigDescription("Radius in meters around a Smelter Box that is searched for smelters, kilns, etc.",
                    new AcceptableValueRange<float>(2f, 30f)));
            FeedInterval = Config.Bind("SmelterBox", "FeedInterval", 1f,
                new ConfigDescription("Seconds between feeds. Each feed adds at most one ore and one fuel to each smelter.",
                    new AcceptableValueRange<float>(0.2f, 10f)));
            NeverFeed = Config.Bind("SmelterBox", "NeverFeed", "FineWood,RoundLog,ElderBark,YggdrasilWood,Blackwood",
                "Comma-separated item prefab names that are never fed into a smelter (keeps valuable wood out of the kiln).");
            CatchOutput = Config.Bind("SmelterBox", "CatchOutput", true,
                "Put what nearby smelters produce into the nearest Smelter Box with room instead of dropping it.");

            PrefabManager.OnVanillaPrefabsAvailable += AddSorterPiece;
            PrefabManager.OnVanillaPrefabsAvailable += AddSmelterBoxPiece;
            new Harmony(PluginGUID).PatchAll();
        }

        private void AddSorterPiece()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddSorterPiece;

            var config = new PieceConfig
            {
                Name = "Item Sorter",
                Description = "Drop items in to stack them into nearby chests. Open it to search and take items from every chest in range.",
                PieceTable = "Hammer",
                Category = "Furniture",
                Requirements = new[]
                {
                    new RequirementConfig("Wood", 10, 0, true),
                    new RequirementConfig("Thistle", 10, 0, true),
                    new RequirementConfig("GreydwarfEye", 5, 0, true),
                }
            };

            var piece = new CustomPiece(SorterPrefabName, "piece_chest_wood", config);
            var container = piece.PiecePrefab.GetComponent<Container>();
            container.m_name = "Item Sorter";
            piece.PiecePrefab.AddComponent<SorterBehaviour>();
            PieceManager.Instance.AddPiece(piece);
        }

        private void AddSmelterBoxPiece()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddSmelterBoxPiece;

            var config = new PieceConfig
            {
                Name = "Smelter Box",
                Description = "Feeds ore and fuel into nearby smelters, kilns and windmills, and collects what they produce.",
                PieceTable = "Hammer",
                Category = "Furniture",
                CraftingStation = "forge",
                Requirements = new[]
                {
                    new RequirementConfig("FineWood", 5, 0, true),
                    new RequirementConfig("RoundLog", 5, 0, true),
                    new RequirementConfig("GreydwarfEye", 5, 0, true),
                    new RequirementConfig("BronzeNails", 5, 0, true),
                    new RequirementConfig("SurtlingCore", 2, 0, true),
                }
            };

            var piece = new CustomPiece(SmelterBoxPrefabName, "piece_chest", config);
            piece.PiecePrefab.GetComponent<Container>().m_name = "Smelter Box";
            piece.PiecePrefab.AddComponent<SmelterBoxBehaviour>();
            PieceManager.Instance.AddPiece(piece);
        }
    }
}
