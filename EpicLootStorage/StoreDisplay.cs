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
    /// Stickers: the stored item's own icon drawn flat on the store's front. The mesh is built from the icon sprite's
    /// own geometry, so it works for icons packed into texture atlases. Meshes and materials are cached per sprite and
    /// per texture and shared by every store.
    /// </summary>
    internal static class Stickers
    {
        // First one the game has wins. The cutout ones are lit like the world; Sprites/Default is always present but unlit.
        private static readonly string[] ShaderNames =
        {
            "Legacy Shaders/Transparent/Cutout/Diffuse",
            "Unlit/Transparent Cutout",
            "Sprites/Default",
        };

        private static Shader shader;
        private static bool shaderChecked;
        private static readonly Dictionary<Sprite, Mesh> Meshes = new Dictionary<Sprite, Mesh>();
        private static readonly Dictionary<Texture, Material> Materials = new Dictionary<Texture, Material>();

        /// <summary>The icon's shape, centred, longest side 1, facing local -Z (the sticker object is turned to face out).</summary>
        public static Mesh MeshFor(Sprite sprite)
        {
            if (Meshes.TryGetValue(sprite, out Mesh mesh))
                return mesh;

            Vector2[] v2 = sprite.vertices;
            var min = new Vector2(v2.Min(v => v.x), v2.Min(v => v.y));
            var max = new Vector2(v2.Max(v => v.x), v2.Max(v => v.y));
            Vector2 centre = (min + max) / 2f;
            float longest = Mathf.Max(max.x - min.x, max.y - min.y, 0.0001f);

            mesh = new Mesh { name = "els_sticker_" + sprite.name };
            mesh.vertices = v2.Select(v => (Vector3)((v - centre) / longest)).ToArray();
            mesh.uv = sprite.uv;
            mesh.triangles = sprite.triangles.Select(t => (int)t).ToArray();
            mesh.normals = Enumerable.Repeat(Vector3.back, v2.Length).ToArray();
            mesh.RecalculateBounds();
            Meshes[sprite] = mesh;
            return mesh;
        }

        public static Material MaterialFor(Texture texture)
        {
            if (Materials.TryGetValue(texture, out Material material))
                return material;

            if (!shaderChecked)
            {
                shaderChecked = true;
                shader = ShaderNames.Select(Shader.Find).FirstOrDefault(found => found != null);
                Jotunn.Logger.LogInfo(shader != null
                    ? $"[EpicLootStorage] Stickers use shader '{shader.name}'."
                    : "[EpicLootStorage] No sticker shader found; stores will show no stickers.");
            }
            if (shader == null)
                return null;

            material = new Material(shader) { name = "els_sticker_" + texture.name, mainTexture = texture, color = Color.white };
            if (material.HasProperty("_Cutoff"))
                material.SetFloat("_Cutoff", 0.5f);
            Materials[texture] = material;
            return material;
        }
    }

    /// <summary>
    /// Shows what a store holds without opening it. When it holds something: the props appear, tinted in the stored
    /// rarity's colour; the sticker shows the stored item's icon; and, for stores set to it, the body takes on the
    /// rarity colour too. Empty: props and sticker hide and the body goes back to its natural look.
    /// Placement previews (no inventory) are left alone.
    ///
    /// Props are tinted with a property block. A tinted body gets its own material copies (made once per store, freed
    /// with it) plus a faint glow, because a dark texture barely shows a colour tint. Nothing shared with vanilla
    /// pieces is ever changed. Props and the sticker are found by name, so nothing has to survive prefab cloning.
    /// </summary>
    public class StoreIndicator : MonoBehaviour
    {
        public const string PropPrefix = "prop_";
        public const string StickerName = "sticker";

        /// <summary>Stores whose body takes the rarity colour, by prefab name. Filled by StorePieces.</summary>
        internal static readonly HashSet<string> TintBodyPieces = new HashSet<string>();

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private const float BodyTintStrength = 0.7f;   // 0 = natural, 1 = full rarity colour
        private const float BodyGlow = 0.15f;          // faint, so dark bodies still read as coloured

        private Container container;
        private Inventory hooked;
        private GameObject[] props;
        private Renderer[] propRenderers;
        private MeshFilter stickerFilter;
        private MeshRenderer stickerRenderer;
        private bool tintBody;
        private Renderer[] bodyRenderers;
        private Material[][] bodyOriginals;
        private Material[][] bodyTinted;
        private MaterialPropertyBlock block;
        private string shown = "<unset>";   // stored prefab currently shown; forces the first Refresh to apply

        private void Start()
        {
            container = GetComponent<Container>();
            props = GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(PropPrefix)).Select(t => t.gameObject).ToArray();
            propRenderers = props.SelectMany(p => p.GetComponentsInChildren<Renderer>(true)).ToArray();

            Transform sticker = GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == StickerName);
            stickerFilter = sticker != null ? sticker.GetComponent<MeshFilter>() : null;
            stickerRenderer = sticker != null ? sticker.GetComponent<MeshRenderer>() : null;

            tintBody = TintBodyPieces.Contains(StorageRegistry.PrefabName(gameObject));
            bodyRenderers = GetComponentsInChildren<Renderer>(true)
                .Where(r => !(r is ParticleSystemRenderer) && !Kitbash.IsDecoration(r.transform, transform)).ToArray();
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
            string prefab = first != null ? StorageRule.PrefabName(first) ?? "" : null;
            if (prefab == shown)
                return;
            shown = prefab;

            bool show = prefab != null;
            foreach (GameObject prop in props)
                if (prop) prop.SetActive(show);
            ShowSticker(show ? first : null);

            if (!show)
            {
                if (tintBody)
                    for (int i = 0; i < bodyRenderers.Length; i++)
                        if (bodyRenderers[i]) bodyRenderers[i].sharedMaterials = bodyOriginals[i];
                return;
            }

            Color color = RarityColors.For(RarityColors.RarityOf(first));
            block.SetColor(ColorId, color);
            foreach (Renderer renderer in propRenderers)
                if (renderer) renderer.SetPropertyBlock(block);

            if (tintBody)
                TintBody(color);
        }

        private void ShowSticker(ItemDrop.ItemData item)
        {
            if (stickerRenderer == null)
                return;
            // Epic Loot materials carry one icon per colour; the item's variant picks its rarity's (what ItemData.GetIcon reads).
            Sprite[] icons = item?.m_shared.m_icons;
            Sprite icon = icons == null || icons.Length == 0 ? null : icons[Mathf.Clamp(item.m_variant, 0, icons.Length - 1)];
            Material material = icon != null ? Stickers.MaterialFor(icon.texture) : null;
            if (material == null)
            {
                stickerRenderer.enabled = false;
                return;
            }
            stickerFilter.sharedMesh = Stickers.MeshFor(icon);
            stickerRenderer.sharedMaterial = material;
            stickerRenderer.enabled = true;
        }

        private void TintBody(Color color)
        {
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
