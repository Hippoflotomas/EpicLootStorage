using BepInEx.Configuration;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using System.Collections.Generic;
using UnityEngine;

namespace EpicLootStorage
{
    /// <summary>
    /// The five stores. Each is a vanilla container clone (so Container, Piece, WearNTear and networking come
    /// for free), with its looks swapped for kitbashed parts.
    ///
    /// Vanilla prefab names: Valheim wiki (valheim.weirdgloop.org), confirmed in-game 2026-10-04.
    /// Part paths: from the game's own prefabs, logged in-game 2026-10-04.
    /// Prop spots and sizes are first guesses - expect to tune them after seeing them in-game.
    /// </summary>
    internal static class StorePieces
    {
        // Vanilla sources
        private const string Barrel = "piece_chest_barrel";
        private const string BlackMetalChest = "piece_chest_blackmetal";
        private const string Fermenter = "fermenter";
        private const string ForgeCooler = "forge_ext5";

        // Barrel and black metal chest layout: visuals and lid states live under "New", collision on "collider".
        private const string VisualRoot = "New";
        private const string LidOpen = "New/Open";
        private const string LidClosed = "New/Closed";

        private sealed class StoreDef
        {
            public string PieceName;        // prefab name, also the rule/size key
            public string Family;           // Epic Loot family it accepts
            public string Token;            // localisation token (without $)
            public string DisplayName;
            public string Description;
            public string BasePrefab;       // vanilla container to clone
            public RequirementConfig[] Cost;
            public System.Action<GameObject> Build;   // swaps the looks
        }

        private static readonly StoreDef[] Stores =
        {
            new StoreDef
            {
                PieceName = "ELS_EssenceStore", Family = "Essence", Token = "piece_els_essencestore",
                DisplayName = "Essence Keg", Description = "A small keg that holds one kind of Essence.",
                BasePrefab = Barrel,
                Cost = new[] { new RequirementConfig("FineWood", 10, 0, true), new RequirementConfig("Bronze", 2, 0, true) },
                Build = BuildEssenceStore,
            },
            new StoreDef
            {
                PieceName = "ELS_DustStore", Family = "Dust", Token = "piece_els_duststore",
                DisplayName = "Dust Sack", Description = "A big sack that holds one kind of Dust.",
                BasePrefab = Barrel,
                Cost = new[] { new RequirementConfig("LeatherScraps", 6, 0, true), new RequirementConfig("Wood", 4, 0, true) },
                Build = BuildDustStore,
            },
            new StoreDef
            {
                PieceName = "ELS_ReagentStore", Family = "Reagent", Token = "piece_els_reagentstore",
                DisplayName = "Reagent Barrel", Description = "A sealed barrel that holds one kind of Reagent.",
                BasePrefab = Barrel,
                Cost = new[] { new RequirementConfig("Wood", 10, 0, true), new RequirementConfig("Resin", 4, 0, true) },
                Build = BuildReagentStore,
            },
            new StoreDef
            {
                PieceName = "ELS_ShardStore", Family = "Shard", Token = "piece_els_shardstore",
                DisplayName = "Shard Bucket", Description = "A cooling bucket that holds one kind of Shard.",
                BasePrefab = Barrel,
                Cost = new[] { new RequirementConfig("Wood", 10, 0, true), new RequirementConfig("Copper", 2, 0, true) },
                Build = BuildShardStore,
            },
            new StoreDef
            {
                PieceName = "ELS_RunestoneStore", Family = "Runestone", Token = "piece_els_runestonestore",
                DisplayName = "Runestone Chest", Description = "A small open black metal chest that holds one kind of Runestone.",
                BasePrefab = BlackMetalChest,
                Cost = new[] { new RequirementConfig("BlackMetal", 4, 0, true), new RequirementConfig("FineWood", 5, 0, true) },
                Build = BuildRunestoneStore,
            },
        };

        /// <summary>Called from plugin Awake: rules, sizes and names (no prefabs needed yet).</summary>
        public static void Define(ConfigFile config, CustomLocalization localization, bool lockToFirstItem, int columns, int rows)
        {
            var english = new Dictionary<string, string>();
            foreach (StoreDef store in Stores)
            {
                StorageRegistry.DefinePiece(store.PieceName, new StorageRule(store.Family, lockToFirstItem));
                StoreSizes.Bind(config, store.PieceName, store.DisplayName, columns, rows);
                english[store.Token] = store.DisplayName;
                english[store.Token + "_description"] = store.Description;
            }
            localization.AddTranslation("English", english);
        }

