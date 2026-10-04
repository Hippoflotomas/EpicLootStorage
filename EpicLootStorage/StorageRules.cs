using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace EpicLootStorage
{
    /// <summary>What one kind of storage piece accepts.</summary>
    internal sealed class StorageRule
    {
        public readonly string Family;
        private readonly HashSet<string> allowedPrefabs;

        /// <summary>
        /// Option 3 from the design notes: once anything is inside, only that exact prefab is accepted.
        /// The contents are the lock, so there is nothing to save or sync. False gives option 2
        /// (any rarity of the family, mixed).
        /// </summary>
        public readonly bool LockToFirstItem;

        public StorageRule(string family, bool lockToFirstItem)
        {
            Family = family;
            allowedPrefabs = new HashSet<string>(EpicLootNames.PrefabsFor(family));
            LockToFirstItem = lockToFirstItem;
        }

        public bool Allows(Inventory inventory, ItemDrop.ItemData item, out string reason)
        {
            reason = null;
            string prefab = PrefabName(item);
            if (prefab == null || !allowedPrefabs.Contains(prefab))
            {
                reason = $"Only {Family} can be stored here";
                return false;
            }

            if (LockToFirstItem)
            {
                foreach (ItemDrop.ItemData stored in inventory.GetAllItems())
                {
                    if (PrefabName(stored) != prefab)
                    {
                        reason = "This store already holds " + Localization.instance.Localize(stored.m_shared.m_name);
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Identity is the drop prefab's name, not m_shared.m_name: Epic Loot's display names are
        /// localisation keys and can change between versions.
        /// </summary>
        public static string PrefabName(ItemDrop.ItemData item) =>
            item != null && item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
    }

    /// <summary>
    /// Which inventories are restricted. Rules are keyed by our piece's prefab name, then attached to each
    /// spawned container's Inventory instance - so another container with the same display name is never
    /// affected (the weakness of matching on Inventory.m_name).
    /// </summary>
    internal static class StorageRegistry
    {
        private static readonly Dictionary<string, StorageRule> RulesByPiece = new Dictionary<string, StorageRule>();
        private static readonly ConditionalWeakTable<Inventory, StorageRule> RulesByInventory = new ConditionalWeakTable<Inventory, StorageRule>();

        public static void DefinePiece(string pieceName, StorageRule rule) => RulesByPiece[pieceName] = rule;

        public static void TryRegister(Container container)
        {
            // Container.Awake returns early (no inventory) for placement ghosts with no ZDO.
            Inventory inventory = container.GetInventory();
            if (inventory == null)
                return;

            if (!RulesByPiece.TryGetValue(PrefabName(container.gameObject), out StorageRule rule))
                return;

            RulesByInventory.Remove(inventory);
            RulesByInventory.Add(inventory, rule);
        }

        public static StorageRule RuleFor(Inventory inventory)
        {
            if (inventory != null && RulesByInventory.TryGetValue(inventory, out StorageRule rule))
                return rule;
            return null;
        }

        private static string PrefabName(GameObject go)
        {
            string name = go.name;
            int clone = name.IndexOf("(Clone)", System.StringComparison.Ordinal);
            return clone >= 0 ? name.Substring(0, clone) : name;
        }
    }
}
