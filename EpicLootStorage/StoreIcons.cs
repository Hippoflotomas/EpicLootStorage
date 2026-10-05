using Jotunn.Managers;
using System.Collections.Generic;
using UnityEngine;

namespace EpicLootStorage
{
    /// <summary>
    /// Build-menu icons: Jötunn's RenderManager photographs each store's own prefab (so the icon always matches the
    /// kitbashed look), then the matching Epic Loot material icon is stamped in the bottom-right corner as a badge.
    /// Runs once, after pieces are registered. If anything fails, the store keeps its vanilla icon.
    /// </summary>
    internal static class StoreIcons
    {
        private const int Size = 128;
        private const float BadgeFraction = 0.45f;

        private static readonly List<(GameObject prefab, string badgeItem)> Pending = new List<(GameObject, string)>();
        private static bool done;

        public static void Add(GameObject piecePrefab, string badgeItem) => Pending.Add((piecePrefab, badgeItem));

        public static void Hook() => PieceManager.OnPiecesRegistered += RenderAll;

        private static void RenderAll()
        {
            if (done)
                return;
            done = true;
            PieceManager.OnPiecesRegistered -= RenderAll;

            foreach ((GameObject prefab, string badgeItem) in Pending)
            {
                try
                {
                    Piece piece = prefab.GetComponent<Piece>();
                    Sprite render = RenderManager.Instance.Render(new RenderManager.RenderRequest(prefab)
                    {
                        Width = Size,
                        Height = Size,
                        Rotation = RenderManager.IsometricRotation,
                        UseCache = true,    // Jotunn re-renders when the mod version changes
                    });
                    if (render == null)
                    {
                        Jotunn.Logger.LogWarning($"[EpicLootStorage] Icon render failed for {prefab.name}; keeping the vanilla icon.");
                        continue;
                    }

                    Sprite badge = BadgeFor(badgeItem);
                    piece.m_icon = badge != null ? Compose(render, badge) : render;
                }
                catch (System.Exception ex)
                {
                    Jotunn.Logger.LogWarning($"[EpicLootStorage] Icon for {prefab.name} failed, keeping the vanilla icon: {ex.Message}");
                }
            }
        }

        private static Sprite BadgeFor(string itemPrefab)
        {
            ItemDrop item = PrefabManager.Instance.GetPrefab(itemPrefab)?.GetComponent<ItemDrop>();
            Sprite[] icons = item != null ? item.m_itemData.m_shared.m_icons : null;
            return icons != null && icons.Length > 0 ? icons[0] : null;
        }

        /// <summary>
        /// Draw both sprites into a render texture and read it back. Drawing on the GPU works even when the source
        /// textures aren't CPU-readable (icons packed into atlases usually aren't).
        /// </summary>
        private static Sprite Compose(Sprite baseIcon, Sprite badge)
        {
            RenderTexture rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, Size, Size, 0);   // GUI-style: origin top-left

            Draw(baseIcon, new Rect(0, 0, Size, Size));
            float b = Size * BadgeFraction;
            Draw(badge, new Rect(Size - b, Size - b, b, b));

            GL.PopMatrix();
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
        }

        private static void Draw(Sprite sprite, Rect destination)
        {
            Texture texture = sprite.texture;
            Rect r = sprite.textureRect;
            var uv = new Rect(r.x / texture.width, r.y / texture.height, r.width / texture.width, r.height / texture.height);
            Graphics.DrawTexture(destination, texture, uv, 0, 0, 0, 0);
        }
    }
}
