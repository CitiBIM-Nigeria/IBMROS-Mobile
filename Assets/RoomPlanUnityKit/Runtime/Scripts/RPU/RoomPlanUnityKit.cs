using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

#if !UNITY_EDITOR && PLATFORM_IOS
using System.IO;
using System.Runtime.InteropServices;
using AOT;
#endif

namespace SilverTau.RoomPlanUnity
{
    public class RoomPlanUnityKit : MonoBehaviour
    {
        
        #region RPU main
#if !UNITY_EDITOR && PLATFORM_IOS
        [DllImport("__Internal")]
        private static extern void prepareRPUResources();

        [DllImport("__Internal")]
        private static extern void initializeRPU();
        
        [DllImport("__Internal")]
        private static extern void disposeRPU();
#endif
        #endregion

        #region RPU RoomPlan
#if !UNITY_EDITOR && PLATFORM_IOS
        [DllImport("__Internal")]
        private static extern void startCaptureSession();
        
        [DllImport("__Internal")]
        private static extern void runCaptureSession();
        
        [DllImport("__Internal")]
        private static extern void stopCaptureSession();
        
        [DllImport("__Internal")]
        private static extern void stopCaptureSessionWithPause(bool pauseARSession);
        
        [DllImport("__Internal")]
        private static extern void pauseCaptureSessionScanning();
        
        [DllImport("__Internal")]
        private static extern void resumeCaptureSessionScanning();
        
        [DllImport("__Internal")]
        private static extern void saveRoomPlanExperience(String directoryName, String scanName);
        
        [DllImport("__Internal")]
        private static extern bool trySaveRoomPlanExperience(String directoryName, String scanName);
        
        [DllImport("__Internal")]
        private static extern void isRPUDefaultScanEnabled(bool value);
        
        [DllImport("__Internal")]
        private static extern void isRPUDefaultMiniatureModelEnabled(bool value);
        
        [DllImport("__Internal")]
        private static extern void isRPUDefaultScanDataTransferEnabled(bool value);

        [DllImport("__Internal")]
        private static extern void isRPUCoachingEnabled(bool value);
        
        [DllImport("__Internal")]
        private static extern void isRPUInstructionEnabled(bool value);
#endif
        
        #region Merge
        
#if !UNITY_EDITOR && PLATFORM_IOS
        [DllImport("__Internal")]
        private static extern void mergeScans(String data, String directoryName, String scanName);
#endif
        
        #endregion

        #region MultiRoom

#if !UNITY_EDITOR && PLATFORM_IOS
        [DllImport("__Internal")]
        private static extern void isRPUMultiRoomEnabled(bool value);
        
        [DllImport("__Internal")]
        private static extern void createNewRoomCaptureSession();
#endif
        #endregion

        #region ARWorldMap
#if !UNITY_EDITOR && PLATFORM_IOS
        [DllImport("__Internal")]
        private static extern void isRPUARWorldMapEnabled(bool value);

        [DllImport("__Internal")]
        private static extern void startCaptureSessionWithARWorldMap(String path);

        [DllImport("__Internal")]
        private static extern void stopCaptureSessionWithARWorldMap(String path);
#endif
        #endregion
        
        #endregion

        #region RPU Get
#if !UNITY_EDITOR && PLATFORM_IOS
        [DllImport("__Internal")]
        private static extern string getRoomPlanUnityKitVersion();

        [DllImport("__Internal")]
        private static extern bool RoomPlanUnityKitSupport();
        
        [DllImport("__Internal")]
        private static extern string getRPUARCameraMatrixRuntime();
        
        [DllImport("__Internal")]
        private static extern string getRPUARCameraProjectionMatrixRuntime();
#endif
        #endregion
        
        #region Delegates
#if !UNITY_EDITOR && PLATFORM_IOS
        public delegate void RPUDidStartDelegate();
        [DllImport("__Internal")]
        private static extern void setRPUDidStart(RPUDidStartDelegate callBack);

        public delegate void RPUDidEndDelegate();
        [DllImport("__Internal")]
        private static extern void setRPUDidEnd(RPUDidEndDelegate callBack);
        
        public delegate void RPUCaptureSessionDidStartDelegate();
        [DllImport("__Internal")]
        private static extern void setRPUCaptureSessionDidStart(RPUCaptureSessionDidStartDelegate callBack);
        
        public delegate void RPUCaptureSessionDidEndDelegate();
        [DllImport("__Internal")]
        private static extern void setRPUCaptureSessionDidEnd(RPUCaptureSessionDidEndDelegate callBack);
        
