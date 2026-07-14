using System.Collections;
using UnityEngine;

namespace SilverTau.RoomPlanUnity
{
    public class FlashlightManager : MonoBehaviour
    {
        [Tooltip("This option allows you to automatically turn on/off the phone's flashlight when the light is not sufficient for scanning.")]
        [SerializeField] private bool autoLowLightMode = true;
        [Tooltip("A parameter that indicates the time of the real-world light check. After how many seconds the device's flashlight will turn on.")]
        [SerializeField] private float checkTime = 3.0f;
        [Tooltip("A parameter that indicates how long the flashlight will be on before the next check.")]
        [SerializeField] private float activeTime = 10.0f;

        private CapturedRoom.SessionInstruction _currentSessionInstruction = CapturedRoom.SessionInstruction.None;

        private bool _isFlashlightEnable;

        #region MonoBehaviour

        private void Start()
        {
            
        }

        private void OnEnable()
        {
            if(!autoLowLightMode) return;
            
            RoomPlanUnityKit.captureSessionInstruction += CaptureSessionInstruction;
        }

        private void OnDisable()
        {
            if (_isFlashlightEnable)
            {
                SetFlashlightStatus(false);
                _isFlashlightEnable = false;
            }
            
            if (!autoLowLightMode) return;

            RoomPlanUnityKit.captureSessionInstruction -= CaptureSessionInstruction;
        }

        #endregion

        #region Flashlight process

        private void CaptureSessionInstruction(CapturedRoom.SessionInstruction sessionInstruction)
        {
            _currentSessionInstruction = sessionInstruction;
            
            if(_isFlashlightEnable) return;
            if(_currentSessionInstruction != CapturedRoom.SessionInstruction.TurnOnLight) return;
            
            StartCoroutine(CheckLight());
        }
        
        private IEnumerator CheckLight()
        {
            yield return new WaitForSeconds(checkTime);
            
            if(_currentSessionInstruction != CapturedRoom.SessionInstruction.TurnOnLight) yield break;
            
            if (!_isFlashlightEnable)
            {
                SetFlashlightStatus(true);
            }

            StartCoroutine(WaitNormalLight());
            yield break;
        }
        
        private IEnumerator WaitNormalLight()
        {
            yield return new WaitForSeconds(activeTime);

            if (_currentSessionInstruction == CapturedRoom.SessionInstruction.TurnOnLight)
            {
                StartCoroutine(WaitNormalLight());
                yield break;
            }
            
            SetFlashlightStatus(false);
            yield break;
        }

        #endregion
        
        #region Flashlight methods
        
        /// <summary>
        /// A method that allows you to set the state of the flashlight. It can be used even when running an AR Session.
        /// </summary>
        /// <param name="status">The condition of the flashlight.</param>
        public void SetFlashlightStatus(bool status)
        {
            RoomPlanUnityKit.SetFlashlightStatus(status);
            _isFlashlightEnable = status;
        }

        /// <summary>
        /// A method that allows you to automatically set the state of the flashlight (on/off).
        /// </summary>
        public void FlashlightOnOff()
        {
            _isFlashlightEnable = !_isFlashlightEnable;
            RoomPlanUnityKit.SetFlashlightStatus(_isFlashlightEnable);
        }
        
        #endregion
    }
}