using Exoa.Designer.Utils;
using Exoa.Events;
using Exoa.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Exoa.Designer.DataModel;
using static Exoa.Events.GameEditorEvents;

namespace Exoa.Designer
{
    public class FloorMapSerializer : BaseSerializable, IDataSerializer
    {
        public GameObject controlPointControllerPrefab;
        public GameObject roomControllerPrefab;
        public GameObject outsideControllerPrefab;
        public GameObject openingControllerPrefab;
        private GameObject roomsContainer;
        private GameObject openingsContainer;
        private GameObject controlPointsContainer;
        private Transform globalContainer;
        // IBMROS: A2 — the document state and its floor-level operations now live in
        // FloorPlanDocument (the model); this class keeps scene/UI orchestration only.
        private readonly IBMROS.Core.FloorPlanDocument document = new IBMROS.Core.FloorPlanDocument();
        private UIFloorMapMenu floorMapMenu;
        private UIFloorsMenu floorsMenu;

        /// <summary>IBMROS: A2 — the floor-plan document model this scene is editing.</summary>
        public IBMROS.Core.FloorPlanDocument Document => document;


        override public string GetFolderName() => HDSettings.EXT_FLOORMAP_FOLDER;
        override public GameEditorEvents.FileType GetFileType() => FileType.FloorMapFile;

        void OnDestroy()
        {
            GameEditorEvents.OnFileSaved -= OnFileSaved;
            GameEditorEvents.OnRequestClearAll -= Clear;
            GameEditorEvents.OnRequestFloorAction -= OnRequestFloorActionHandler;
        }
        void Start()
        {
            globalContainer = transform;// GameObject.Find("GlobalContainer").transform;
            floorMapMenu = GameObject.FindObjectOfType<UIFloorMapMenu>();
            floorsMenu = GameObject.FindObjectOfType<UIFloorsMenu>();

            Clear();
            GameEditorEvents.OnFileSaved += OnFileSaved;
            GameEditorEvents.OnRequestClearAll += Clear;
            GameEditorEvents.OnRequestFloorAction += OnRequestFloorActionHandler;
        }

        private void OnRequestFloorActionHandler(FloorAction action, string floorId)
        {
            switch (action)
            {
                case FloorAction.Select: DeserializeFloorMapUI(floorId); break;
                case FloorAction.Add: AddFloor(); break;
                case FloorAction.Duplicate: DuplicateFloor(floorId); break;
                case FloorAction.Remove: RemoveFloor(floorId); break;
            }
            // IBMROS: A2 — the A1 DocumentEvents raise moved into FloorPlanDocument:
            // the model announces its own mutations now.
        }

        public void RemoveFloor(string uniqueId)
        {
            // IBMROS: A2 — delegated to the document model.
            document.RemoveFloor(uniqueId);
        }

        private void OnFileSaved(string fileName, FileType fileType)
        {
            if (fileType != FileType.FloorMapFile)
            {
                return;
            }
            string perspViewName = "";

            // Takes a thumbnail of the current floor map
            if (!string.IsNullOrEmpty(floorsMenu.CurrentFloorId) &&
                    AppController.Instance.State != AppController.States.PreviewBuilding)
            {
                perspViewName = "Floormap_" + floorsMenu.CurrentFloorId + "_persp.png";
                //ThumbnailGeneratorUtils.TakeAndSaveScreenshot(globalContainer, perspViewName, false, new Vector3(1, -1, 1));
                ThumbnailGeneratorUtils.TakeAndSaveScreenshot(GameObject.Find("Rooms").transform, perspViewName, false, new Vector3(1, -1, 1));
                GameEditorEvents.OnScreenShotSaved?.Invoke(perspViewName, MenuType.FloorsMenu);
            }
            // Takes a thumbnail of the current floor map as project image
            //string topViewName = fileName.Replace(".json", "_top.png");
            perspViewName = fileName.Replace(".json", "_persp.png");
            //print("globalContainer:" + globalContainer);
            //ThumbnailGeneratorUtils.TakeAndSaveScreenshot(globalContainer, topViewName, true, Vector3.down);
            GameObject roomsGo = GameObject.Find("Rooms");
            if (roomsGo != null)
                ThumbnailGeneratorUtils.TakeAndSaveScreenshot(roomsGo.transform, "Floormap_" + perspViewName, false, new Vector3(1, -1, 1));



            GameEditorEvents.OnScreenShotSaved?.Invoke(perspViewName, MenuType.FloorMapMenu);
        }

