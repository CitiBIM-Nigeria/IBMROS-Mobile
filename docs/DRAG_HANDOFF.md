# Furniture drag — handoff notes

**Symptom:** dragging a placed furniture item feels sticky/jumpy — it doesn't
follow the cursor smoothly, especially near walls. Selection + rotation work.

---

## Files involved (in priority order)

| File | Role |
|---|---|
| `Assets/MyStuffs/Scripts/Interaction/ObjectManipulator.cs` | **Input driver.** Receives pointer down/move/up and dispatches to scale → drag → camera (priority order). Calls `dragHandler.UpdateDrag(screenPosition)` on every pointer move (line ~138). |
| `Assets/MyStuffs/Scripts/Interaction/ObjectDragHandler.cs` | **The drag itself.** `TryBeginDrag` (begins, stores grab offset + room bounds), `UpdateDrag` (every move: raycasts the floor, builds target, clamps to room bounds, sets `transform.position`). **This is where the stickiness is.** |
| `Assets/MyStuffs/Scripts/Furniture/FurniturePlacer.cs` | Initial GHOST placement of a NEW item (`HandlePointerMove`/`HandlePointerClick`). Same floor-raycast pattern; has a `POSITION_SMOOTH_SPEED` Lerp. |
| `Assets/MyStuffs/Scripts/Furniture/FurnitureSpawnManager.cs` | `InitializeFurnitureItem`: sets the item's **layer** (`Interactable`), **colliders** (disabled root BoxCollider + non-convex MeshColliders), and a **kinematic Rigidbody**. |
| `Assets/MyStuffs/Scripts/Core/SelectionManager.cs` | Selection (raycasts the `Interactable` layer). |
| `Assets/MyStuffs/Scripts/Core/CameraController.cs` | Camera orbit/pan. Note it ALSO uses a `wallLayer` and competes for the same pointer input. |

---

## Most likely root cause (start here)

`ObjectDragHandler.UpdateDrag` only moves the item when this succeeds:

```csharp
if (Physics.Raycast(ray, out RaycastHit hit, 100f, floorLayer)) { ... move ... }
```

It raycasts the **floor collider**. When the cursor points at a **wall** (or just
outside the room, or the camera angle makes the ray skim the floor), this raycast
**misses or hits a wild point**, so the item doesn't update that frame → it feels
stuck and only "jumps" once the cursor is back over open floor.

**Recommended fix:** stop raycasting the floor collider. Raycast a **math plane**
at floor height instead — it always returns a point, so the item tracks the cursor
smoothly everywhere, and the existing room-bounds clamp keeps it inside the walls:

```csharp
// once, when drag starts: float _floorY = floorHit.point.y;
var plane = new Plane(Vector3.up, new Vector3(0, _floorY, 0));
if (plane.Raycast(ray, out float enter)) {
    Vector3 p = ray.GetPoint(enter);            // always valid
    Vector3 target = new Vector3(p.x + _grabOffset.x, _floorY + pivotToBase,
                                 p.z + _grabOffset.z);
    // ...then the SAME room-bounds clamp that's already in UpdateDrag...
    _selectedObject.position = target;
}
```

Apply the same change in `FurniturePlacer.HandlePointerMove`/`HandlePointerClick`.

---

## Things to verify in the Scene / Inspector

1. **`ObjectDragHandler.floorLayer`** (and `FurniturePlacer.floorLayer`) — the
   LayerMask must point ONLY at the floor's layer. If it's wrong/empty, the
   raycast misses constantly.
2. **Floor object** — has a collider, is on that floor layer, and is large enough
   to cover the room.
3. **Layers exist:** `Wall`, `Interactable`, and the floor layer must all be
   defined in Tags & Layers. If `Interactable` is missing,
   `FurnitureSpawnManager` silently leaves items on `Default`.
4. **`Camera.main`** — exactly one camera tagged `MainCamera` (ObjectDragHandler
   caches `Camera.main` in Awake).
5. **Walls** — `Mesh Collider` with **Convex OFF**, on the `Wall` layer.

## What's already been done (don't redo)

- Wall blocking: replaced a fragile `Physics.BoxCast` (against `Wall`+`Default`,
  which false-blocked on the floor/props) with **room-bounds clamping** in
  `UpdateDrag` (clamp the item's footprint to the floor's bounds). It stops at
  walls and slides along them. The remaining stickiness is the **floor-raycast
  miss** described above, not the clamp.
- Colliders: disabled root BoxCollider (kept only because some code reads its
  size) + non-convex MeshColliders for precise selection.