        /// <summary>Called on OnVanillaPrefabsAvailable.</summary>
        public static void Register(int columns, int rows)
        {
            foreach (StoreDef store in Stores)
            {
                var piece = new CustomPiece(store.PieceName, store.BasePrefab, new PieceConfig
                {
                    Name = "$" + store.Token,
                    Description = "$" + store.Token + "_description",
                    PieceTable = PieceTables.Hammer,
                    Category = PieceCategories.Furniture,
                    CraftingStation = CraftingStations.Workbench,
                    Requirements = store.Cost,
                });
                if (piece.PiecePrefab == null)
                {
                    Jotunn.Logger.LogError($"[EpicLootStorage] Base prefab '{store.BasePrefab}' not found; {store.PieceName} not added.");
                    continue;
                }

                Container container = piece.PiecePrefab.GetComponent<Container>();
                container.m_name = "$" + store.Token;
                container.m_width = columns;   // overridden from the config in Container.Awake
                container.m_height = rows;

                try
                {
                    store.Build(piece.PiecePrefab);
                }
                catch (System.Exception ex)
                {
                    // Looks are cosmetic: a failure leaves a plain clone that still works as a store.
                    Jotunn.Logger.LogError($"[EpicLootStorage] Could not build the look for {store.PieceName}: {ex}");
                }

                piece.PiecePrefab.AddComponent<StoreIndicator>();
                PieceManager.Instance.AddPiece(piece);
                StoreIcons.Add(piece.PiecePrefab, store.Family + "Magic");   // badge: the family's material icon
            }
        }

        // ---------- shared steps ----------

        /// <summary>Empty the base clone's looks and collision, and stop its lid from swapping.</summary>
        private static Transform ClearBase(GameObject store)
        {
            Container container = store.GetComponent<Container>();
            container.m_open = null;    // vanilla null-checks these, so the lid just stops changing
            container.m_closed = null;

            Kitbash.DestroyAllNamed(store, "floor_2x2_snow");
            Kitbash.DestroyAllNamed(store, "collider");
            Transform visuals = store.transform.Find(VisualRoot);   // kept: WearNTear points at it
            Kitbash.DestroyChildren(visuals);
            Kitbash.FixLodGroups(store, removeGroups: true);

            // Break-apart fragments would point at the destroyed parts; null makes vanilla skip them.
            WearNTear wear = store.GetComponent<WearNTear>();
            if (wear != null)
                wear.m_fragmentRoots = null;
            return visuals;
        }

        private static void AddProps(GameObject store, Transform visuals, Bounds area, IEnumerable<PropSpot> spots)
        {
            int missing = 0;
            foreach (PropSpot spot in spots)
                if (!Props.Add(store.transform, visuals, area, spot))
                    missing++;
            if (missing > 0)
                Jotunn.Logger.LogWarning($"[EpicLootStorage] {store.name}: {missing} prop(s) skipped (Epic Loot item models not found).");
        }

        private static GameObject Vanilla(string name)
        {
            GameObject prefab = PrefabManager.Instance.GetPrefab(name);
            if (prefab == null)
                throw new System.Exception($"vanilla prefab '{name}' not found");
            return prefab;
        }

        // ---------- the five looks ----------

        /// <summary>
        /// Essence: half-scale fermenter, two Essence bottles standing in front. The fermenter's front bound includes
        /// its tap and the barrel curves away at the base, so the bottles need a large negative gap to nearly touch it.
        /// </summary>
        private static void BuildEssenceStore(GameObject store)
        {
            Transform visuals = ClearBase(store);
            GameObject fermenter = Vanilla(Fermenter);
            Kitbash.CopyPart(fermenter, "New/barrel", store.transform, visuals, keepColliders: true);
            Kitbash.CopyPart(fermenter, "_top/default", store.transform, visuals, keepColliders: false);

            Bounds area = Kitbash.MeshBounds(store.transform, visuals);
            AddProps(store, visuals, area, new[]
            {
                PropSpot.InFront("EssenceMagic", 0.35f, -0.30f,  15f, 0.40f),
                PropSpot.InFront("EssenceMagic", 0.65f, -0.30f, 160f, 0.40f),
            });
            store.transform.localScale = Vector3.one * 0.5f;
        }

        /// <summary>Dust: the barley flour sack at 1.5x, two Dust bags on the ground in front of it.</summary>
        private const string BarleyFlour = "BarleyFlour";

