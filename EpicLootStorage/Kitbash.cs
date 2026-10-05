using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EpicLootStorage
{
    /// <summary>
    /// Helpers for building a store's look out of other prefabs' parts. All positions are in the store's
    /// root space, so a part lands where it sat in its source no matter how the parents are arranged.
    /// Setup runs on prefabs that are not in a scene, so everything is removed with DestroyImmediate.
    /// </summary>
    internal static class Kitbash
    {
        /// <summary>Copy a source child (by path) under <paramref name="parent"/>, keeping its placement relative to the source root.</summary>
        public static GameObject CopyPart(GameObject source, string path, Transform root, Transform parent, bool keepColliders)
        {
            Transform part = source.transform.Find(path);
            if (part == null)
            {
                Jotunn.Logger.LogWarning($"[EpicLootStorage] Kitbash part '{path}' not found in '{source.name}'.");
                return null;
            }
            return CopyTransform(part, source.transform, root, parent, keepColliders);
        }

        /// <summary>Copy every direct child of the source with this name (some prefabs have several "collider" objects).</summary>
        public static void CopyAllNamed(GameObject source, string name, Transform root, Transform parent, bool keepColliders)
        {
            foreach (Transform child in source.transform.Cast<Transform>().Where(c => c.name == name).ToList())
                CopyTransform(child, source.transform, root, parent, keepColliders);
        }

        private static GameObject CopyTransform(Transform part, Transform sourceRoot, Transform root, Transform parent, bool keepColliders)
        {
            Matrix4x4 inSource = sourceRoot.worldToLocalMatrix * part.localToWorldMatrix;
            GameObject copy = Object.Instantiate(part.gameObject, parent, false);
            copy.name = part.name;
            SetRootSpace(copy.transform, root, inSource.GetColumn(3), inSource.rotation, inSource.lossyScale);
            Strip(copy, keepColliders);
            return copy;
        }

        /// <summary>
        /// Copy a vanilla item's model (every child that renders, plus the root's own mesh if it has one), turn it by
        /// <paramref name="yaw"/>, scale it per axis in the store's own axes, and seat it on the ground centred on the
        /// store. Items sit on the "item" layer; the copy takes the store's.
        /// </summary>
        public static void CopyItemModel(GameObject item, Transform root, Transform parent, float yaw, Vector3 scale)
        {
            var model = new GameObject("model");
            model.transform.SetParent(parent, false);
            SetRootSpace(model.transform, root, Vector3.zero, Quaternion.identity, Vector3.one);   // axes = store axes

            MeshFilter rootFilter = item.GetComponent<MeshFilter>();
            MeshRenderer rootRenderer = item.GetComponent<MeshRenderer>();
            if (rootFilter != null && rootRenderer != null)
            {
                var self = new GameObject(item.name + "_mesh");
                self.transform.SetParent(model.transform, false);
                self.AddComponent<MeshFilter>().sharedMesh = rootFilter.sharedMesh;
                self.AddComponent<MeshRenderer>().sharedMaterials = rootRenderer.sharedMaterials;
            }
            foreach (Transform child in item.transform)
                if (child.GetComponentInChildren<Renderer>(true) != null && child.GetComponentInChildren<ParticleSystem>(true) == null)
                    CopyTransform(child, item.transform, root, model.transform, keepColliders: false);

            SetLayer(model, root.gameObject.layer);

            // Turn and scale the whole assembly (children move with it), then seat it.
            SetRootSpace(model.transform, root, Vector3.zero, Quaternion.Euler(0f, yaw, 0f), scale);
            Bounds b = MeshBounds(root, model.transform);
            SetRootSpace(model.transform, root, new Vector3(-b.center.x, -b.min.y, -b.center.z), Quaternion.Euler(0f, yaw, 0f), scale);
        }

        /// <summary>
        /// How far forward (+Z, store root space) the store's surface reaches inside an x/y window - so things can be
        /// placed against a curved body rather than its bounding box. Reads mesh vertices; meshes the game doesn't let
        /// us read are skipped. If no vertex falls in the window it widens to the full height, then falls back to
        /// <paramref name="fallback"/> (normally the bounds' front face).
        /// </summary>
        public static float FrontSurfaceZ(Transform root, Transform under, float xMin, float xMax, float yMin, float yMax, float fallback) =>
            FrontSurfaceZ(root, under, xMin, xMax, yMin, yMax, fallback, out _);

        /// <param name="exact">True only if vertices inside the window were found (not widened, not the fallback).</param>
        public static float FrontSurfaceZ(Transform root, Transform under, float xMin, float xMax, float yMin, float yMax, float fallback, out bool exact)
        {
            const float slack = 0.03f;   // vertices sitting exactly on the window's edge (the base ring at y = 0) still count
            float z = MaxZ(root, under, xMin - slack, xMax + slack, yMin - slack, yMax + slack);
            exact = !float.IsNegativeInfinity(z);
            if (!exact)
                z = MaxZ(root, under, xMin, xMax, float.NegativeInfinity, float.PositiveInfinity);
            return float.IsNegativeInfinity(z) ? fallback : z;
        }

        private static float MaxZ(Transform root, Transform under, float xMin, float xMax, float yMin, float yMax)
        {
            float best = float.NegativeInfinity;
            foreach (MeshFilter filter in under.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable || IsDecoration(filter.transform, root))
                    continue;
                Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 v in mesh.vertices)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(v);
                    if (p.x >= xMin && p.x <= xMax && p.y >= yMin && p.y <= yMax && p.z > best)
                        best = p.z;
                }
            }
            return best;
        }

        /// <summary>Props and stickers are ours, not part of the body.</summary>
        public static bool IsDecoration(Transform t, Transform stop)
        {
            for (; t != null && t != stop; t = t.parent)
                if (t.name.StartsWith(StoreIndicator.PropPrefix) || t.name == StoreIndicator.StickerName)
                    return true;
            return false;
        }

        /// <summary>A plain box collider around the store's looks, on the store's layer.</summary>
        public static void AddBoxCollider(GameObject root, Bounds area)
        {
            var go = new GameObject("collider");
            go.layer = root.layer;
            go.transform.SetParent(root.transform, false);
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.center = area.center;
            box.size = area.size;
        }

        public static void SetLayer(GameObject go, int layer)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }

        /// <summary>Remove everything that isn't looks: particles, lights, audio, and optionally colliders.</summary>
        public static void Strip(GameObject go, bool keepColliders)
        {
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
                if (ps) Object.DestroyImmediate(ps.gameObject);
            foreach (Light light in go.GetComponentsInChildren<Light>(true))
                Object.DestroyImmediate(light);
            foreach (AudioSource audio in go.GetComponentsInChildren<AudioSource>(true))
                Object.DestroyImmediate(audio);
            if (!keepColliders)
                foreach (Collider collider in go.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(collider);
        }

        /// <summary>
        /// Drop renderers that no longer exist from every LODGroup on the store (call before adding props,
        /// whose own LODGroups are complete). With <paramref name="removeGroups"/>, remove the groups instead -
        /// for stores whose original looks are all gone.
        /// </summary>
        public static void FixLodGroups(GameObject root, bool removeGroups)
        {
            foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
            {
                if (removeGroups)
                {
                    Object.DestroyImmediate(group);
                    continue;
                }
                LOD[] lods = group.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                    lods[i].renderers = lods[i].renderers.Where(r => r != null).ToArray();
                group.SetLODs(lods);
            }
        }

        public static void DestroyPath(GameObject root, string path)
        {
            Transform t = root.transform.Find(path);
            if (t != null)
                Object.DestroyImmediate(t.gameObject);
        }

        public static void DestroyAllNamed(GameObject root, string name)
        {
            foreach (Transform child in root.transform.Cast<Transform>().Where(c => c.name == name).ToList())
                Object.DestroyImmediate(child.gameObject);
        }

        public static void DestroyChildren(Transform parent)
        {
            foreach (Transform child in parent.Cast<Transform>().ToList())
                Object.DestroyImmediate(child.gameObject);
        }

        /// <summary>Place <paramref name="t"/> by a position, rotation and scale given in root space.</summary>
        public static void SetRootSpace(Transform t, Transform root, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Matrix4x4 parentInRoot = root.worldToLocalMatrix * t.parent.localToWorldMatrix;
            Matrix4x4 rootToParent = parentInRoot.inverse;
            t.localPosition = rootToParent.MultiplyPoint3x4(position);
            t.localRotation = rootToParent.rotation * rotation;
            Vector3 parentScale = parentInRoot.lossyScale;
            t.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
        }

        /// <summary>Combined mesh bounds of everything under <paramref name="under"/>, in <paramref name="space"/>'s local space.</summary>
        public static Bounds MeshBounds(Transform space, Transform under)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (MeshFilter filter in under.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                Matrix4x4 toSpace = space.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Bounds mb = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = toSpace.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }

        /// <summary>
        /// Give the store's own copies of its materials a tint. Copies, never the shared vanilla materials,
        /// so nothing else in the game changes colour (the ShieldShare lesson).
        /// </summary>
        public static void Tint(Transform under, Color tint)
        {
            foreach (Renderer renderer in under.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m =>
                {
                    if (m == null || !m.HasProperty("_Color"))
                        return m;
                    var copy = new Material(m) { name = m.name + "_els_tint" };
                    copy.color = m.color * tint;
                    return copy;
                }).ToArray();
            }
        }
    }

    /// <summary>One prop: which item's mesh, where it sits (as a fraction of the store's visual bounds), how big.</summary>
    internal readonly struct PropSpot
    {
        public readonly string ItemPrefab;
        public readonly Vector3 Anchor;   // 0..1 across the bounds on each axis; the prop's base sits here
        public readonly float Yaw;
        public readonly float Tilt;
        public readonly float Size;       // longest side, in the store's unscaled units (metres before any root scale)
        public readonly float? FrontGap;  // set: stands on the ground in front (+Z) of the store, this far from it
        public readonly bool Against;     // gap measured from the body's real surface, not its bounding box
        public readonly float FallbackGap; // Against only: gap from the bounding box when the surface can't be read

        public PropSpot(string itemPrefab, float x, float y, float z, float yaw, float size, float tilt = 0f)
        {
            ItemPrefab = itemPrefab;
            Anchor = new Vector3(x, y, z);
            Yaw = yaw;
            Tilt = tilt;
            Size = size;
            FrontGap = null;
            Against = false;
            FallbackGap = 0f;
        }

        private PropSpot(string itemPrefab, float x, float gap, float yaw, float size, bool against, float fallbackGap = 0f)
        {
            FallbackGap = fallbackGap;
            ItemPrefab = itemPrefab;
            Anchor = new Vector3(x, 0f, 1f);
            Yaw = yaw;
            Tilt = 0f;
            Size = size;
            FrontGap = gap;
            Against = against;
        }

        /// <summary>On the ground in front of the store. <paramref name="gap"/> in metres before root scale; negative tucks it under an overhang.</summary>
        public static PropSpot InFront(string itemPrefab, float x, float gap, float yaw, float size) =>
            new PropSpot(itemPrefab, x, gap, yaw, size, against: false);

        /// <summary>
        /// On the ground in front, pushed up against the body: <paramref name="gap"/> is measured from the body's
        /// actual surface at the prop's own height and width (curves and bulges included).
        /// </summary>
        /// <param name="fallbackGap">Used, from the bounding box, if the body's shape can't be read at the prop's height.</param>
        public static PropSpot AgainstFront(string itemPrefab, float x, float gap, float yaw, float size, float fallbackGap) =>
            new PropSpot(itemPrefab, x, gap, yaw, size, against: true, fallbackGap);
    }

    internal static class Props
    {
        /// <summary>
        /// Copy an Epic Loot item's model into the store. Epic Loot items carry a placeholder mesh on their
        /// "attach" object; the real model is its first child, which is what gets copied.
        /// Returns false if Epic Loot (or that item) isn't there - the store still works, just without props.
        /// </summary>
        public static bool Add(Transform root, Transform parent, Bounds area, PropSpot spot)
        {
            GameObject item = Jotunn.Managers.PrefabManager.Instance.GetPrefab(spot.ItemPrefab);
            Transform model = item != null ? item.transform.Find("attach") : null;
            model = model != null && model.childCount > 0 ? model.GetChild(0) : null;
            if (model == null)
                return false;

            GameObject prop = Object.Instantiate(model.gameObject, parent, false);
            prop.name = StoreIndicator.PropPrefix + spot.ItemPrefab;
            Kitbash.Strip(prop, keepColliders: false);
            Kitbash.SetLayer(prop, root.gameObject.layer);

            // Size it from its own unrotated bounds.
            prop.transform.localScale = Vector3.one;
            Bounds own = Kitbash.MeshBounds(prop.transform, prop.transform);
            float longest = Mathf.Max(own.size.x, own.size.y, own.size.z);
            float scale = longest > 0.0001f ? spot.Size / longest : 1f;

            Vector3 anchor = new Vector3(   // unclamped: anchors outside 0..1 are allowed
                Mathf.LerpUnclamped(area.min.x, area.max.x, spot.Anchor.x),
                Mathf.LerpUnclamped(area.min.y, area.max.y, spot.Anchor.y),
                Mathf.LerpUnclamped(area.min.z, area.max.z, spot.Anchor.z));

            // Rotate and scale first, then measure where it really ended up and move it into place:
            // centred on the anchor in x/z, its lowest point on the anchor height. Works for tilted props too.
            Kitbash.SetRootSpace(prop.transform, root, anchor, Quaternion.Euler(spot.Tilt, spot.Yaw, 0f), Vector3.one * scale);
            Bounds placed = Kitbash.MeshBounds(root, prop.transform);
            Vector3 shift = new Vector3(anchor.x - placed.center.x, anchor.y - placed.min.y, anchor.z - placed.center.z);
            if (spot.FrontGap.HasValue)   // in front of the store: back face this far from its front
            {
                float front = area.max.z, gap = spot.FrontGap.Value;
                if (spot.Against)
                {
                    float surface = Kitbash.FrontSurfaceZ(root, parent, placed.min.x + shift.x, placed.max.x + shift.x,
                        area.min.y, area.min.y + placed.size.y, area.max.z, out bool exact);
                    if (exact)
                        front = surface;
                    else
                        gap = spot.FallbackGap;
                    Jotunn.Logger.LogInfo($"[EpicLootStorage] {root.name}: {spot.ItemPrefab} placed against " +
                                          (exact ? "the body's surface." : "the bounding box (shape not readable), using the fallback gap."));
                }
                shift.z = front + gap - placed.min.z;
            }
            Kitbash.SetRootSpace(prop.transform, root, anchor + shift, Quaternion.Euler(spot.Tilt, spot.Yaw, 0f), Vector3.one * scale);
            return true;
        }
    }
}
