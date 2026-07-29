using ProceduralToolkit;
using ProceduralToolkit.ClipperLib;
using System.Collections.Generic;
using UnityEngine;

namespace Exoa.Designer
{
    public class ProceduralOpening : MonoBehaviour
    {
        public MeshFilter doorMf;
        public MeshFilter glassMf;
        public MeshFilter handleMf;

        public bool addMeshColliders;

        void Start()
        {

        }
        public DataModel.FloorMapItemType type;

        public float width = 1;
        public float height = 2.5f;
        public float wallsHeight = 3f;
        [Range(0, 2.5f)]
        public float yPos;
        [Range(0.01f, 0.1f)]
        public float thickness = .02f;
        [Range(0.01f, 0.1f)]
        public float handleRadius = 0.03f;
        [Range(0.0f, 1f)]
        public float handleX = 0.9f;
        [Range(0.0f, 1f)]
        public float handleY = 0.5f;
        [Range(4, 16)]
        public int handleSegments = 10;

        public bool hasWindow;
        public bool hasHandle;
        [Range(0, 10)]
        public int horizontalWindowSubs = 2;
        [Range(0, 10)]
        public int verticalWindowSubs = 2;
        [Range(0f, 1f)]
        public float horizontalWindowSize = 0.9f;
        [Range(0f, 1f)]
        public float verticalWindowSize = 0.5f;
        [Range(0.01f, 0.1f)]
        public float windowFrameSize = 0.01f;



        public void Generate()
        {
            Generate(type, width, height, yPos,
                hasWindow, windowFrameSize, horizontalWindowSize,
                verticalWindowSize, horizontalWindowSubs, verticalWindowSubs,
                wallsHeight, thickness);
        }

        public void Generate(ProceduralRoom.GenericOpening go, float wallsHeight, float thickness)
        {
            DataModel.FloorMapItemType t = DataModel.FloorMapItemType.Opening;
            if (go.type == ProceduralRoom.GenericOpening.OpeningType.Door) t = DataModel.FloorMapItemType.Door;
            if (go.type == ProceduralRoom.GenericOpening.OpeningType.Window) t = DataModel.FloorMapItemType.Window;
            Generate(t, go.width, go.height, go.yPos, go.hasWindow, go.windowFrameSize, go.windowSizeH, go.windowSizeV, go.windowSubDivH, go.windowSubDivV, wallsHeight, thickness);
        }
        public void Generate(DataModel.FloorMapItemType t, float width, float height, float yPos,
            bool hasWindow, float windowFrameSize, float windowSizeH, float windowSizeV,
            int windowSubDivH, int windowSubDivV,
            float wallsHeight, float thickness)
        {
            if (t != DataModel.FloorMapItemType.Door && t != DataModel.FloorMapItemType.Window)
                return;

            this.type = t;
            this.width = width;
            this.height = height;
            this.yPos = yPos;
            this.wallsHeight = wallsHeight;
            this.thickness = thickness;
            this.hasWindow = hasWindow;
            this.windowFrameSize = windowFrameSize;
            this.horizontalWindowSize = windowSizeH;
            this.verticalWindowSize = windowSizeV;
            this.horizontalWindowSubs = windowSubDivH;
            this.verticalWindowSubs = windowSubDivV;

            if (type == DataModel.FloorMapItemType.Door)
            {
                this.hasHandle = true;
                // IBMROS: a door's height is ITS OWN, not the global doorsHeight.
                // Overwriting it here meant the height on the item did nothing at all:
                // every door rendered (and was cut) at doorsHeight, so resizing a door
                // vertically changed the number in the document and the label and nothing
                // else. doorsHeight is now only the DEFAULT for a door that has none.
                if (this.height <= 0.01f)
                    this.height = AppController.Instance.doorsHeight;
                this.height = Mathf.Min(this.height, AppController.Instance.wallsHeight);
            }
            if (type == DataModel.FloorMapItemType.Window)
            {
                this.verticalWindowSize = 1;
                this.horizontalWindowSize = 1;
                this.hasWindow = true;
                this.hasHandle = false;
            }

            //print("Generate DoorOrWindow width:" + width + " height:" + height + " ypos:" + yPos);
            GenerateMesh();
        }
        void GenerateMesh()
        {

            MeshDraft md = new MeshDraft() { name = "Opening" };
            MeshDraft glass = GenerateGlass(type, width, height);

            MeshDraft front = GenerateFace(type, width, height, yPos, true);
            MeshDraft back = GenerateFace(type, width, height, yPos, false);

            back.FlipTriangles();
            back.FlipNormals();
            back.Move(-Vector3.forward * thickness * .5f);
            front.Move(Vector3.forward * thickness * .5f);

            if (hasHandle)
            {
                MeshDraft handle = GenerateHandle(width, height);
                handle.Move(Vector3.forward * thickness * .5f);

                MeshDraft handle2 = GenerateHandle(width, height);
                handle2.Move(-Vector3.forward * (thickness * .5f + handleRadius * 2));
                handle.Add(handle2);
                handleMf.mesh = handle.ToMesh();
            }


            md.Add(front);
            md.Add(back);
            doorMf.mesh = md.ToMesh();
            glassMf.mesh = glass.ToMesh();

            AddMeshColliders();
        }

