using System;
using System.Collections.Generic;
using UnityEngine;
using static Exoa.Designer.DataModel;

namespace IBMROS.Designer
{
    /// <summary>
    /// Data-driven floor-plan presets. Adding a template = adding one Preset
    /// entry here (polygon + openings); nothing else changes — the picker UI,
    /// thumbnails and generation all read this table.
    ///
    /// Geometry contract (INTEGRATION_MAP §1): meters, world XZ (Vector2.y = Z),
    /// polygons wound counter-clockwise like FloorPlanEditor.CreateRectRoom,
    /// centered on the origin (the grid plane center). Openings must lie on
    /// (within 0.2 m of) a wall line; Tangent is the direction the wall runs.
    /// </summary>
    public static class RoomPresets
    {
        public sealed class OpeningSpec
        {
            public FloorMapItemType Type;
            public Vector2 Position;
            public Vector2 Tangent;
            public float Width;
            public float Height;
            public float YPos; // sill height (windows)

            public OpeningSpec(FloorMapItemType type, Vector2 position, Vector2 tangent,
                float width, float height, float ypos = 0f)
            {
                Type = type; Position = position; Tangent = tangent;
                Width = width; Height = height; YPos = ypos;
            }
        }

        public sealed class Preset
        {
            public string Id;
            public string DisplayName;
            public Vector2[] Polygon;
            public OpeningSpec[] Openings;
        }

        public const string DefaultId = "rectangle";

        private static Vector2 V(float x, float z) => new Vector2(x, z);

        private static OpeningSpec Door(float x, float z, Vector2 tangent) =>
            new OpeningSpec(FloorMapItemType.Door, V(x, z), tangent, 0.9f, 2.1f);

        private static OpeningSpec Window(float x, float z, Vector2 tangent) =>
            new OpeningSpec(FloorMapItemType.Window, V(x, z), tangent, 1.2f, 1.2f, 0.9f);

        public static readonly Preset[] All =
        {
            new Preset
            {
                Id = "rectangle", DisplayName = "Rectangle",
                Polygon = new[] { V(-2.5f, -2f), V(2.5f, -2f), V(2.5f, 2f), V(-2.5f, 2f) },
                Openings = new[]
                {
                    Door(-1.5f, -2f, Vector2.right),
                    Window(-1.0f, 2f, Vector2.right),
                    Window(1.2f, 2f, Vector2.right),
                },
            },
            new Preset
            {
                Id = "l-shape", DisplayName = "L-Shape",
                Polygon = new[]
                {
                    V(-3f, -2.5f), V(3f, -2.5f), V(3f, 0f),
                    V(0f, 0f), V(0f, 2.5f), V(-3f, 2.5f),
                },
                Openings = new[]
                {
                    Door(-1.5f, -2.5f, Vector2.right),
                    Window(-3f, 0f, Vector2.up),      // left wall runs along Z
                    Window(1.5f, 0f, Vector2.right),  // inner top wall of the low wing
                },
            },
            new Preset
            {
                Id = "t-shape", DisplayName = "T-Shape",
                Polygon = new[]
                {
                    V(-3f, 0f), V(-1.25f, 0f), V(-1.25f, -2.5f), V(1.25f, -2.5f),
                    V(1.25f, 0f), V(3f, 0f), V(3f, 2.5f), V(-3f, 2.5f),
                },
                Openings = new[]
                {
                    Door(0f, -2.5f, Vector2.right),
                    Window(0f, 2.5f, Vector2.right),
                },
            },
            new Preset
            {
                Id = "z-shape", DisplayName = "Z-Shape",
                Polygon = new[]
                {
                    V(-0.5f, -2.5f), V(3f, -2.5f), V(3f, 0f), V(0.5f, 0f),
                    V(0.5f, 2.5f), V(-3f, 2.5f), V(-3f, 0f), V(-0.5f, 0f),
                },
                Openings = new[]
                {
                    Door(1.25f, -2.5f, Vector2.right),
                    Window(-1.25f, 2.5f, Vector2.right),
                },
            },
        };

        public static Preset Get(string id)
        {
            foreach (Preset p in All)
                if (p.Id == id)
                    return p;
            return null;
        }

        /// <summary>
        /// Generates the preset through the mutation gateway as ONE labelled undo
        /// step (nested gateway scopes fold into the outermost — INTEGRATION_MAP §3).
        /// Returns the room item id, or null on failure.
        /// </summary>
        public static string Apply(Preset preset)
        {
            if (preset == null)
                return null;

            var svc = IBMROS.Bridge.UndoRedo.UndoRedoService.Instance;
            IDisposable scope = svc != null
                ? (IDisposable)svc.BeginAction("Create " + preset.DisplayName)
                : null;
            try
            {
                string roomId = IBMROS.Core.FloorPlanEditor.CreateRoom(preset.Polygon, preset.DisplayName);
                if (roomId == null)
                    return null;
                if (preset.Openings != null)
                {
                    foreach (OpeningSpec o in preset.Openings)
                        IBMROS.Core.FloorPlanEditor.AddOpening(o.Type, o.Position, o.Tangent,
                            o.Width, o.Height, o.YPos);
                }
                return roomId;
            }
            finally
            {
                scope?.Dispose();
            }
        }
    }
}
