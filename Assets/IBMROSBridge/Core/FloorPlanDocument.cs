using System.Collections.Generic;
using Exoa.Json;
using static Exoa.Designer.DataModel;

namespace IBMROS.Core
{
    /// <summary>
    /// Architecture track A2 (step 1) — the floor-plan document model.
    ///
    /// Owns the FloorMapV2 state that previously lived as a private field inside the
    /// FloorMapSerializer MonoBehaviour, and every document-level operation on it
    /// (floor add/duplicate/remove, load/serialize, emptiness). Mutation methods raise
    /// DocumentEvents themselves — the model announces its own changes (A1), callers
    /// don't have to remember to.
    ///
    /// Deliberate scope of step 1: the *current floor's spaces* are still gathered from
    /// the UI items on serialize (see FloorMapSerializer.SerializeScene) — migrating
    /// that ownership is A2 step 2. Until then this class is the single write path for
    /// everything at document/floor granularity.
    ///
    /// Semantics note: FloorMapV2 and FloorMapLevel are structs whose List fields are
    /// shared references across copies. All methods here preserve the vendor's exact
    /// copy semantics (verified against the original serializer code) — including
    /// DuplicateFloor sharing the spaces list with the source floor, which the existing
    /// save flow compensates for by *assigning* fresh lists rather than mutating.
    /// Do not "fix" that here without a golden run proving the change safe.
    /// </summary>
    public sealed class FloorPlanDocument
    {
        private FloorMapV2 data;

        /// <summary>
        /// The raw document state. Struct copy, but its floors list is the live shared
        /// reference — read freely; route writes through this class (A3 enforces this).
        /// </summary>
        public FloorMapV2 Data => data;

        /// <summary>Mirrors the vendor's "project created or opened" test.</summary>
        public bool IsLoaded => data.floors != null;

        public bool HasFloors => data.floors != null && data.floors.Count > 0;

        /// <summary>Mirrors the vendor's IsSceneEmpty test exactly.</summary>
        /// <summary>
        /// True when no floor holds any space. A2 step 2b bug fix: the vendor's
        /// IsSceneEmpty inspected floors[0] ONLY, so a document with a blank ground
        /// floor but a populated upper floor read as "empty" and could skip UISaving's
        /// save-on-exit prompt (data-loss risk). Now checks every floor.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                if (data.floors == null || data.floors.Count == 0)
                    return true;
                foreach (FloorMapLevel floor in data.floors)
                {
                    if (floor.spaces != null && floor.spaces.Count > 0)
                        return false;
                }
                return true;
            }
        }

        public void Reset()
        {
            data = new FloorMapV2();
        }

        /// <summary>
        /// All space item ids across all floors (A2/A3 query surface). Items created
        /// before A2 identity (or from ancient saves) may still be null; skipped.
        /// </summary>
        public List<string> GetAllItemIds()
        {
            List<string> ids = new List<string>();
            if (data.floors == null)
                return ids;
            foreach (FloorMapLevel floor in data.floors)
            {
                if (floor.spaces == null)
                    continue;
                foreach (FloorMapItem item in floor.spaces)
                {
                    if (!string.IsNullOrEmpty(item.uniqueId))
                        ids.Add(item.uniqueId);
                }
            }
            return ids;
        }

        /// <summary>Finds a space item by id across floors. found=false when absent.</summary>
        public FloorMapItem GetItemById(string itemId, out bool found)
        {
            found = false;
            if (data.floors != null && !string.IsNullOrEmpty(itemId))
            {
                foreach (FloorMapLevel floor in data.floors)
                {
                    if (floor.spaces == null)
                        continue;
                    foreach (FloorMapItem item in floor.spaces)
                    {
                        if (item.uniqueId == itemId)
                        {
                            found = true;
                            return item;
                        }
                    }
                }
            }
            return default;
        }

        /// <summary>Loads from JSON via the vendor converter (handles the v1→v2 ladder).</summary>
        public void LoadFromJson(string json)
        {
            data = DeserializeFloorMapJsonFile(json);
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(data, Formatting.Indented, new JsonSerializerSettings()
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            });
        }

        /// <summary>Creates the empty v2 project (one blank floor) and returns its JSON.</summary>
        public string CreateEmpty()
        {
            data = new FloorMapV2
            {
                version = "v2",
                floors = new List<FloorMapLevel> { new FloorMapLevel() },
                settings = new BuildingSettings()
            };
            return ToJson();
        }

        /// <summary>Returns the floor with this id, or a default FloorMapLevel (vendor semantics).</summary>
        public FloorMapLevel GetFloorById(string id)
        {
            if (data.floors != null && data.floors.Count > 0)
            {
                for (int i = 0; i < data.floors.Count; i++)
                {
                    if (data.floors[i].uniqueId == id)
                        return data.floors[i];
                }
            }
            return new FloorMapLevel();
        }

        public FloorMapLevel AddFloor()
        {
            FloorMapLevel floor = new FloorMapLevel();
            floor.spaces = new List<FloorMapItem>();
            floor.GenerateUniqueId();
            data.floors.Add(floor);
            DocumentEvents.RaiseChanged(DocumentChangeKind.Floor, "Add Floor");
            return floor;
        }

        /// <summary>
        /// Duplicates a floor with a fresh unique id (struct copy: the spaces list
        /// reference is shared with the source — vendor semantics, see class note).
        /// </summary>
        public FloorMapLevel DuplicateFloor(string id)
        {
            FloorMapLevel floor = GetFloorById(id);
            floor.uniqueId = null;
            floor.GenerateUniqueId();
            data.floors.Add(floor);
            DocumentEvents.RaiseChanged(DocumentChangeKind.Floor, "Duplicate Floor");
            return floor;
        }

        public void RemoveFloor(string id)
        {
            bool removed = false;
            if (data.floors != null && data.floors.Count > 0)
            {
                for (int i = 0; i < data.floors.Count; i++)
                {
                    if (data.floors[i].uniqueId == id)
                    {
                        data.floors.RemoveAt(i);
                        removed = true;
                    }
                }
            }
            if (removed)
                DocumentEvents.RaiseChanged(DocumentChangeKind.Floor, "Remove Floor");
        }

        /// <summary>
        /// A2 step-1 transition seam: the serializer still gathers the floor list and the
        /// current floor's spaces from the UI on save; this is the single method through
        /// which that UI-derived state enters the document. Step 2 inverts the flow.
        /// Not a user mutation — raises nothing.
        /// </summary>
        public void SetFloors(List<FloorMapLevel> floors)
        {
            data.floors = floors;
        }

        /// <summary>Transition seam, same as SetFloors (settings come from AppController on save).</summary>
        public void SetSettings(BuildingSettings settings)
        {
            data.settings = settings;
        }
    }
}