        public delegate void RPUCaptureSessionDidAddDelegate();
        [DllImport("__Internal")]
        private static extern void setRPUCaptureSessionDidAdd(RPUCaptureSessionDidAddDelegate callBack);
        
        public delegate void RPUCaptureSessionDidUpdateDelegate();
        [DllImport("__Internal")]
        private static extern void setRPUCaptureSessionDidUpdate(RPUCaptureSessionDidUpdateDelegate callBack);
        
        public delegate void RPUCaptureSessionDidChangeDelegate();
        [DllImport("__Internal")]
        private static extern void setRPUCaptureSessionDidChange(RPUCaptureSessionDidChangeDelegate callBack);
        
        public delegate void RPUCaptureSessionDidRemoveDelegate();
        [DllImport("__Internal")]
        private static extern void setRPUCaptureSessionDidRemove(RPUCaptureSessionDidRemoveDelegate callBack);
        
        public delegate void RPUCaptureSessionInstructionDelegate(String value);
        [DllImport("__Internal")]
        private static extern void setRPUCaptureSessionInstruction(RPUCaptureSessionInstructionDelegate callBack);
        
        public delegate void RPURoomSnapshotDelegate(String value);
        [DllImport("__Internal")]
        private static extern void setRPURoomSnapshot(RPURoomSnapshotDelegate callBack);
#endif
        #endregion

        #region RPU Flashlight
#if !UNITY_EDITOR && PLATFORM_IOS
        [DllImport("__Internal")]
        private static extern void setFlashlightStatus(bool value);
#endif
        #endregion
        
        #region RPU XR
#if !UNITY_EDITOR && PLATFORM_IOS
        [DllImport("__Internal")]
        private static extern void isRPUCustomXREnabled(bool value);
        
        [DllImport("__Internal")]
        private static extern void RoomPlanUnityKit_Сombine_XRSession(IntPtr ptr);
#endif
        #endregion
        
        #region UnityActions
        
        /// <summary>
        /// Called when the framework is initialized.
        /// </summary>
        public static UnityAction didStart;
        
        /// <summary>
        /// Called when the framework is disposed of.
        /// </summary>
        public static UnityAction didEnd;
        
        /// <summary>
        /// Called when the scanning session starts.
        /// </summary>
        public static UnityAction captureSessionDidStart;
        
        /// <summary>
        /// Called when the scan session ends.
        /// </summary>
        public static UnityAction captureSessionDidEnd;
        
        public static UnityAction captureSessionDidAdd;
        public static UnityAction captureSessionDidUpdate;
        public static UnityAction captureSessionDidChange;
        public static UnityAction captureSessionDidRemove;
        
        /// <summary>
        /// Provides scan session instructions for the user.
        /// </summary>
        public static UnityAction<CapturedRoom.SessionInstruction> captureSessionInstruction;
        
        /// <summary>
        /// Called when a session submits a snapshot to the Unity Engine.
        /// </summary>
        public static UnityAction<String> roomSnapshot;

        /// <summary>
        /// 
        /// </summary>
        public static UnityAction addedNewRoomToSnapshot;
        
        /// <summary>
        /// Called when the session status is changed.
        /// </summary>
        public static UnityAction<CapturedRoom.CaptureStatus> captureStatus;
        #endregion
        
        [SerializeField] private RoomPlanUnityKitSettings kitSettings;

        /// <summary>
        /// An option that allows you to set or get the current settings of the framework.
        /// </summary>
        public static RoomPlanUnityKitSettings CurrentRoomPlanUnityKitSettings { get; set; }

        private static CapturedRoom.CaptureStatus _captureStatus = CapturedRoom.CaptureStatus.None;
        
        /// <summary>
        /// A parameter that informs about the current status of the session.
        /// </summary>
        public static CapturedRoom.CaptureStatus CurrentCaptureStatus
        {
            get => _captureStatus;
            set => OnChangedCaptureStatus(value);
        }
        
        public static bool debug = false;
        
        [Tooltip("If the parameter is enabled, initialization will occur only when you initialize the framework manually.")]
        [SerializeField] private bool delayInitialization = false;
        
        [Tooltip("This helps to optimize the performance of the application and the framework, as well as to set the desired frame rate.")]
        [SerializeField] private bool matchFrameRate = true;
        [SerializeField] private bool useWaitForNextFrame = false;
        [SerializeField] private int targetFrameRate = 60;

