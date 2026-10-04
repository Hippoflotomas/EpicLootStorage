using BepInEx;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

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

        // Option 3+2: each store takes one family, then locks to the first rarity put in.
        private const bool LockToFirstItem = true;

        // Store size defaults, used when a config file is first created.
        private const int DefaultColumns = 5;
        private const int DefaultRows = 4;

        // TEMPORARY: the plain test chest from step 3, kept so test worlds don't lose the one already built.
        // Empty it in-game, then delete this block and its registration.
        private const string TestPieceName = "ELS_TestDustStore";
        private const string TestPieceBase = "piece_chest_wood";
        private const string TestPieceToken = "piece_els_testduststore";

        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private void Awake()
        {
            Jotunn.Logger.LogInfo("EpicLootStorage has landed");

            StorePieces.Define(Config, Localization, LockToFirstItem, DefaultColumns, DefaultRows);
            StoreIcons.Hook();

            Localization.AddTranslation("English", new Dictionary<string, string>
            {
                { TestPieceToken, "Dust store (test)" },
                { TestPieceToken + "_description", "Old test store. Empty it; it will be removed." },
            });
            StorageRegistry.DefinePiece(TestPieceName, new StorageRule("Dust", LockToFirstItem));
            StoreSizes.Bind(Config, TestPieceName, "Dust store (test)", DefaultColumns, DefaultRows);

            ApplyPatches(typeof(InventoryLockPatches), PluginGUID,
                "Item lock", "storage pieces accept anything");
            ApplyPatches(typeof(StoreSizePatches), PluginGUID + ".sizes",
                "Store sizes", "stores use their default size and ignore the config");
            ApplyPatches(typeof(StoreDisplayPatches), PluginGUID + ".display",
                "Hover text", "store hover text doesn't show the contents");

            PrefabManager.OnVanillaPrefabsAvailable += AddPieces;
            ItemManager.OnItemsRegistered += LogEpicLootPrefabs;
        }

        /// <summary>
        /// Each patch set is all or nothing, under its own Harmony ID so one failing set can be removed
        /// without the other. A partial item lock (say AddItem but not the load bypass) could refuse saved
        /// contents on load and lose them; a partial size set could hide items in columns cut off by the config.
        /// </summary>
        private static void ApplyPatches(Type patchSet, string harmonyId, string feature, string fallback)
        {
            var harmony = new Harmony(harmonyId);
            try
            {
                foreach (Type type in patchSet.GetNestedTypes().Where(t => t.IsDefined(typeof(HarmonyPatch), false)))
                    harmony.CreateClassProcessor(type).Patch();
                Jotunn.Logger.LogInfo($"[EpicLootStorage] {feature} patches applied.");
            }
            catch (Exception ex)
            {
                harmony.UnpatchSelf();
                Jotunn.Logger.LogError($"[EpicLootStorage] {feature} could not be applied on this game build, so {fallback}: {ex.Message}");
            }
        }

        private void AddPieces()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddPieces;

            StorePieces.Register(DefaultColumns, DefaultRows);

            var test = new CustomPiece(TestPieceName, TestPieceBase, new PieceConfig
            {
                Name = "$" + TestPieceToken,
                Description = "$" + TestPieceToken + "_description",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Furniture,
                Requirements = new[] { new RequirementConfig("Wood", 2, 0, true) },
            });
            if (test.PiecePrefab != null)
            {
                Container container = test.PiecePrefab.GetComponent<Container>();
                container.m_name = "$" + TestPieceToken;
                PieceManager.Instance.AddPiece(test);
            }
        }

        /// <summary>Report which of the hardcoded Epic Loot names exist in this install (once per session).</summary>
        private static void LogEpicLootPrefabs()
        {
            if (!ObjectDB.instance)
                return;
            ItemManager.OnItemsRegistered -= LogEpicLootPrefabs;

            foreach (string family in EpicLootNames.Families)
            {
                var found = EpicLootNames.PrefabsFor(family).Where(n => ObjectDB.instance.GetItemPrefab(n) != null).ToList();
                var missing = EpicLootNames.PrefabsFor(family).Except(found).ToList();
                Jotunn.Logger.LogInfo($"[EpicLootStorage] {family}: found [{string.Join(", ", found)}]" +
                                      (missing.Count > 0 ? $" missing [{string.Join(", ", missing)}]" : ""));
            }
        }
    }
}
