using System.Collections.Generic;
using UnityEngine;

namespace IBMROS.Designer.Materials
{
    /// <summary>
    /// Catalog of door/window VISUAL models and their per-part materials.
    ///
    /// The plugin builds doors and windows procedurally (ProceduralOpening
    /// generates leaf/glass/handle meshes from width, height, thickness…), and
    /// the wall hole is cut by the room from the opening's control point. Those
    /// two things are independent: the hole does not care what the visual looks
    /// like. So a custom model can replace the visual WITHOUT touching any wall
    /// logic, as long as it respects the placement convention.
    ///
    ///   Models:     Resources/OpeningModels/{id}.prefab
    ///   Materials:  Resources/Opening/{name}.mat   (frame / glass / handle)
    ///
    /// A model prefab is placed by OpeningController exactly like the procedural
    /// one — parented to the opening, positioned at the control point offset
    /// along the wall tangent, rotated so its local +X runs along the wall — and
    /// is scaled by <see cref="OpeningModel.referenceSize"/> so one prefab serves
    /// any opening width/height. Drop a prefab in the folder to add a variant; no
    /// code change.
    ///
    /// An empty model id keeps the plugin's procedural door/window, so every
    /// existing room is unaffected.
    /// </summary>
    public static class OpeningStyleLibrary
    {
        public const string MODEL_FOLDER = "OpeningModels";
        public const string MATERIAL_FOLDER = "Opening";

        /// <summary>Marks a model prefab and declares the size it was authored at.</summary>
        public sealed class OpeningModel
        {
            public string Id;
            public GameObject Prefab;
            /// <summary>Authored width/height in metres, used to scale to the opening.</summary>
            public Vector2 ReferenceSize;
        }

        private static Dictionary<string, OpeningModel> models;
        private static readonly Dictionary<string, Material> materialCache =
            new Dictionary<string, Material>();

        private static void EnsureLoaded()
        {
            if (models != null)
                return;
            models = new Dictionary<string, OpeningModel>();
            foreach (GameObject go in Resources.LoadAll<GameObject>(MODEL_FOLDER))
            {
                var marker = go.GetComponent<OpeningModelMarker>();
                models[go.name] = new OpeningModel
                {
                    Id = go.name,
                    Prefab = go,
                    ReferenceSize = marker != null && marker.referenceSize.x > 0f
                        ? marker.referenceSize
                        : new Vector2(1f, 2.1f),
                };
            }
        }

        /// <summary>Available model ids (empty id = the procedural default).</summary>
        public static IReadOnlyList<string> ModelIds()
        {
            EnsureLoaded();
            var ids = new List<string>(models.Keys);
            ids.Sort(string.CompareOrdinal);
            return ids;
        }

        public static OpeningModel GetModel(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            EnsureLoaded();
            return models.TryGetValue(id, out OpeningModel m) ? m : null;
        }

        /// <summary>Available part materials (frame / glass / handle share one set).</summary>
        public static IReadOnlyList<string> MaterialNames()
        {
            var names = new List<string>();
            foreach (Material m in Resources.LoadAll<Material>(MATERIAL_FOLDER))
            {
                names.Add(m.name);
                materialCache[m.name] = m;
            }
            names.Sort(string.CompareOrdinal);
            return names;
        }

        public static Material ResolveMaterial(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (materialCache.TryGetValue(name, out Material hit) && hit != null)
                return hit;
            Material m = Resources.Load<Material>(MATERIAL_FOLDER + "/" + name);
            if (m != null)
                materialCache[name] = m;
            return m;
        }

        public static void Invalidate()
        {
            models = null;
            materialCache.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Invalidate();
    }

}