        /// <summary>
        /// Value that sets the target frame rate in real time. If you use useWaitForNextFrame!
        /// </summary>
        public int ApplicationTargetFrameRate
        {
            get => targetFrameRate;
            set => SetTargetFrameRate(value);
        }
        
        private float currentFrameTime;
        
        private static float[] aRCameraMatrix;

        /// <summary>
        /// Values to check for RoomPlan support.
        /// </summary>
        public static bool RPUSupport => CheckRPUSupport();
        
        private static bool CheckRPUSupport()
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            var result = RoomPlanUnityKitSupport();
            if (debug) Debug.Log($"RoomPlan Unity Kit Support = {result}.");
            return result;
#elif !UNITY_EDITOR && PLATFORM_ANDROID //FOR THE TEST APPLICATION ONLY
            if (debug) Debug.Log("RoomPlan Unity Kit Support = PLATFORM ANDROID (FOR THE TEST APPLICATION ONLY).");
            return true;
#else
            if (debug) Debug.Log("RoomPlan Unity Kit = Unity Editor.");
            return false;
#endif
        }

        /// <summary>
        /// An option that allows you to set or get the current status of the session, whether it is active.
        /// </summary>
        public static bool RPUActive { get; private set; } = false;
        
        /// <summary>
        /// An option that allows you to set or get the current scanning status of the session, whether it is active.
        /// </summary>
        public static bool RPUCaptureSessionActive { get; private set; } = false;
        
        private void Awake()
        {
            if (matchFrameRate)
            {
                QualitySettings.vSyncCount = 0;
                
                if (useWaitForNextFrame)
                {
                    Application.targetFrameRate = (int)targetFrameRate;
                    currentFrameTime = Time.realtimeSinceStartup;
                    StartCoroutine(WaitForNextFrame());
                }
                else
                {
                    Application.targetFrameRate = (int)targetFrameRate;
                }
            }
            
            CurrentRoomPlanUnityKitSettings = kitSettings;
            
            if(delayInitialization) return;
            PrepareRoomPlanForUnityKit();
        }
        
        private void Start()
        {
            if(delayInitialization) return;
            Initialize();
        }
        
        /// <summary>
        /// A method that sets the target frame rate.
        /// </summary>
        /// <param name="value">Frame rate value.</param>
        private void SetTargetFrameRate(float value)
        {
            QualitySettings.vSyncCount = 0;
            
            if (useWaitForNextFrame)
            {
                targetFrameRate = (int)value;
            }
            
            Application.targetFrameRate = (int)value;
        }

        private IEnumerator WaitForNextFrame()
        {
            while (true)
            {
                yield return new WaitForEndOfFrame();
                currentFrameTime += 1.0f / targetFrameRate;
                var t = Time.realtimeSinceStartup;
                var sleepTime = currentFrameTime - t - 0.01f;
                if (sleepTime > 0)
                    Thread.Sleep((int)(sleepTime * 1000));
                while (t < currentFrameTime)
                    t = Time.realtimeSinceStartup;
            }
        }

        private void PrepareRoomPlanForUnityKit()
        {
            if (RPUActive) return;

#if !UNITY_EDITOR && PLATFORM_IOS
            isRPUDefaultScanEnabled(CurrentRoomPlanUnityKitSettings.isDefaultScanEnabled);
            isRPUDefaultMiniatureModelEnabled(CurrentRoomPlanUnityKitSettings.isDefaultMiniatureModelEnabled);
            isRPUDefaultScanDataTransferEnabled(CurrentRoomPlanUnityKitSettings.isDefaultScanDataTransferEnabled);
            isRPUCustomXREnabled(CurrentRoomPlanUnityKitSettings.isCustomXREnabled); 

            prepareRPUResources();
#endif
        }

