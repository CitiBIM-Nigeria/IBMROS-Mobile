using Exoa.Events;
using UnityEngine;

namespace Exoa.Designer
{
    public class SpaceController : MonoBehaviour, IObjectDrawer
    {

        public GameObject GO
        {
            get
            {
                return gameObject;
            }
        }

#if FLOORMAP_MODULE
        protected ControlPointsController cpc;
        protected UIBaseItem ui;
        protected DataModel.FloorMapItem seq;
        protected ProceduralSpace proceduralSpace;
        protected Color roomCOlor;

        protected float lastRebuild;
        public float delayBetweenRebuilds = .1f;
        protected bool queuedRebuild;
        protected bool queuedSendRepositionOpeningsEvent;

        public ControlPointsController Cpc
        {
            get
            {
                return cpc;
            }

            set
            {
                cpc = value;
            }
        }


        public UIBaseItem UI
        {
            get
            {
                return ui;
            }

            set
            {
                ui = value as UIBaseItem;
            }
        }

        public Color DrawingColor
        {
            get
            {
                return roomCOlor;
            }

            set
            {
                roomCOlor = value;
            }
        }

        virtual protected void OnDestroy()
        {
            if (cpc != null)
            {
                cpc.OnPathChanged -= OnPathChanged;
                cpc.OnControlPointsChanged -= OnControlPointsChanged;
                cpc.OnRequestDrawMode -= OnRequestDrawMode;
            }
            if (ui != null) ui.OnChangeSettings -= OnChangeSequenceSettings;
            GameEditorEvents.OnRequestRebuildAllRooms -= OnRequestRebuildAllRooms;
        }

        public void Init()
        {
            if (cpc != null)
            {
                cpc.drawPath = true;
                cpc.snapToGrid = true;
                cpc.snapToPathLines = false;
                cpc.OnPathChanged += OnPathChanged;
                cpc.OnControlPointsChanged += OnControlPointsChanged;
                cpc.OnRequestDrawMode += OnRequestDrawMode;
            }
            if (ui != null) ui.OnChangeSettings += OnChangeSequenceSettings;
            GameEditorEvents.OnRequestRebuildAllRooms += OnRequestRebuildAllRooms;
        }

        protected void OnRequestRebuildAllRooms()
        {
            Rebuild();
        }

        public void Build(DataModel.FloorMapItem s)
        {
            seq = s;
            Rebuild();
        }

        private void OnRequestDrawMode(bool request)
        {
            if (ui != null) ui.ToggleDrawMode(request, true, true);
        }

        protected void OnControlPointsChanged()
        {
            //print("OnControlPointsChanged");
            // IBMROS: A1 observable document — announce the mutation at its source
            // (covers RoomController and OutsideController via this base class).
            IBMROS.Core.DocumentEvents.RaiseChanged(IBMROS.Core.DocumentChangeKind.Geometry, "Edit Shape");
            Rebuild();
        }

        protected void OnPathChanged()
        {
            //print("OnPathChanged");
            IBMROS.Core.DocumentEvents.RaiseChanged(IBMROS.Core.DocumentChangeKind.Geometry, "Draw"); // IBMROS: A1
            Rebuild(true);
        }

        public void OnChangeSequenceSettings(DataModel.FloorMapItem s, DataModel.FloorMapItemType type)
        {
            seq = s;
            IBMROS.Core.DocumentEvents.RaiseChanged(IBMROS.Core.DocumentChangeKind.ItemSettings, "Change Settings"); // IBMROS: A1
            Rebuild();
        }

        void Update()
        {
            if (queuedRebuild && lastRebuild < Time.time - delayBetweenRebuilds)
            {
                Rebuild(queuedSendRepositionOpeningsEvent);
            }
        }

        // IBMROS: A7 scoped invalidation — public entry so the scoped-rebuild helper can
        // rebuild just this room (recut its holes) without the global rebuild-all-rooms
        // broadcast. Does not reposition openings (an opening edit didn't move walls).
        public void RequestRebuild()
        {
            Rebuild(false);
        }

        virtual protected void Rebuild(bool sendRepositionOpeningsEvent = false)
        {
            if (lastRebuild > Time.time - delayBetweenRebuilds)
            {
                queuedRebuild = true;
                if (sendRepositionOpeningsEvent)
                    queuedSendRepositionOpeningsEvent = true;
                return;
            }

            if (sendRepositionOpeningsEvent)
            {
                //print("call OnRequestRepositionOpenings");
                IBMROS.Core.ScopedRebuild.RepositionOpeningsNear(this); // IBMROS: A7 scoped reposition
            }

            lastRebuild = Time.time;
            queuedRebuild = false;
            queuedSendRepositionOpeningsEvent = false;

            // TO BE EXTENDED
        }
#endif
    }
}
