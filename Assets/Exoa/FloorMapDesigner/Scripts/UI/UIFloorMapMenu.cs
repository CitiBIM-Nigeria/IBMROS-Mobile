
using Exoa.Events;
using Exoa.Maths;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Exoa.Designer
{
    public class UIFloorMapMenu : GenericLeftMenu
    {
        public static UIFloorMapMenu instance;

        [System.Serializable]
        public class MenuItem
        {
            public Button createBtn;
            public GameObject UIItemPrefab;
            public float xPos;
            public DataModel.FloorMapItemType type;
            public FloatSpring posMoveSpring;
        }
        public List<MenuItem> menuItems;


        public float itemOffset = 2;
        public ScrollRect scrollRect;
        public RectTransform containerRect;
        public Transform sceneContainer;
        private List<UIBaseItem> list;
        private FloorMapSerializer serializer;
        public Button createButton;
        public Button exitDrawModeBtn;
        public CanvasGroup canvasGroup;

        private bool isCreateMenuOpen = true;
        public Springs createMenuMove;
        public Springs scrollMove;
        private FloatSpring scrollSpring;
        private float targetScrollY;
        public bool autoScroll;

        void OnDestroy()
        {
            GameEditorEvents.OnRequestClearAll -= OnRequestClearAll;
            AppController.OnAppStateChange -= OnAppStateChange;
        }
        void Awake()
        {
            instance = this;
            serializer = GameObject.FindObjectOfType<FloorMapSerializer>();

            for (int i = 0; i < menuItems.Count; i++)
            {
                MenuItem mi = menuItems[i];
                mi.createBtn.onClick.AddListener(() => CreateNewUIItem(new DataModel.FloorMapItem(), mi.UIItemPrefab, mi.type));
                mi.xPos = mi.createBtn.transform.localPosition.x;
                menuItems[i] = mi;

            }
            createButton.onClick.AddListener(() => ToggleMenu(true));
            exitDrawModeBtn.onClick.AddListener(OnClickExitDrawMode);
            exitDrawModeBtn.gameObject.SetActive(false);

            containerRect.ClearChildren();

            ToggleMenu(false);
        }

        override public void Start()
        {
            base.Start();
            GameEditorEvents.OnRequestClearAll += OnRequestClearAll;
            AppController.OnAppStateChange += OnAppStateChange;
        }



        private void OnClickExitDrawMode()
        {
            DisableDrawModeOnItems();
        }

        override public void Open(bool close = true)
        {
            base.Open(close);
            if (isCreateMenuOpen) ToggleMenu();
        }


        private void ToggleMenu(bool animate = true)
        {
            isCreateMenuOpen = !isCreateMenuOpen;
            for (int i = 0; i < menuItems.Count; i++)
            {
                if (!animate)
                {
                    float targetX = (isCreateMenuOpen ? menuItems[i].xPos : createButton.transform.localPosition.x);
                    menuItems[i].posMoveSpring.Value = targetX;
                    menuItems[i].createBtn.transform.localPosition = menuItems[i].createBtn.transform.localPosition.SetX(targetX);

                }
            }

        }


        public void OnDuplicateUIItem(DataModel.FloorMapItem s, DataModel.FloorMapItemType type)
        {
            DisableDrawModeOnItems();
            s.uniqueId = null; // IBMROS: A2 — a duplicate is a new item; never share identity
            CreateNewUIItem(s, GetUIItemPrefabByType(type), type);
            scrollSpring.Value = scrollRect.verticalNormalizedPosition;
            targetScrollY = 0;
            autoScroll = true;
        }

        private GameObject GetUIItemPrefabByType(DataModel.FloorMapItemType type)
        {
            for (int i = 0; i < menuItems.Count; i++)
            {
                if (menuItems[i].type == type)
                    return menuItems[i].UIItemPrefab;
            }
            return null;
        }



        override protected void Update()
        {
            base.Update();

            if (HDInputs.EscapePressed())
            {
                DisableDrawModeOnItems();
            }
            if (autoScroll)
                scrollRect.verticalNormalizedPosition = scrollMove.Update(ref scrollSpring, targetScrollY, OnAutoScrollComplete);


            for (int i = 0; i < menuItems.Count; i++)
            {
                float targetX = isCreateMenuOpen ? menuItems[i].xPos : createButton.transform.localPosition.x;
                //FloatSpring fs = menuItems[i].posMoveSpring;
                float finalX = createMenuMove.Update(ref menuItems[i].posMoveSpring, targetX);
                menuItems[i].createBtn.transform.localPosition = menuItems[i].createBtn.transform.localPosition.SetX(finalX);
                //menuItems[i].posMoveSpring = fs;
                //Debug.Log("i " + i + " targetX:" + targetX + " finalX:" + finalX);
            }
        }

        private void OnAutoScrollComplete()
        {
            autoScroll = false;
        }

        private UIBaseItem CreateUIItem(GameObject prefab, DataModel.FloorMapItemType type)
        {
            GameObject inst = Instantiate(prefab, containerRect);
            RectTransform r = inst.GetComponent<RectTransform>();
            r.localScale = Vector3.one;
            UIBaseItem plmi = r.GetComponent<UIBaseItem>();
            plmi.sequencingItemType = type;
            return plmi;
        }

        internal void CreateNewUIItem(DataModel.FloorMapItem seq, string sequenceType)
        {
            DataModel.FloorMapItemType type = seq.GetItemType();


            CreateNewUIItem(seq, GetUIItemPrefabByType(type), type);
            DisableDrawModeOnItems();
        }

        public void CreateNewUIItem(DataModel.FloorMapItem seq, GameObject prefab, DataModel.FloorMapItemType type)
        {
            if (!serializer.IsProjectCreatedOrOpened(true))
            {
                return;
            }
            if (isCreateMenuOpen) ToggleMenu();

            UIBaseItem ui = CreateUIItem(prefab, type);
            ControlPointsController cpc = serializer.CreateSequence();
            //print("CreateEmptySequence seq:" + seq.type + " " + type);

            IObjectDrawer drawer = null;

            switch (type)
            {
                case DataModel.FloorMapItemType.Door:
                case DataModel.FloorMapItemType.Window:
                case DataModel.FloorMapItemType.Opening:
                    drawer = serializer.CreateOpeningController();
                    break;
                case DataModel.FloorMapItemType.Room:
                    drawer = serializer.CreateRoomController();
                    break;
                case DataModel.FloorMapItemType.Outside:
                    drawer = serializer.CreateOutsideController();
                    break;
            }

            ui.OnDuplicate += OnDuplicateUIItem;
            ui.OnIsolate += OnIsolate;
            ui.OnDrawModeToggled += OnDrawModeToggled;
            ui.cpc = cpc;
            ui.drawer = drawer;
#if FLOORMAP_MODULE
            drawer.Cpc = cpc;
            drawer.UI = ui;
            // IBMROS: assign a deterministic color from the item's stable identity BEFORE
            // the first build, so every item is always the same color across rebuilds,
            // undo/redo, reconcile-recreate and reload (fixes the color-shifting class of
            // bugs at the root; replaces the transient sibling-index heuristic).
            string ibmrosColorId = !string.IsNullOrEmpty(seq.uniqueId) ? seq.uniqueId : ui.ItemUniqueId;
            cpc.pathColor = IBMROS.Core.ItemColor.ForId(ibmrosColorId, cpc.randomColors);
            ui.colorDisplayed = cpc.pathColor;
            if (drawer != null) drawer.DrawingColor = cpc.pathColor;
            drawer.Build(seq);
            drawer.Init();
#endif
            if (seq.normalizedPositions != null && seq.normalizedPositions.Count > 0)
            {
                ui.SetData(seq);
                uint index = 0;
                foreach (Vector3 normalizedPosition in seq.normalizedPositions)
                {
                    ControlPoint cp = cpc.CreateControlPointBasedOnNormalizedPosition(normalizedPosition,
                        false,
                        index, false);
                    // IBMROS: apply the persisted per-point direction on creation.
                    // CreateControlPointBasedOnNormalizedPosition never set dir, so every
                    // recreated opening started at dir=zero — OpeningController renders
                    // that at the base Euler(0,90,0) (visible "everything rotated 90°"),
                    // and the later re-snap that fixed it also changed the serialized
                    // state AFTER a restore, which spawned a phantom undo step and killed
                    // redo. Setting dir here makes openings render correctly immediately
                    // and keeps serialize(restore(S)) == S (stable history).
                    if (cp != null && seq.directions != null && index < seq.directions.Count)
                        cp.dir = seq.directions[(int)index];
                    index++;
                }
                cpc.CreatePathVisualization();
                cpc.OnControlPointsChanged?.Invoke();

            }
            scrollSpring.Value = scrollRect.verticalNormalizedPosition;
            targetScrollY = 0;
            autoScroll = true;
        }


        private void OnDrawModeToggled(UIBaseItem item, bool active)
        {
            HDLogger.Log("[UIFloorMapMenu] OnDrawModeToggled active:" + active + " item:" + item.sequencingItemType, HDLogger.LogCategory.Floormap);
            DisableDrawModeOnItems(false, active ? item : null);

        }

        internal List<DataModel.FloorMapItem> GetItemsData()
        {
            List<DataModel.FloorMapItem> list = new List<DataModel.FloorMapItem>();
            for (int i = 0; i < containerRect.childCount; i++)
            {
                UIBaseItem item = containerRect.GetChild(i).GetComponent<UIBaseItem>();
                if (item != null)
                {
                    DataModel.FloorMapItem data = item.GetData();
                    // IBMROS: one-opening-per-item leaves a transient 0-point armed opening
                    // while drawing; never serialize/capture it (keeps undo snapshots and
                    // saves clean — no empty openings, no "undo does nothing" steps).
                    bool isOpening = item.sequencingItemType == DataModel.FloorMapItemType.Door ||
                                     item.sequencingItemType == DataModel.FloorMapItemType.Window ||
                                     item.sequencingItemType == DataModel.FloorMapItemType.Opening;
                    if (isOpening && (data.normalizedPositions == null || data.normalizedPositions.Count == 0))
                        continue;
                    list.Add(data);
                }
            }
            return list;
        }


        private void OnAppStateChange(AppController.States state)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = state == AppController.States.PreviewBuilding ? .4f : 1;
                canvasGroup.interactable = state == AppController.States.PreviewBuilding ? false : true;
            }
        }

        private void OnRequestClearAll(bool clearFloorsUI, bool clearFloorMapUI, bool clearScene)
        {
            if (!clearFloorMapUI) return;

            containerRect.ClearChildren();
        }

        private bool isolated;
        private UIBaseItem itemIsolated;

        public void OnIsolate(UIBaseItem item, bool active)
        {
            if (itemIsolated == null || itemIsolated == item)
            {
                isolated = !isolated;
            }
            else
            {
                isolated = true;
            }
            HideSequences(!isolated);
            itemIsolated = item;
        }

        public void DisableDrawModeOnItems(bool drawMode = false, UIBaseItem exceptItem = null)
        {
            bool disableAll = !drawMode && exceptItem == null;
            HDLogger.Log("[UIFloorMapMenu] DisableDrawModeOnItems drawMode:" + drawMode + " disableAll:" + disableAll, HDLogger.LogCategory.Floormap);
            AppController.Instance.State = !disableAll ? AppController.States.Draw : AppController.States.Idle;
            exitDrawModeBtn.gameObject.SetActive(!disableAll);

            UIBaseItem[] items = null;
            items = GameObject.FindObjectsOfType<UIBaseItem>();
            if (disableAll && items.Length > 0)
            {
                items[0].cpc.HideGhost();
            }
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != exceptItem)
                {
                    items[i].ToggleDrawMode(drawMode, false, false);
                }
            }

            // IBMROS: one-opening-per-item leaves a freshly-armed (0-point) opening at the
            // end of a drawing session; remove it when drawing fully ends so no empty
            // opening is saved. Only empty OPENINGS (not partial rooms).
            if (disableAll)
                RemoveEmptyOpenings(items);
        }

        // IBMROS: delete opening items that never received a point (the trailing armed
        // item from single-point opening drawing).
        private void RemoveEmptyOpenings(UIBaseItem[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                UIBaseItem it = items[i];
                if (it == null || it.cpc == null)
                    continue;
                bool isOpening = it.sequencingItemType == DataModel.FloorMapItemType.Door ||
                                 it.sequencingItemType == DataModel.FloorMapItemType.Window ||
                                 it.sequencingItemType == DataModel.FloorMapItemType.Opening;
                if (isOpening && it.cpc.GetPointsList().Count == 0)
                    it.Delete();
            }
        }

        // IBMROS: one-opening-per-item. After a point is placed on an opening (single-point
        // mode), arm a fresh opening of the SAME type so the next tap starts a new item.
        public void ContinueDrawingNewOpening(ControlPointsController fromCpc)
        {
            DataModel.FloorMapItemType type = DataModel.FloorMapItemType.Door;
            bool found = false;
            foreach (UIBaseItem it in GameObject.FindObjectsOfType<UIBaseItem>())
            {
                if (it.cpc == fromCpc)
                {
                    type = it.sequencingItemType;
                    found = true;
                    break;
                }
            }
            if (!found)
                return;
            CreateNewUIItem(new DataModel.FloorMapItem(), GetUIItemPrefabByType(type), type);
        }

        public void HideSequences(bool displayed = false)
        {
            for (int i = 0; i < containerRect.childCount; i++)
            {
                UIBaseItem item = containerRect.GetChild(i).GetComponent<UIBaseItem>();
                if (item != null)
                {
                    item.SetDisplayed(displayed);
                }
            }
        }
    }
}