        private MeshDraft GenerateHandle(float width, float height)
        {
            MeshDraft md = MeshDraft.Sphere(handleRadius, handleSegments, handleSegments);

            md.Move(new Vector3(width * handleX, height * handleY, handleRadius));
            return md;
        }

        /// <summary>
        /// IBMROS: re-SYNC, not just add.
        ///
        /// This used to add a MeshCollider only when one was missing and never touch
        /// sharedMesh again. A MeshCollider keeps whatever mesh it was handed and does
        /// NOT follow its MeshFilter, and Generate() assigns a brand-new mesh every
        /// call — so from the second generation onward the collider described the
        /// opening's PREVIOUS size. Resizing a door left its pickable shape at the old
        /// dimensions, which is the same defect fixed in ProceduralRoom (commit
        /// b60eb53) for wall/floor/ceiling colliders.
        ///
        /// Only sharedMesh is re-pointed when it already matches, so no garbage is
        /// produced on the steady-state re-assert path.
        /// </summary>
        private void AddMeshColliders()
        {
            if (!addMeshColliders)
                return;
            SyncCollider(doorMf);
            SyncCollider(glassMf);
            SyncCollider(handleMf);
        }

        private static void SyncCollider(MeshFilter mf)
        {
            if (mf == null)
                return;
            MeshCollider mc = mf.GetComponent<MeshCollider>();
            if (mc == null)
                mc = mf.gameObject.AddComponent<MeshCollider>();
            if (mc.sharedMesh != mf.sharedMesh)
                mc.sharedMesh = mf.sharedMesh;
        }



