using System;
using SilverTau.RoomPlanUnity;
using SilverTau.Utilities;
using TMPro;
using UnityEngine;

namespace SilverTau.Sample
{
    public class ModelViewer : MonoBehaviour
    {
        public static ModelViewer Instance { get; set; }
        
        [SerializeField] private MenuUIManager uIManager;
        
        [Space(10)]
        [Header("Common")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private GameObject modelViewer;
        [SerializeField] private Transform modelViewerContainer;
        [SerializeField] private RoomPlanObject prefabRoomPlanObject;
        
        [Space(10)]
        [Header("UI")]
        [SerializeField] private GameObject loader;
        [SerializeField] private TMP_Text textTitle;
        
        public TMP_Text GetTitleElement => textTitle;

        private RoomBuilder _currentRoomBuilder;
        
        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            Init();
        }

        private void Init()
        {
            // Updated: Adds a target RoomPlan for Unity Ki settings to automatically synchronize settings.
            _currentRoomBuilder = new RoomBuilder
            {
                container = modelViewerContainer,
                prefabRoomPlanObject = prefabRoomPlanObject,
                TargetRoomPlanUnityKitSettings = RoomPlanUnityKit.CurrentRoomPlanUnityKitSettings
                // or set:
                //createFloor = true,
                //concavity = 0.5f,
                //typeFloorConstructor = CapturedRoom.TypeFloorConstructor.All,
                //useMeshBooleanOperations = true,
                //meshBooleanOperations = CSG.MeshBooleanOperations.doors | CSG.MeshBooleanOperations.openings
            };
            
            //The RoomBuilder object is added to the inspector's RPU.
#if UNITY_EDITOR
            RPUInspector.AddRoomBuilder(_currentRoomBuilder);
#endif
        }
        
        /// <summary>
        /// A method that opens a model viewer (3D) with a parameter.
        /// </summary>
        /// <param name="element"></param>
        /// <typeparam name="T"></typeparam>
        public void OpenFrom<T>(T element)
        {
            loader.SetActive(true);
            switch (element)
            {
                case null:
                    loader.SetActive(false);
                    return;
                case Scan scan:
                    _currentRoomBuilder.CreateRoomFromSnapshot(scan.snapshot, CreateRoomFromSuccessfully, CreateRoomFromError);
                    return;
                case CapturedStructure capturedStructure:
                    _currentRoomBuilder.CreateRoomFromCapturedStructure(capturedStructure, CreateRoomFromSuccessfully, CreateRoomFromError);
                    return;
                case CapturedRoom capturedRoom:
                    _currentRoomBuilder.CreateRoomFromCapturedRoom(capturedRoom, CreateRoomFromSuccessfully, CreateRoomFromError);
                    return;
            }
        }

        private void CreateRoomFromSuccessfully()
        {
            uIManager.OpenMenu("model-viewer");
            modelViewer.SetActive(true);
            mainCamera.gameObject.SetActive(false);
            loader.SetActive(false);
        }

        private void CreateRoomFromError(string error)
        {
            Debug.Log(error);
            loader.SetActive(false); 
        }
        
        /// <summary>
        /// A method that closes the model viewer with a parameter.
        /// </summary>
        public void Close()
        {
            loader.SetActive(true);
            
            _currentRoomBuilder.Dispose(() =>
            {
                uIManager.OpenMenu("scans");
                modelViewer.SetActive(false);
                mainCamera.gameObject.SetActive(true);
                
                loader.SetActive(false);
            });
        }
    }
}
