namespace OpenRoomPlan.Core
{
    /// <summary>
    /// Stuff-class vocabulary carried by the semantic TSDF (report Stage 4/5). Kept deliberately small:
    /// geometry already classifies wall/floor/ceiling deterministically; the learned classes that matter
    /// are openings and furniture (architecture review, Challenge 4). Byte-sized for per-voxel storage.
    /// </summary>
    public enum SemanticClass : byte
    {
        Unknown = 0,
        Wall = 1,
        Floor = 2,
        Ceiling = 3,
        Door = 4,
        Window = 5,
        Furniture = 6,
        Other = 7,
    }
}