        public MeshDraft GenerateGlass(DataModel.FloorMapItemType t, float width, float height)
        {
            // IBMROS: yPos is the SILL — the bottom of the opening above the floor —
            // which is what the field means everywhere else (RoomPresets passes 0.9 for a
            // window sill) and what OpeningAnchor's clamps assume. It used to be read as
            // an offset from the wall's MIDDLE to the opening's CENTRE, so a window with
            // sill 0.90 in a 3 m wall was drawn (and cut) at 1.80..3.00 — jammed against
            // the ceiling. Measured before this change: doc sill..top 0.90..2.10, actual
            // 1.80..3.00.
            float holeBottom = t == DataModel.FloorMapItemType.Door
                ? 0f : Mathf.Clamp(yPos, 0f, wallsHeight);
            float holeTop = Mathf.Clamp(holeBottom + height, 0f, wallsHeight);

            MeshDraft md = MeshDraft.Quad(new Vector3(0, holeBottom, 0), Vector3.right * width, Vector3.up * height, true);
            md.FlipTriangles();
            md.FlipNormals();
            md.Add(MeshDraft.Quad(new Vector3(0, holeBottom, 0), Vector3.right * width, Vector3.up * height, true));
            return md;
        }
        public MeshDraft GenerateFace(DataModel.FloorMapItemType type, float width, float height, float yPos, bool sides)
        {

            MeshDraft md = new MeshDraft() { name = "Face" };

            // IBMROS: yPos is the SILL, not an offset from the wall's middle — see
            // GenerateGlass. Doors sit on the floor, so their sill is always 0.
            float holeBottom = type == DataModel.FloorMapItemType.Door
                ? 0f : Mathf.Clamp(yPos, 0f, wallsHeight);
            float holeTop = Mathf.Clamp(holeBottom + height, 0f, wallsHeight);


            List<Vector2> subject = new List<Vector2>();

            subject.Add(new Vector2(0, holeBottom));
            subject.Add(new Vector2(0, holeTop));
            subject.Add(new Vector2(width, holeTop));
            subject.Add(new Vector2(width, holeBottom));

            List<List<Vector2>> output = new List<List<Vector2>>();

            var clipper = new PathClipper();
            clipper.AddPath(subject, PolyType.ptSubject);




            float mw = width * .5f;
            float mwf = width * .5f - windowFrameSize;
            float mh = height * .5f;
            float hf = height - windowFrameSize;
            float hf2 = height - windowFrameSize * .5f;
            float hf3 = height - windowFrameSize * 2f;
            float windowHeight = (hf3 * verticalWindowSize);
            float windowWidthHalf = mwf * horizontalWindowSize;
            float windowWidth = (width - windowFrameSize) * horizontalWindowSize;

            if (hasWindow)
            {
                List<Vector2> clip = new List<Vector2>();
                if (type == DataModel.FloorMapItemType.Window)
                {
                    clip.Add(new Vector2(mw - windowWidthHalf, holeBottom + windowFrameSize));
                    clip.Add(new Vector2(mw - windowWidthHalf, holeTop - windowFrameSize));
                    clip.Add(new Vector2(mw + windowWidthHalf, holeTop - windowFrameSize));
                    clip.Add(new Vector2(mw + windowWidthHalf, holeBottom + windowFrameSize));
                }
                else if (type == DataModel.FloorMapItemType.Door)
                {
                    clip.Add(new Vector2(mw - windowWidthHalf, hf - windowHeight));
                    clip.Add(new Vector2(mw - windowWidthHalf, hf));
                    clip.Add(new Vector2(mw + windowWidthHalf, hf));
                    clip.Add(new Vector2(mw + windowWidthHalf, hf - windowHeight));
                }

                clipper.AddPath(clip, PolyType.ptClip);
            }

            if (sides)
            {
                for (int i = 1; i < horizontalWindowSubs; i++)
                {
                    MeshDraft cy = MeshDraft.Cylinder(thickness * .5f, 7, height, true);
                    cy.Move(new Vector3(mw - windowWidthHalf + (i * (windowWidth) / horizontalWindowSubs) - windowFrameSize * .5f, holeBottom + height * .5f, -thickness * .5f));
                    md.Add(cy);

                }
                for (int j = 1; j < verticalWindowSubs; j++)
                {
                    MeshDraft cy = MeshDraft.Cylinder(thickness * .5f, 7, width, true);
                    cy.Rotate(Quaternion.Euler(0, 0, 90));
                    cy.Move(new Vector3(width * .5f, holeBottom + hf2 - j * windowHeight / verticalWindowSubs, -thickness * .5f));
                    //cy.Move(new Vector3(j * height / verticalWindowSubs, height * .5f, -thickness * .5f));
                    md.Add(cy);
                }
            }

            clipper.Clip(ClipType.ctDifference, ref output);

            if (output.Count == 0)
                return md;

            Tessellator tessellator = new Tessellator();
            for (int i = 0; i < output.Count; i++)
            {
                tessellator.AddContour(output[i]);

                if (sides)
                {
                    MeshDraft windowFrame = MathUtils.Extrude(output[i], -Vector3.forward, thickness, i != 0);
                    md.Add(windowFrame);
                }
            }
            tessellator.Tessellate(normal: Vector3.forward);

            MeshDraft wall = tessellator.ToMeshDraft();
            md.Add(wall);
            md.Paint(Color.white);


            return md;
        }

    }
}
