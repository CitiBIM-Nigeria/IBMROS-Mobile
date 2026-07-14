using System;

namespace SilverTau.RoomPlanUnity
{
    public class CapturedRoomSnapshot : RoomPlanCapturedSnapshot
    {
        public override void OnEnable()
        {
            base.OnEnable();
            Dispose();
            RoomPlanUnityKit.roomSnapshot += RoomSnapshot;

            if (RoomPlanHelper.AvailableIOS17())
            {
                RoomPlanUnityKit.addedNewRoomToSnapshot += AddedNewRoomToSnapshot;
            }
        }
        
        public override void OnDisable()
        {
            base.OnDisable();
            RoomPlanUnityKit.roomSnapshot -= RoomSnapshot;

            if (RoomPlanHelper.AvailableIOS17())
            {
                RoomPlanUnityKit.addedNewRoomToSnapshot -= AddedNewRoomToSnapshot;
            }
        }
        
        public override void Start()
        {
            base.Start();
            // Updated: Added automatic synchronization of settings with RoomPlan for Unity Kit Settings.
            TargetRoomPlanUnityKitSettings = RoomPlanUnityKit.CurrentRoomPlanUnityKitSettings;
            // or set:
            //createFloor = true;
            //typeFloorConstructor = CapturedRoom.TypeFloorConstructor.All;
            //resetObjectsIfNewRoomIsAdded = true;
        }

        public override void Dispose(Action callback = null)
        {
            base.Dispose(callback);
        }

        public override void EditorRoomSnapshot(string snapshot)
        {
            base.EditorRoomSnapshot(snapshot);
        }

        /// <summary>
        /// A method that allows you to change the prefab RoomPlanObject at any time.
        /// </summary>
        /// <param name="roomPlanObject">Target prefab RoomPlanObject.</param>
        public void SetCapturedRoomObjectPrefab(RoomPlanObject roomPlanObject)
        {
            CapturedRoomObjectPrefab = roomPlanObject;
        }
    }
}