        /// <summary>
        /// A method that initializes RoomPlan for Unity.
        /// </summary>
        public void Initialize()
        {
            if (RPUActive) return;
#if UNITY_EDITOR
            
            CurrentCaptureStatus = CapturedRoom.CaptureStatus.None;
            RPUActive = true;
#elif !UNITY_EDITOR && PLATFORM_IOS
            isRPUDefaultScanEnabled(CurrentRoomPlanUnityKitSettings.isDefaultScanEnabled);
            isRPUDefaultMiniatureModelEnabled(CurrentRoomPlanUnityKitSettings.isDefaultMiniatureModelEnabled);
            isRPUDefaultScanDataTransferEnabled(CurrentRoomPlanUnityKitSettings.isDefaultScanDataTransferEnabled);
            isRPUCustomXREnabled(CurrentRoomPlanUnityKitSettings.isCustomXREnabled);

            initializeRPU();

            if (!RPUSupport)
            {
                if (debug)
                {
                    Debug.Log("The camera is disabled because RoomPlan Unity Kit does not support your current platform.");
                }
                
                CurrentCaptureStatus = CapturedRoom.CaptureStatus.None;
                RPUActive = false;
                return;
            }
            
            setRPUDidStart(RPUDidStart);
            setRPUDidEnd(RPUDidEnd);

            setRPUCaptureSessionDidStart(RPUCaptureSessionDidStart);
            setRPUCaptureSessionDidEnd(RPUCaptureSessionDidEnd);
            setRPUCaptureSessionDidAdd(RPUCaptureSessionDidAdd);
            setRPUCaptureSessionDidUpdate(RPUCaptureSessionDidUpdate);
            setRPUCaptureSessionDidChange(RPUCaptureSessionDidChange);
            setRPUCaptureSessionDidRemove(RPUCaptureSessionDidRemove);
            setRPUCaptureSessionInstruction(RPUCaptureSessionInstruction);
            setRPURoomSnapshot(RPURoomSnapshot);
            
            CurrentCaptureStatus = CapturedRoom.CaptureStatus.None;
            RPUActive = true;
#else
            if (debug) Debug.Log("RoomPlan Unity Kit = OTHER PLATFORM. Not supported!");
#endif
        }
        
        /// <summary>
        /// A method that disposes of a RoomPlan.
        /// </summary>
        public void Dispose()
        {
            if (!RPUActive)
            {
                return;
            }
            
#if !UNITY_EDITOR && PLATFORM_IOS
            disposeRPU();
#endif
            
            CurrentCaptureStatus = CapturedRoom.CaptureStatus.None;
            RPUActive = false;
        }

