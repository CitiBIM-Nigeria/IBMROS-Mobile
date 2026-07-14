using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace SilverTau.RoomPlanUnity
{
    public class RPUARChecker : MonoBehaviour
    {
        [SerializeField] private float delayTime = 15.0f;
        [Space]
        [SerializeField] private UnityEvent onDidNotScanning;

        private bool _isCaptureSessionDidUpdate = false;

        #region MonoBehaviour
        
        private void Start() { }

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (RoomPlanUnityKit.CurrentRoomPlanUnityKitSettings.editorAR)
            {
                return;
            }
#endif
            
            RoomPlanUnityKit.captureSessionDidUpdate += CaptureSessionDidUpdate;
            StartCoroutine(CheckLIDAR());
        }

        private void OnDisable()
        {
            RoomPlanUnityKit.captureSessionDidUpdate -= CaptureSessionDidUpdate;
            _isCaptureSessionDidUpdate = false;
        }

        #endregion

        #region Check LIDAR

        private void CaptureSessionDidUpdate()
        {
            if (_isCaptureSessionDidUpdate) return;
            StopAllCoroutines();
            _isCaptureSessionDidUpdate = true;
        }
        
        private IEnumerator CheckLIDAR()
        {
            yield return new WaitForSeconds(delayTime);
            
            if(_isCaptureSessionDidUpdate) yield break;
            
            Debug.Log("It seems some issues with scanning.\nPlease, check your LiDAR sensor or the lighting in the room.");
            onDidNotScanning?.Invoke();
            yield break;
        }

        #endregion
    }
}