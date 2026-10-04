using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace EpicLootStorage
{
    /// <summary>
    /// Columns and rows per storage piece, from the config file. Admin-only, so Jötunn syncs the server's values
    /// to every client and all players see the same grid.
    ///
    /// A container reads m_width/m_height once, in Awake, when it builds its Inventory, so the prefab value alone
    /// wouldn't reach stores already standing in the world. Instead the size is applied in Container.Awake (every
    /// store, every load) and pushed to loaded stores whenever a setting changes, including a server sync.
    ///
    /// Shrinking never hides items. Rows: vanilla 1.0 already grows the inventory to fit the lowest stored row
    /// after every load (Container.UpdateRows). Columns: vanilla doesn't, so we widen to fit after each load.
    /// </summary>
    internal static class StoreSizes
    {
        public const int MaxColumns = 8;   // the vanilla container panel is 8 wide (black metal chest)
        public const int MaxRows = 20;     // the container grid scrolls; very tall grids are untested

        private sealed class Size
        {
            public ConfigEntry<int> Columns;
            public ConfigEntry<int> Rows;
        }

        private static readonly Dictionary<string, Size> SizesByPiece = new Dictionary<string, Size>();
        private static readonly AccessTools.FieldRef<Inventory, int> InventoryWidth = AccessTools.FieldRefAccess<Inventory, int>("m_width");

        public static void Bind(ConfigFile config, string pieceName, string section, int defaultColumns, int defaultRows)
        {
            var size = new Size
            {
                Columns = config.Bind(section, "Columns", defaultColumns, new ConfigDescription(
                    "Width of the storage grid. Applies to stores already built.",
                    new AcceptableValueRange<int>(1, MaxColumns),
                    new ConfigurationManagerAttributes { IsAdminOnly = true })),
                Rows = config.Bind(section, "Rows", defaultRows, new ConfigDescription(
                    "Height of the storage grid. Applies to stores already built. Never shrinks below the lowest stored item.",
                    new AcceptableValueRange<int>(1, MaxRows),
                    new ConfigurationManagerAttributes { IsAdminOnly = true })),
            };
            size.Columns.SettingChanged += (_, _) => ResizeLoadedStores();
            size.Rows.SettingChanged += (_, _) => ResizeLoadedStores();
            SizesByPiece[pieceName] = size;
        }

        private static bool TryGetSize(Container container, out int columns, out int rows)
        {
            columns = rows = 0;
            if (!SizesByPiece.TryGetValue(StorageRegistry.PrefabName(container.gameObject), out Size size))
                return false;
            columns = Mathf.Clamp(size.Columns.Value, 1, MaxColumns);
            rows = Mathf.Clamp(size.Rows.Value, 1, MaxRows);
            return true;
        }

        /// <summary>Before Awake builds the Inventory: use the configured size.</summary>
        internal static void ApplyBeforeAwake(Container container)
        {
            if (!TryGetSize(container, out int columns, out int rows))
                return;
            container.m_width = columns;
            container.m_height = rows;
        }

        /// <summary>After a load: widen if a stored item sits beyond the configured columns.</summary>
        internal static void FitColumnsAfterLoad(Container container)
        {
            Inventory inventory = container.GetInventory();
            if (inventory == null || !TryGetSize(container, out int columns, out _))
                return;
            InventoryWidth(inventory) = Mathf.Max(columns, FurthestColumn(inventory) + 1);
        }

        private static void ResizeLoadedStores()
        {
            foreach (Container container in StorageRegistry.LoadedContainers())
            {
                if (!TryGetSize(container, out int columns, out int rows))
                    continue;
                container.m_width = columns;
                container.m_height = rows;

                Inventory inventory = container.GetInventory();
                if (inventory == null)
                    continue;
                int furthestRow = -1;
                foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                    furthestRow = Mathf.Max(furthestRow, item.m_gridPos.y);
                InventoryWidth(inventory) = Mathf.Max(columns, FurthestColumn(inventory) + 1);
                inventory.SetHeight(Mathf.Max(rows, furthestRow + 1));
            }
        }

        private static int FurthestColumn(Inventory inventory)
        {
            int furthest = -1;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                furthest = Mathf.Max(furthest, item.m_gridPos.x);
            return furthest;
        }
    }

    public static class StoreSizePatches
    {
        [HarmonyPatch(typeof(Container), "Awake")]
        public static class ContainerAwake
        {
            private static void Prefix(Container __instance) => StoreSizes.ApplyBeforeAwake(__instance);
        }

        [HarmonyPatch(typeof(Container), "Load")]
        public static class ContainerLoad
        {
            private static void Postfix(Container __instance) => StoreSizes.FitColumnsAfterLoad(__instance);
        }
    }
}