        /// <summary>
        /// A method that updates the main RoomPlan settings in real time.
        /// </summary>
        public void UpdateRPUKitSettings()
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            isRPUDefaultScanEnabled(CurrentRoomPlanUnityKitSettings.isDefaultScanEnabled);
            isRPUDefaultMiniatureModelEnabled(CurrentRoomPlanUnityKitSettings.isDefaultMiniatureModelEnabled);
            isRPUDefaultScanDataTransferEnabled(CurrentRoomPlanUnityKitSettings.isDefaultScanDataTransferEnabled);
            isRPUCustomXREnabled(CurrentRoomPlanUnityKitSettings.isCustomXREnabled);
#endif
        }

        private static void OnChangedCaptureStatus(CapturedRoom.CaptureStatus cs)
        {
            _captureStatus = cs;
            captureStatus?.Invoke(cs);
        }
        
        #region CaptureSession

        /// <summary>
        /// A method that starts a RoomPlan scanning session.
        /// </summary>
        public void StartCaptureSession()
        {
            if (!RPUActive)
            {
                return;
            }
            
            if (!RPUSupport)
            {
                return;
            }
            
#if !UNITY_EDITOR && PLATFORM_IOS
            isRPUCoachingEnabled(CurrentRoomPlanUnityKitSettings.isCoachingEnabled);
            isRPUInstructionEnabled(CurrentRoomPlanUnityKitSettings.isInstructionEnabled);
            isRPUCustomXREnabled(CurrentRoomPlanUnityKitSettings.isCustomXREnabled);

            startCaptureSession();
#endif
            
            CurrentCaptureStatus = CapturedRoom.CaptureStatus.Processing;
            RPUCaptureSessionActive = true;
        }
        
        /// <summary>
        /// Starts a room capture session with the specified configuration after the session was stopped with a pause (StopCaptureSessionWithPause).
        /// </summary>
        public void RunCaptureSession()
        {
            if (!RPUActive)
            {
                return;
            }
            
            if (!RPUSupport)
            {
                return;
            }
            
            if (RPUCaptureSessionActive)
            {
                return;
            }
            
#if !UNITY_EDITOR && PLATFORM_IOS
            isRPUCoachingEnabled(CurrentRoomPlanUnityKitSettings.isCoachingEnabled);
            isRPUInstructionEnabled(CurrentRoomPlanUnityKitSettings.isInstructionEnabled);
            isRPUCustomXREnabled(CurrentRoomPlanUnityKitSettings.isCustomXREnabled);

            runCaptureSession();
#endif
        }
        
        /// <summary>
        /// A method that stops a RoomPlan scanning session.
        /// </summary>
        public void StopCaptureSession()
        {
            if (!RPUActive)
            {
                return;
            }
            
#if !UNITY_EDITOR && PLATFORM_IOS
            stopCaptureSession();
#endif
            CurrentCaptureStatus = CapturedRoom.CaptureStatus.None;
            RPUCaptureSessionActive = false;
        }
        
        /// <summary>
        /// Stops the room capture session with the specified AR session parameter. Used to be able to stop the session and add a new scanning room.
        /// </summary>
        /// <param name="pauseARSession">AR session status.</param>
        public void StopCaptureSessionWithPause(bool pauseARSession)
        {
            if (!RPUActive)
            {
                return;
            }
            
#if !UNITY_EDITOR && PLATFORM_IOS
            stopCaptureSessionWithPause(pauseARSession);
#endif
        }
        
        /// <summary>
        /// A method that paused a RoomPlan scanning session.
        /// </summary>
        public void PauseCaptureSessionScanning()
        {
            if (!RPUActive)
            {
                return;
            }
            
            if (!RPUCaptureSessionActive)
            {
                return;
            }
            
#if !UNITY_EDITOR && PLATFORM_IOS
            pauseCaptureSessionScanning();
#endif
            CurrentCaptureStatus = CapturedRoom.CaptureStatus.Paused;
        }
        
        /// <summary>
        /// A method that resumed a RoomPlan scanning session.
        /// </summary>
        public void ResumeCaptureSessionScanning()
        {
            if (!RPUActive)
            {
                return;
            }
            
#if !UNITY_EDITOR && PLATFORM_IOS
            resumeCaptureSessionScanning();
#endif
            CurrentCaptureStatus = CapturedRoom.CaptureStatus.Scanning;
        }
        
        /// <summary>
        /// A method that allows you to preserve the scanning experience.
        /// </summary>
        /// <param name="scanName">Scan name.</param>
        /// <param name="directoryName">The name of the catalog. The main directory for saving files.</param>
        public void SaveRoomPlanExperience(string scanName = "My new scan", string directoryName = "model-viewer")
        {
            if (string.IsNullOrEmpty(directoryName))
            {
                directoryName = "model-viewer";
            }
            
            if (string.IsNullOrEmpty(scanName))
            {
                scanName = "My new scan";
            }
            
#if !UNITY_EDITOR && PLATFORM_IOS
            saveRoomPlanExperience(directoryName, scanName);
#endif
        }
        
        /// <summary>
        /// A method that allows you to preserve the scanning experience.
        /// </summary>
        /// <param name="scanName">Scan name.</param>
        /// <param name="directoryName">Directory name.</param>
        public bool TrySaveRoomPlanExperience(string scanName = "My new scan", string directoryName = "model-viewer")
        {
            if (string.IsNullOrEmpty(directoryName))
            {
                directoryName = "model-viewer";
            }
            
            if (string.IsNullOrEmpty(scanName))
            {
                scanName = "My new scan";
            }
            
#if !UNITY_EDITOR && PLATFORM_IOS
            return trySaveRoomPlanExperience(directoryName, scanName);
#endif
            return false;
        }
        
        #endregion
        
        #region MultiRoom

        public void CreateNewRoomCaptureSession()
        {
            if (!RPUActive) return;
            addedNewRoomToSnapshot?.Invoke();
#if !UNITY_EDITOR && PLATFORM_IOS
            createNewRoomCaptureSession();
#endif
        }

        #endregion
        
        #region Merged Scans

        public void MergeScans(CapturedRoom[] input, string scanName = "My new scan", string directoryName = "model-viewer")
        {
            if (!RPUActive)
            {
                return;
            }

            if (!RoomPlanHelper.AvailableIOS17())
            {
                Debug.Log("This feature is only available for iOS 17+.");
                return;
            }

            var data = input.ExportForMerge();
            
#if !UNITY_EDITOR && PLATFORM_IOS
            mergeScans(data, directoryName, scanName);
#endif
        }

        #endregion
        
        #region Flashlight
        
        /// <summary>
        /// A method that allows you to set the state of the flashlight. It can be used even when running an AR Session. 
        /// </summary>
        /// <param name="status">The condition of the flashlight.</param>
        public static void SetFlashlightStatus(bool status)
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            setFlashlightStatus(status);
#endif
        }

        #endregion

        #region Camera

        /// <summary>
        /// A method that processes the CVMatrix from the camera.
        /// </summary>
        /// <returns></returns>
        public static string GetCameraTransformDataRuntime()
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            return getRPUARCameraMatrixRuntime();
#endif
            return null;
        }
        
        /// <summary>
        /// A method that processes the CVProjection from the camera.
        /// </summary>
        /// <returns></returns>
        public static string GetCameraProjectionDataRuntime()
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            return getRPUARCameraProjectionMatrixRuntime();
#endif
            return null;
        }

        #endregion

        #region XR

        /// <summary>
        /// This is a function for ARSession (subsystem) that combines work sessions in runtime.
        /// </summary>
        /// <param name="nativeXRSession">Native AR Session.</param>
        public void CombineRoomPlanUnityKitXRSession(IntPtr nativeXRSession)
        {
            if(!CurrentRoomPlanUnityKitSettings.isCustomXREnabled) return;
            
#if !UNITY_EDITOR && PLATFORM_IOS
            RoomPlanUnityKit_Сombine_XRSession(nativeXRSession);
#endif
        }
        
        #endregion

        #region Handle Delegates
