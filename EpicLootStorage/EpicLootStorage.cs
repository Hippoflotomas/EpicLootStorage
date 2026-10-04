using BepInEx;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EpicLootStorage
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class EpicLootStorage : BaseUnityPlugin
    {
        public const string PluginGUID = "com.hippotech.epiclootstorage";
        public const string PluginName = "EpicLootStorage";
        public const string PluginVersion = "0.0.1";

        // Build step 3: one plain cloned container to test the item lock. No kitbash yet.
        public const string TestPieceName = "ELS_TestDustStore";
        private const string TestPieceBase = "piece_chest_wood";   // [unverified] vanilla wood chest; Jotunn logs an error if wrong
        private const string TestPieceToken = "piece_els_testduststore";

        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private Harmony harmony;

        private void Awake()
        {
            Jotunn.Logger.LogInfo("EpicLootStorage has landed");

            Localization.AddTranslation("English", new Dictionary<string, string>
            {
                { TestPieceToken, "Dust store (test)" },
                { TestPieceToken + "_description", "Holds Epic Loot Dust. Locks to the first rarity put in." },
            });

            // Option 3+2 for now: Dust only, locked to the first rarity. One flag to change.
            StorageRegistry.DefinePiece(TestPieceName, new StorageRule("Dust", lockToFirstItem: true));

            ApplyLockPatches();

            PrefabManager.OnVanillaPrefabsAvailable += AddPieces;
            ItemManager.OnItemsRegistered += LogEpicLootPrefabs;
        }

        /// <summary>
        /// All or nothing: with only some patches applied (say AddItem but not the load bypass), saved
        /// contents could be refused on load and lost. If any patch fails, remove them all and run as plain chests.
        /// </summary>
        private void ApplyLockPatches()
        {
            harmony = new Harmony(PluginGUID);
            var patchClasses = typeof(InventoryLockPatches).GetNestedTypes()
                .Where(t => t.IsDefined(typeof(HarmonyPatch), false));
            try
            {
                foreach (Type type in patchClasses)
                    harmony.CreateClassProcessor(type).Patch();
                Jotunn.Logger.LogInfo("[EpicLootStorage] Item lock patches applied.");
            }
            catch (Exception ex)
            {
                harmony.UnpatchSelf();
                Jotunn.Logger.LogError($"[EpicLootStorage] Item lock could not be applied on this game build, so storage pieces accept anything: {ex.Message}");
            }
        }

        private void AddPieces()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddPieces;

            var piece = new CustomPiece(TestPieceName, TestPieceBase, new PieceConfig
            {
                Name = "$" + TestPieceToken,
                Description = "$" + TestPieceToken + "_description",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Furniture,
                Requirements = new[] { new RequirementConfig("Wood", 2, 0, true) },
            });

            if (piece.PiecePrefab == null)
            {
                Jotunn.Logger.LogError($"[EpicLootStorage] Base prefab '{TestPieceBase}' not found; test store not added.");
                return;
            }

            Container container = piece.PiecePrefab.GetComponent<Container>();
            container.m_name = "$" + TestPieceToken;
            container.m_width = 8;
            container.m_height = 4;

            PieceManager.Instance.AddPiece(piece);
        }

        /// <summary>
        /// Build step 2 for free: report which of the hardcoded Epic Loot names exist in this install.
        /// </summary>
        private static void LogEpicLootPrefabs()
        {
            if (!ObjectDB.instance)
                return;

            foreach (string family in EpicLootNames.Families)
            {
                var found = EpicLootNames.PrefabsFor(family).Where(n => ObjectDB.instance.GetItemPrefab(n) != null).ToList();
                var missing = EpicLootNames.PrefabsFor(family).Except(found).ToList();
                Jotunn.Logger.LogInfo($"[EpicLootStorage] {family}: found [{string.Join(", ", found)}]" +
                                      (missing.Count > 0 ? $" missing [{string.Join(", ", missing)}]" : ""));
            }

            int shardStones = ObjectDB.instance.m_items.Count(go => go != null && go.name.EndsWith("_ShardStone"));
            Jotunn.Logger.LogInfo($"[EpicLootStorage] Socketable shardstones (<Color>_<Rarity>_ShardStone): {shardStones} prefabs.");
        }
    }
}