        private static void BuildDustStore(GameObject store)
        {
            Transform visuals = ClearBase(store);
            Kitbash.CopyItemModel(Vanilla(BarleyFlour), store.transform, visuals, scale: 1.5f);
            Bounds area = Kitbash.MeshBounds(store.transform, visuals);
            Kitbash.AddBoxCollider(store, area);

            // The sack is widest at the top, so a small negative gap tucks the bags in almost touching at the base.
            AddProps(store, visuals, area, new[]
            {
                PropSpot.InFront("DustMagic", 0.32f, -0.06f,  25f, 0.30f),
                PropSpot.InFront("DustMagic", 0.68f, -0.06f, 200f, 0.30f),
            });
        }

        /// <summary>Shard: full-size forge cooler (the bucket) with Shard crystals inside.</summary>
        private static void BuildShardStore(GameObject store)
        {
            Transform visuals = ClearBase(store);
            Kitbash.CopyPart(Vanilla(ForgeCooler), "new/fi_vil_forge_coolingbath_large1", store.transform, visuals, keepColliders: true);

            Bounds area = Kitbash.MeshBounds(store.transform, visuals);
            AddProps(store, visuals, area, new[]
            {
                // A heap: a low layer spread across the tub, then a smaller layer on top.
                new PropSpot("ShardMagic", 0.30f, 0.70f, 0.32f,   0f, 0.30f, tilt:  25f),
                new PropSpot("ShardMagic", 0.50f, 0.70f, 0.28f,  60f, 0.30f, tilt: -30f),
                new PropSpot("ShardMagic", 0.70f, 0.70f, 0.34f, 120f, 0.30f, tilt:  20f),
                new PropSpot("ShardMagic", 0.28f, 0.70f, 0.55f, 180f, 0.30f, tilt: -15f),
                new PropSpot("ShardMagic", 0.72f, 0.70f, 0.52f, 240f, 0.30f, tilt:  35f),
                new PropSpot("ShardMagic", 0.32f, 0.70f, 0.74f, 300f, 0.30f, tilt: -25f),
                new PropSpot("ShardMagic", 0.52f, 0.70f, 0.72f,  30f, 0.30f, tilt:  15f),
                new PropSpot("ShardMagic", 0.70f, 0.70f, 0.72f,  90f, 0.30f, tilt: -20f),
                new PropSpot("ShardMagic", 0.42f, 0.77f, 0.45f, 150f, 0.28f, tilt:  40f),
                new PropSpot("ShardMagic", 0.60f, 0.77f, 0.48f, 210f, 0.28f, tilt: -35f),
                new PropSpot("ShardMagic", 0.50f, 0.77f, 0.62f, 270f, 0.28f, tilt:  30f),
                new PropSpot("ShardMagic", 0.50f, 0.83f, 0.52f, 330f, 0.26f, tilt: -10f),
            });
        }

        /// <summary>Reagent: vanilla barrel, lid fixed shut, Reagent jugs standing in front.</summary>
        private static void BuildReagentStore(GameObject store)
        {
            Container container = store.GetComponent<Container>();
            container.m_open = null;
            container.m_closed = null;
            Kitbash.DestroyPath(store, LidOpen);   // keep the closed lid only
            Kitbash.FixLodGroups(store, removeGroups: false);

            Transform visuals = store.transform.Find(VisualRoot);
            Bounds area = Kitbash.MeshBounds(store.transform, visuals);
            AddProps(store, visuals, area, new[]
            {
                PropSpot.InFront("ReagentMagic", 0.32f, -0.05f,  30f, 0.30f),
                PropSpot.InFront("ReagentMagic", 0.68f, -0.05f, 200f, 0.26f),
            });
        }

        /// <summary>Runestone: half-scale black metal chest, lid fixed open, runestones standing inside.</summary>
        private static void BuildRunestoneStore(GameObject store)
        {
            Container container = store.GetComponent<Container>();
            container.m_open = null;
            container.m_closed = null;
            Kitbash.DestroyPath(store, LidClosed);   // keep the open lid only
            Kitbash.FixLodGroups(store, removeGroups: false);

            Transform visuals = store.transform.Find(VisualRoot);
            Transform body = store.transform.Find("New/blackmetalchest");
            Bounds area = Kitbash.MeshBounds(store.transform, body != null ? body : visuals);
            AddProps(store, visuals, area, new[]
            {
                // Stood nearly upright in a row along the chest's long side, kept clear of the walls.
                new PropSpot("RunestoneMagic", 0.36f, 0.45f, 0.50f, 0f, 0.42f, tilt: -70f),
                new PropSpot("RunestoneMagic", 0.50f, 0.45f, 0.50f, 0f, 0.42f, tilt: -75f),
                new PropSpot("RunestoneMagic", 0.64f, 0.45f, 0.50f, 0f, 0.42f, tilt: -70f),
            });
            store.transform.localScale = Vector3.one * 0.5f;
        }
    }
}