#if !UNITY_EDITOR && PLATFORM_IOS
        [MonoPInvokeCallback(typeof(RPUDidStartDelegate))]
        public static void RPUDidStart()
        {
            if (debug) Debug.Log("RoomPlan Unity Kit - Did start");
            didStart?.Invoke();
        }

        [MonoPInvokeCallback(typeof(RPUDidEndDelegate))]
        public static void RPUDidEnd()
        {
            if (debug) Debug.Log("RoomPlan Unity Kit - Did end");
            didEnd?.Invoke();
        }
        
        [MonoPInvokeCallback(typeof(RPUCaptureSessionDidStartDelegate))]
        public static void RPUCaptureSessionDidStart()
        {
            if (debug) Debug.Log("RoomPlan Unity Kit - Did Capture Session Start");
            captureSessionDidStart?.Invoke();
        }
        
        [MonoPInvokeCallback(typeof(RPUCaptureSessionDidEndDelegate))]
        public static void RPUCaptureSessionDidEnd()
        {
            if (debug) Debug.Log("RoomPlan Unity Kit - Did Capture Session End");
            captureSessionDidEnd?.Invoke();
        }

        [MonoPInvokeCallback(typeof(RPUCaptureSessionDidAddDelegate))]
        public static void RPUCaptureSessionDidAdd()
        {
            if (debug) Debug.Log("RoomPlan Unity Kit - Did Capture Session Add");
            captureSessionDidAdd?.Invoke();
        }

        [MonoPInvokeCallback(typeof(RPUCaptureSessionDidUpdateDelegate))]
        public static void RPUCaptureSessionDidUpdate()
        {
            if (debug) Debug.Log("RoomPlan Unity Kit - Did Capture Session Update");
            captureSessionDidUpdate?.Invoke();
        }

        [MonoPInvokeCallback(typeof(RPUCaptureSessionDidChangeDelegate))]
        public static void RPUCaptureSessionDidChange()
        {
            if (debug) Debug.Log("RoomPlan Unity Kit - Did Capture Session Change");
            captureSessionDidChange?.Invoke();
        }

        [MonoPInvokeCallback(typeof(RPUCaptureSessionDidRemoveDelegate))]
        public static void RPUCaptureSessionDidRemove()
        {
            if (debug) Debug.Log("RoomPlan Unity Kit - Did Capture Session Remove");
            captureSessionDidRemove?.Invoke();
        }
        
        [MonoPInvokeCallback(typeof(RPUCaptureSessionInstructionDelegate))]
        public static void RPUCaptureSessionInstruction(String value)
        {
            if (debug) Debug.Log("RoomPlan Unity Kit - Capture Session Instruction: " + value);

            if (Enum.TryParse(value, true, out CapturedRoom.SessionInstruction instruction))
            {
                captureSessionInstruction?.Invoke(instruction);
            }
        }
        
        [MonoPInvokeCallback(typeof(RPURoomSnapshotDelegate))]
        public static void RPURoomSnapshot(String value)
        {
            if (debug) Debug.Log("RoomPlan Unity Kit - Room Snapshot");

            if (CurrentCaptureStatus != CapturedRoom.CaptureStatus.Scanning)
            {
                CurrentCaptureStatus = CapturedRoom.CaptureStatus.Scanning;
            }

            if (CurrentRoomPlanUnityKitSettings.isDefaultScanEnabled)
            {
                if(!CurrentRoomPlanUnityKitSettings.isDefaultScanDataTransferEnabled) return;
            }
            
            roomSnapshot?.Invoke(value);
        }
#endif
        #endregion
    }
}