        public bool IsProjectCreatedOrOpened(bool showAlert)
        {
            bool open = document.IsLoaded; // IBMROS: A2
            if (showAlert && !open)
            {
                AlertPopup.ShowAlert("noproject", "No Project", "Please create or open a project first!");
            }
            return open;
        }

        public void AddFloor()
        {
            if (!IsProjectCreatedOrOpened(true))
            {
                return;
            }
            // IBMROS: A2 — floor creation delegated to the document model.
            FloorMapLevel floor = document.AddFloor();
            floorsMenu.CreateNewUIItem(floor);

        }
        public void DuplicateFloor(string uniqueFloorId)
        {
            // First save the current opened floor
            SerializeScene();

            // IBMROS: A2 — duplication delegated to the document model.
            FloorMapLevel floor = document.DuplicateFloor(uniqueFloorId);

            ThumbnailGeneratorUtils.Duplicate("Floormap_" + uniqueFloorId + "_persp", "Floormap_" + floor.uniqueId + "_persp");

            floorsMenu.CreateNewUIItem(floor);
        }

        public FloorMapLevel GetFloorById(string id)
        {
            return document.GetFloorById(id); // IBMROS: A2
        }
        public void Clear(bool clearFloorsUI = true, bool clearFloorMapUI = true, bool clearScene = true)
        {

            if (clearScene)
            {
                HDLogger.Log("Floor Map Serializer Clear", HDLogger.LogCategory.Floormap);

                roomsContainer?.DestroyUniversal();
                openingsContainer?.DestroyUniversal();
                controlPointsContainer?.DestroyUniversal();
                if (globalContainer != transform && globalContainer != null)
                    globalContainer?.gameObject.DestroyUniversal();

                roomsContainer = new GameObject("Rooms");
                openingsContainer = new GameObject("Openings");
                controlPointsContainer = new GameObject("ControlPointsControllers");

                roomsContainer.transform.SetParent(globalContainer);
                openingsContainer.transform.SetParent(globalContainer);
                controlPointsContainer.transform.SetParent(globalContainer);
            }
            if (clearFloorsUI)
            {
                document.Reset(); // IBMROS: A2
            }
        }

        public ControlPointsController CreateSequence()
        {
            GameObject go = Instantiate(controlPointControllerPrefab, controlPointsContainer.transform.position, Quaternion.identity, controlPointsContainer.transform);
            return go.GetComponent<ControlPointsController>();
        }

        public OpeningController CreateOpeningController()
        {
            GameObject go = Instantiate(openingControllerPrefab, openingsContainer.transform.position, Quaternion.identity, openingsContainer.transform);
            OpeningController mpc = go.GetComponent<OpeningController>();

            return mpc;
        }

        public RoomController CreateRoomController()
        {
            GameObject go = Instantiate(roomControllerPrefab, roomsContainer.transform.position, Quaternion.identity, roomsContainer.transform);
            RoomController mpc = go.GetComponent<RoomController>();

            return mpc;
        }

        public OutsideController CreateOutsideController()
        {
            GameObject go = Instantiate(outsideControllerPrefab, roomsContainer.transform.position, Quaternion.identity, roomsContainer.transform);
            OutsideController mpc = go.GetComponent<OutsideController>();

            return mpc;
        }

