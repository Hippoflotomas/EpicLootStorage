using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EpicLootStorage
{
    /// <summary>
    /// Epic Loot's rarity colours. Read from Epic Loot's own config (section "Item Colors"), so a player who
    /// recoloured rarities sees their colours here too; Epic Loot's defaults if it isn't installed.
    /// Defaults and the colour names come from Epic Loot's source (0.14.13: ELConfig.cs, EpicLoot.cs).
    /// </summary>
    internal static class RarityColors
    {
        private const string EpicLootGuid = "randyknapp.mods.epicloot";

        private static readonly Dictionary<string, string> Named = new Dictionary<string, string>
        {
            { "Red", "#ff4545" }, { "Orange", "#ffac59" }, { "Yellow", "#ffff75" }, { "Green", "#80fa70" },
            { "Teal", "#18e7a9" }, { "Blue", "#00abff" }, { "Indigo", "#709bba" }, { "Purple", "#d078ff" },
            { "Pink", "#ff63d6" }, { "Gray", "#dbcadb" },
        };

        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { "Magic", "Blue" }, { "Rare", "Yellow" }, { "Epic", "Purple" },
            { "Legendary", "Teal" }, { "Mythic", "Orange" }, { "Ancient", "Red" },
        };

        public static Color For(string rarity)
        {
            if (rarity == null || !Defaults.TryGetValue(rarity, out string value))
                return Color.white;

            if (Chainloader.PluginInfos.TryGetValue(EpicLootGuid, out var info) && info.Instance != null &&
                info.Instance.Config.TryGetEntry(new ConfigDefinition("Item Colors", rarity + " Rarity Color"), out ConfigEntry<string> entry))
                value = entry.Value;

            string hex = value.StartsWith("#") ? value : Named.TryGetValue(value, out string named) ? named : "#ffffff";
            return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.white;
        }

        /// <summary>The rarity is the end of the prefab name: DustRare -> Rare.</summary>
        public static string RarityOf(ItemDrop.ItemData item)
        {
            string prefab = StorageRule.PrefabName(item);
            return prefab == null ? null : EpicLootNames.Rarities.FirstOrDefault(r => prefab.EndsWith(r));
        }
    }

    /// <summary>
    /// Shows what a store holds without opening it. When it holds something, the props appear and the props and
    /// the store's own body take on the stored rarity's colour; empty, the props hide and the body goes back to its
    /// natural look. Placement previews (no inventory) are left alone.
    ///
    /// Props are tinted with a property block. The body gets its own material copies (made once per store, freed
    /// with it), because a dark texture barely shows a colour tint - the copies also get a faint glow in the colour
    /// where the shader supports one. Nothing shared with vanilla pieces is ever changed.
    /// Props are found by name, so nothing has to survive prefab cloning.
    /// </summary>
    public class StoreIndicator : MonoBehaviour
    {
        public const string PropPrefix = "prop_";
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private const float BodyTintStrength = 0.7f;   // 0 = natural, 1 = full rarity colour
        private const float BodyGlow = 0.15f;          // faint, so dark bodies still read as coloured

        private Container container;
        private Inventory hooked;
        private GameObject[] props;
        private Renderer[] propRenderers;
        private Renderer[] bodyRenderers;
        private Material[][] bodyOriginals;
        private Material[][] bodyTinted;
        private MaterialPropertyBlock block;
        private string shownRarity = "<unset>";   // forces the first Refresh to apply

        private void Start()
        {
            container = GetComponent<Container>();
            props = GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(PropPrefix)).Select(t => t.gameObject).ToArray();
            propRenderers = props.SelectMany(p => p.GetComponentsInChildren<Renderer>(true)).ToArray();
            bodyRenderers = GetComponentsInChildren<Renderer>(true)
                .Where(r => !(r is ParticleSystemRenderer) && !propRenderers.Contains(r)).ToArray();
            bodyOriginals = bodyRenderers.Select(r => r.sharedMaterials).ToArray();
            block = new MaterialPropertyBlock();

            hooked = container != null ? container.GetInventory() : null;
            if (hooked != null)
                hooked.m_onChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (hooked != null)
                hooked.m_onChanged -= Refresh;
            if (bodyTinted != null)
                foreach (Material m in bodyTinted.SelectMany(ms => ms))
                    if (m) Destroy(m);
        }

        private void Refresh()
        {
            if (hooked == null)
                return;   // placement preview

            ItemDrop.ItemData first = hooked.GetAllItems().FirstOrDefault();
            string rarity = first != null ? RarityColors.RarityOf(first) ?? "" : null;
            if (rarity == shownRarity)
                return;
            shownRarity = rarity;

            bool show = rarity != null;
            foreach (GameObject prop in props)
                if (prop) prop.SetActive(show);

            if (!show)
            {
                for (int i = 0; i < bodyRenderers.Length; i++)
                    if (bodyRenderers[i]) bodyRenderers[i].sharedMaterials = bodyOriginals[i];
                return;
            }

            Color color = RarityColors.For(rarity);
            block.SetColor(ColorId, color);
            foreach (Renderer renderer in propRenderers)
                if (renderer) renderer.SetPropertyBlock(block);

            if (bodyTinted == null)
                bodyTinted = bodyOriginals.Select(ms => ms.Select(m => m ? new Material(m) { name = m.name + "_els" } : null).ToArray()).ToArray();
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                for (int j = 0; j < bodyTinted[i].Length; j++)
                {
                    Material original = bodyOriginals[i][j];
                    Material tinted = bodyTinted[i][j];
                    if (tinted == null)
                        continue;
                    if (tinted.HasProperty(ColorId))
                        tinted.SetColor(ColorId, original.GetColor(ColorId) * Color.Lerp(Color.white, color, BodyTintStrength));
                    if (tinted.HasProperty(EmissionId))
                    {
                        tinted.EnableKeyword("_EMISSION");
                        tinted.SetColor(EmissionId, color * BodyGlow);
                    }
                }
                if (bodyRenderers[i]) bodyRenderers[i].sharedMaterials = bodyTinted[i];
            }
        }
    }

    public static class StoreDisplayPatches
    {
        /// <summary>Hover text: what's inside, in its rarity colour, and how many.</summary>
        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        public static class ContainerHoverText
        {
            private static void Postfix(Container __instance, ref string __result)
            {
                Inventory inventory = __instance.GetInventory();
                if (StorageRegistry.RuleFor(inventory) == null)
                    return;

                ItemDrop.ItemData first = inventory.GetAllItems().FirstOrDefault();
                if (first == null)
                {
                    __result += "\n<color=#a0a0a0>Empty</color>";
                    return;
                }

                string prefab = StorageRule.PrefabName(first);
                int count = inventory.GetAllItems().Where(i => StorageRule.PrefabName(i) == prefab).Sum(i => i.m_stack);
                string rarity = RarityColors.RarityOf(first);
                string hex = ColorUtility.ToHtmlStringRGB(RarityColors.For(rarity));
                string name = Localization.instance.Localize(first.m_shared.m_name);
                if (rarity != null && !name.Contains(rarity))
                    name = rarity + " " + name;   // Epic Loot's material names don't always include the rarity
                __result += $"\n<color=#{hex}>{name}</color> x{count}";
            }
        }
    }
}
