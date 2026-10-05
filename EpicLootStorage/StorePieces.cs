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
            public bool TintBody;           // body takes the stored rarity's colour (props always do)
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
                TintBody = true,
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
                Cost = new[] { new RequirementConfig("Wood", 10, 0, true), new RequirementConfig("Stone", 4, 0, true), new RequirementConfig("Flint", 2, 0, true) },
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
                if (store.TintBody)
                    StoreIndicator.TintBodyPieces.Add(store.PieceName);
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

        /// <summary>
        /// A blank sticker mount on the store's front (+Z), filled at runtime with the stored item's icon.
        /// <paramref name="x"/>/<paramref name="y"/>: centre as a fraction of <paramref name="area"/>;
        /// <paramref name="size"/>: longest side as a fraction of the area's width. It sits just proud of the body's
        /// real surface in that window, found from the meshes under <paramref name="body"/>.
        /// </summary>
        private static void AddSticker(GameObject store, Transform body, Bounds area, float x, float y, float size)
        {
            Transform root = store.transform;
            float side = area.size.x * size;
            float cx = Mathf.Lerp(area.min.x, area.max.x, x);
            float cy = Mathf.Lerp(area.min.y, area.max.y, y);
            float z = Kitbash.FrontSurfaceZ(root, body, cx - side / 2f, cx + side / 2f, cy - side / 2f, cy + side / 2f, area.max.z);

            var sticker = new GameObject(StoreIndicator.StickerName) { layer = store.layer };
            sticker.transform.SetParent(root, false);
            // Turned 180 degrees so the icon's front faces out of the store and reads the right way round.
            Kitbash.SetRootSpace(sticker.transform, root, new Vector3(cx, cy, z + StickerLift), Quaternion.Euler(0f, 180f, 0f), Vector3.one * side);
            sticker.AddComponent<MeshFilter>();
            MeshRenderer renderer = sticker.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.enabled = false;   // nothing to show until something is stored
        }

        private const float StickerLift = 0.012f;   // off the surface, so it doesn't flicker into the body

        private static GameObject Vanilla(string name)
        {
            GameObject prefab = PrefabManager.Instance.GetPrefab(name);
            if (prefab == null)
                throw new System.Exception($"vanilla prefab '{name}' not found");
            return prefab;
        }

        // ---------- the five looks ----------

        /// <summary>
        /// Essence: half-scale fermenter, two Essence bottles standing in front, sticker above the tap. The front bound includes
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
            AddSticker(store, visuals, area, x: 0.5f, y: 0.62f, size: 0.40f);
            store.transform.localScale = Vector3.one * 0.5f;
        }

        /// <summary>
        /// Dust: the barley flour sack, 1.5x tall and 1.25x that in girth, spun a quarter turn; two Dust bags pushed up
        /// against its base; sticker on the front. Body tinted (set in the store list).
        /// </summary>
        private const string BarleyFlour = "BarleyFlour";
        private const float SackHeight = 1.5f;
        private const float SackGirth = SackHeight * 1.25f;
        private const float DustBagGap = -0.35f;   // was -0.20 (about one bag-width short); more negative = closer

        private static void BuildDustStore(GameObject store)
        {
            Transform visuals = ClearBase(store);
            Kitbash.CopyItemModel(Vanilla(BarleyFlour), store.transform, visuals, yaw: 90f, new Vector3(SackGirth, SackHeight, SackGirth));
            Bounds area = Kitbash.MeshBounds(store.transform, visuals);
            Kitbash.AddBoxCollider(store, area);

            AddProps(store, visuals, area, new[]
            {
                // Set by eye from in-game screenshots (the sack's shape can't be read): its rim overhangs the base a lot,
                // so from the bounding box the bags tuck well in. Slightly more than touching, so they press into the sack.
                PropSpot.InFront("DustMagic", 0.32f, DustBagGap,  25f, 0.30f),
                PropSpot.InFront("DustMagic", 0.68f, DustBagGap, 200f, 0.30f),
            });
            AddSticker(store, visuals, area, x: 0.5f, y: 0.58f, size: 0.42f);
        }

        /// <summary>
        /// Shard: full-size forge cooler (the bucket), heaped over the rim with Shard crystals, sticker on the front.
        /// Heights are fractions of the bucket: 1.0 is the rim, so the upper layers stand proud of it.
        /// </summary>
        private static void BuildShardStore(GameObject store)
        {
            Transform visuals = ClearBase(store);
            Kitbash.CopyPart(Vanilla(ForgeCooler), "new/fi_vil_forge_coolingbath_large1", store.transform, visuals, keepColliders: true);

            Bounds area = Kitbash.MeshBounds(store.transform, visuals);
            AddProps(store, visuals, area, new[]
            {
                // Bottom layer: a ring just under the rim.
                new PropSpot("ShardMagic", 0.30f, 0.84f, 0.32f,   0f, 0.30f, tilt:  25f),
                new PropSpot("ShardMagic", 0.50f, 0.84f, 0.27f,  60f, 0.30f, tilt: -30f),
                new PropSpot("ShardMagic", 0.70f, 0.84f, 0.33f, 120f, 0.30f, tilt:  20f),
                new PropSpot("ShardMagic", 0.27f, 0.84f, 0.53f, 180f, 0.30f, tilt: -15f),
                new PropSpot("ShardMagic", 0.73f, 0.84f, 0.52f, 240f, 0.30f, tilt:  35f),
                new PropSpot("ShardMagic", 0.31f, 0.84f, 0.73f, 300f, 0.30f, tilt: -25f),
                new PropSpot("ShardMagic", 0.51f, 0.84f, 0.74f,  30f, 0.30f, tilt:  15f),
                new PropSpot("ShardMagic", 0.70f, 0.84f, 0.71f,  90f, 0.30f, tilt: -20f),
                // Middle layer: level with the rim.
                new PropSpot("ShardMagic", 0.40f, 0.95f, 0.38f, 150f, 0.30f, tilt:  40f),
                new PropSpot("ShardMagic", 0.62f, 0.95f, 0.40f, 210f, 0.30f, tilt: -35f),
                new PropSpot("ShardMagic", 0.38f, 0.95f, 0.62f, 100f, 0.30f, tilt: -30f),
                new PropSpot("ShardMagic", 0.62f, 0.95f, 0.62f, 270f, 0.30f, tilt:  30f),
                // Top: the mound above the rim.
                new PropSpot("ShardMagic", 0.45f, 1.03f, 0.48f, 330f, 0.28f, tilt: -10f),
                new PropSpot("ShardMagic", 0.56f, 1.03f, 0.55f,  45f, 0.28f, tilt:  20f),
                new PropSpot("ShardMagic", 0.50f, 1.10f, 0.50f, 200f, 0.26f, tilt:   5f),
            });
            AddSticker(store, visuals, area, x: 0.5f, y: 0.45f, size: 0.35f);
        }

        /// <summary>Reagent: vanilla barrel, lid fixed shut, Reagent jugs pushed up against its base, sticker on the front.</summary>
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
                PropSpot.AgainstFront("ReagentMagic", 0.32f, 0.01f,  30f, 0.30f, fallbackGap: -0.05f),
                PropSpot.AgainstFront("ReagentMagic", 0.68f, 0.01f, 200f, 0.26f, fallbackGap: -0.05f),
            });
            AddSticker(store, visuals, area, x: 0.5f, y: 0.6f, size: 0.40f);
        }

        /// <summary>Runestone: black metal chest at 40% (80% of the earlier half scale), lid fixed open, runestones standing inside, sticker on the front.</summary>
        private const float RunestoneChestScale = 0.4f;

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
            AddSticker(store, body != null ? body : visuals, area, x: 0.5f, y: 0.45f, size: 0.18f);
            store.transform.localScale = Vector3.one * RunestoneChestScale;
        }
    }
}
