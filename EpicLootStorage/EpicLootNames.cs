using System.Collections.Generic;

namespace EpicLootStorage
{
    /// <summary>
    /// Epic Loot prefab names, hardcoded so this mod loads without Epic Loot installed.
    /// Taken from Epic Loot's source (0.14.13, Sept 2026): crafting materials are named
    /// &lt;Family&gt;&lt;Rarity&gt;, e.g. DustMagic, EssenceRare. Not every combination is
    /// guaranteed to exist; <see cref="EpicLootStorage.LogEpicLootPrefabs"/> reports what
    /// the installed version actually has.
    /// </summary>
    internal static class EpicLootNames
    {
        public static readonly string[] Rarities = { "Magic", "Rare", "Epic", "Legendary", "Mythic", "Ancient" };

        // EtchedRunestone is left out on purpose: its stack size is 1, so it's not a bulk material.
        public static readonly string[] Families = { "Dust", "Essence", "Reagent", "Shard", "Runestone" };

        public static IEnumerable<string> PrefabsFor(string family)
        {
            foreach (string rarity in Rarities)
                yield return family + rarity;
        }
    }
}