        override public object DeserializeToScene(string str)
        {
            Clear();
            // IBMROS: A2 — the document model owns parsing/state; UI is built from it.
            document.LoadFromJson(str);
            DeserializeProjectUI(document.Data);
            return document.Data;
        }


        private void DeserializeProjectUI(FloorMapV2 building)
        {
            // Filling settings menu
            if (building.settings.wallsHeight != 0)
            {
                AppController.Instance.SetFloorMapSettings(building.settings);
            }

            // Opening the first floor in Floor Map spaces menu
            if (building.floors != null && building.floors.Count > 0)
            {
                DeserializeFloorMapUI(building.floors[0]);
            }

            // Showing all floors in the Floors menu
            for (int i = 0; i < building.floors.Count; i++)
            {
                FloorMapLevel floor = building.floors[i];
                floor.GenerateUniqueId();
                building.floors[i] = floor;
                floorsMenu.CreateNewUIItem(building.floors[i]);
            }
            //setting the first level as current
            if (building.floors != null && building.floors.Count > 0)
            {
                floorsMenu.CurrentFloorId = building.floors[0].uniqueId;
            }


        }

        public void DeserializeFloorMapUI(string uniqueId)
        {
            DeserializeFloorMapUI(GetFloorById(uniqueId));
        }

        public void DeserializeFloorMapUI(FloorMapLevel floor)
        {
            HDLogger.Log("[FloorMapSerializer] DeserializeFloorMapUI id:" + floor.uniqueId, HDLogger.LogCategory.Floormap);

            FloorController fc = CreateFloorContainer(0);
            globalContainer = fc.transform;

            roomsContainer.transform.SetParent(globalContainer);
            openingsContainer.transform.SetParent(globalContainer);
            controlPointsContainer.transform.SetParent(globalContainer);


            if (floor.spaces != null && floor.spaces.Count > 0)
            {
                foreach (DataModel.FloorMapItem c in floor.spaces)
                {
                    floorMapMenu.CreateNewUIItem(c, c.type);
                }
            }

            // IBMROS: recreate the furniture stored with this floor. Runs after the
            // spaces exist so placement has floors/colliders to land on.
            FurnitureDocumentBridge.Restorer?.Invoke(floor.furniture);
        }

        override public string SerializeScene()
        {
            HDLogger.Log("[FloorMapSerializer] SerializeScene", HDLogger.LogCategory.Floormap);

            // IBMROS: A2 step 1 — this method still *gathers* the floor list and the
            // current floor's spaces from the UI (the UI remains the runtime truth for
            // spaces until A2 step 2 inverts that); the gathered state now enters the
            // document model through its transition seam, and the model serializes.
            List<string> floorIds = floorsMenu.GetItemsData();
            List<FloorMapLevel> newList = new List<FloorMapLevel>();
            if (document.HasFloors)
            {
                for (int i = 0; i < floorIds.Count; i++)
                {
                    FloorMapLevel level = document.GetFloorById(floorIds[i]);
                    // Saving the current floor map

                    if (level.uniqueId == floorsMenu.CurrentFloorId)
                    {
                        level.spaces = floorMapMenu.GetItemsData();
                        level.uniqueId = floorsMenu.CurrentFloorId;
                        // IBMROS: furniture is part of the room the user built, so it
                        // is gathered into the document alongside the spaces. Null
                        // provider (no furniture stack in this scene) leaves whatever
                        // the level already carried, so a plan opened in a
                        // furniture-less context never loses its furnishing.
                        if (FurnitureDocumentBridge.Provider != null)
                            level.furniture = FurnitureDocumentBridge.Provider();
                    }
                    newList.Add(level);
                }
            }
            document.SetFloors(newList);
            document.SetSettings((BuildingSettings)AppController.Instance.GetBuildingSettings());

            return document.ToJson();
        }

        override public bool IsSceneEmpty()
        {
            return document.IsEmpty; // IBMROS: A2
        }
        override public string SerializeEmpty(string name)
        {
            return document.CreateEmpty(); // IBMROS: A2
        }
    }
}
