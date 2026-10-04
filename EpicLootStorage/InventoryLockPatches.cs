using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EpicLootStorage
{
    /// <summary>
    /// The item lock. Checked against the Valheim 1.0 assembly (Oct 2026), every path that puts an
    /// existing item into an inventory ends in one of three Inventory.AddItem overloads that take an
    /// ItemData: (item), (item, pos) and the private (item, amount, x, y, skipValidPositionCheck).
    /// Blocking those three covers MoveAll ("place stacks"), MoveItemToThis and drag-and-drop.
    ///
    /// Callers that remove the source item only after a successful add (MoveAll, both MoveItemToThis
    /// overloads) are safe as they are. The one caller that removes first is InventoryGrid.DropItem's
    /// swap branch, so it gets its own check before anything moves.
    /// </summary>
    public static class InventoryLockPatches
    {
        /// <summary>Above zero while any inventory is loading its saved contents.</summary>
        private static int loadDepth;

        /// <summary>True if <paramref name="item"/> must not go into <paramref name="inventory"/>. Shows the reason.</summary>
        internal static bool Blocks(Inventory inventory, ItemDrop.ItemData item)
        {
            if (loadDepth > 0 || item == null)
                return false;

            StorageRule rule = StorageRegistry.RuleFor(inventory);
            if (rule == null || rule.Allows(inventory, item, out string reason))
                return false;

            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, reason);
            return true;
        }

        /// <summary>Attach a rule to each of our containers as it spawns.</summary>
        [HarmonyPatch(typeof(Container), "Awake")]
        public static class ContainerAwake
        {
            private static void Postfix(Container __instance) => StorageRegistry.TryRegister(__instance);
        }

        /// <summary>The three AddItem overloads that place an existing ItemData.</summary>
        [HarmonyPatch]
        public static class AddItem
        {
            private static IEnumerable<MethodBase> TargetMethods() =>
                AccessTools.GetDeclaredMethods(typeof(Inventory)).Where(m =>
                    m.Name == nameof(Inventory.AddItem) &&
                    m.GetParameters().FirstOrDefault()?.ParameterType == typeof(ItemDrop.ItemData));

            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
            {
                if (!Blocks(__instance, item))
                    return true;
                __result = false;
                return false;
            }
        }

        /// <summary>
        /// Drag-and-drop. When the target slot is occupied, vanilla removes the dragged item from its source
        /// first, then moves the target item back the other way, ignoring both results - so a refused add
        /// here would destroy the item. Refuse the whole drop up front if either direction is not allowed.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
        public static class GridDropItem
        {
            private static bool Prefix(Inventory ___m_inventory, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos, ref bool __result)
            {
                ItemDrop.ItemData existing = ___m_inventory.GetItemAt(pos.x, pos.y);
                if (existing == item)
                    return true;

                bool blocked = Blocks(___m_inventory, item) || (existing != null && Blocks(fromInventory, existing));
                if (!blocked)
                    return true;

                __result = false;
                return false;
            }
        }

        /// <summary>
        /// Loading saved contents goes through AddItem too. Skip all checks while it runs, or a reload could
        /// refuse items already inside (for example after a rule change).
        /// </summary>
        [HarmonyPatch]
        public static class InventoryLoad
        {
            private static IEnumerable<MethodBase> TargetMethods() =>
                AccessTools.GetDeclaredMethods(typeof(Inventory)).Where(m => m.Name == nameof(Inventory.Load));

            private static void Prefix() => loadDepth++;

            private static void Finalizer() => loadDepth--;
        }
    }
}
