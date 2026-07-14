using System;
using UnityEngine;

namespace SilverTau.RoomPlanUnity
{
    public class RPUSessionCamera : SessionCamera
    {
        [Tooltip("The current session camera.")]
        [SerializeField] private Camera ARCamera;
        
        /// <summary>
        /// Customize the current session camera.
        /// </summary>
        public Camera CurrentARCamera
        {
            get { return ARCamera; }
            set { SetNewCamera(value); }
        }

        /// <summary>
        /// An option that lets you set or get the current session camera.
        /// </summary>
        public static RPUSessionCamera CurrentSessionCamera { get; set; }

        private void Awake()
        {
            CurrentSessionCamera = this;
        }

        private void OnEnable()
        {
            if(RoomPlanUnityKit.CurrentRoomPlanUnityKitSettings.isCustomXREnabled) return;
            
#if UNITY_EDITOR
            if (RoomPlanUnityKit.CurrentRoomPlanUnityKitSettings.editorAR)
            {
                var editorArCamera = CurrentARCamera.gameObject.AddComponent<EditorARCamera>();
                editorArCamera.cameraAR = CurrentARCamera;
                editorArCamera.canMove = true;
                editorArCamera.canRotate = true;
            }
#endif
            UpdateProjection(CurrentARCamera);
        }

        /// <summary>
        /// An additional method of installing a new camera.
        /// </summary>
        /// <param name="newCamera">Target camera.</param>
        public void SetNewCamera(Camera newCamera)
        {
            if(ARCamera == newCamera) return;
            ARCamera = newCamera;
            
            if(RoomPlanUnityKit.CurrentRoomPlanUnityKitSettings != null && RoomPlanUnityKit.CurrentRoomPlanUnityKitSettings.isCustomXREnabled) return;
            UpdateProjection(newCamera);
        }
        
        private void Update()
        {
            if (CurrentARCamera == null) return;
            
            if(RoomPlanUnityKit.CurrentRoomPlanUnityKitSettings.isCustomXREnabled) return;
            if(!RoomPlanUnityKit.RPUCaptureSessionActive) return;
            UpdateCameraTRS(CurrentARCamera, RoomPlanUnityKit.GetCameraTransformDataRuntime(), RoomPlanUnityKit.GetCameraProjectionDataRuntime());
        }
    }
}
