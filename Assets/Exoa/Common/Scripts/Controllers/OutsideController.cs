using Exoa.Events;
using System.Collections.Generic;
using UnityEngine;

namespace Exoa.Designer
{
    public class OutsideController : SpaceController, IObjectDrawer
    {
#if FLOORMAP_MODULE
        public ProceduralOutside proceduralOutside
        {
            get
            {
                return proceduralSpace as ProceduralOutside;
            }

            set
            {
                proceduralSpace = value;
            }
        }


        override protected void Rebuild(bool sendRepositionOpeningsEvent = false)
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


            //print("RebuildMesh sendRepositionOpeningsEvent:" + sendRepositionOpeningsEvent + " roomColor:" + roomCOlor);
            List<Vector3> worldPosList = cpc.GetPointsWorldPositionList();


            if (proceduralSpace == null)
                proceduralSpace = GetComponent<ProceduralOutside>();

            if (worldPosList.Count < 3 || MathUtils.PointsAreInLine(worldPosList))
            {
                proceduralSpace.GenerateEmpty();
                return;
            }

            //print("Room Rebuild");

            proceduralSpace.SpaceVertexColor = DrawingColor;
            proceduralSpace.Generate(worldPosList);

            // IBMROS: A1 — parity with RoomController. Outside areas were the only space
            // type whose rebuild did not announce OnRequestRebuildBuilding, so building-
            // level consumers (exterior shell, capture systems) never heard about them.
            GameEditorEvents.OnRequestRebuildBuilding?.Invoke();
        }
#endif
    }
}
