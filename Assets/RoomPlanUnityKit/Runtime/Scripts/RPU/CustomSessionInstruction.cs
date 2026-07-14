using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SilverTau.RoomPlanUnity
{
    [Serializable]
    public class SessionInstructionText
    {
        public CapturedRoom.SessionInstruction sessionInstruction;
        [TextArea] public string text;
    }
    
    public class CustomSessionInstruction : MonoBehaviour
    {
        [Tooltip("This is the UI text element to which the session instruction will be passed.")]
        [SerializeField] private Text textCustomSessionInstruction;
        [Tooltip("This is a parameter that indicates how long the session instruction notification will be displayed.")]
        [SerializeField] private float delayTime = 3.0f;
        [Tooltip("This is the email parameter where you can specify custom text for each category of instructions.")]
        [SerializeField] private List<SessionInstructionText> sessionInstructionText = new List<SessionInstructionText>();
        
        private bool _isSessionInstructionEnable;

        #region MonoBehaviour

        private void Start() { }

        private void OnEnable()
        {
            if (textCustomSessionInstruction != null)
            {
                textCustomSessionInstruction.text = string.Empty;
                textCustomSessionInstruction.gameObject.SetActive(false);
            }
            
            RoomPlanUnityKit.captureSessionInstruction += CaptureSessionInstruction;
        }

        private void OnDisable()
        {
            _isSessionInstructionEnable = false;
            RoomPlanUnityKit.captureSessionInstruction -= CaptureSessionInstruction;
        }

        #endregion

        #region Capture Session Instruction process

        private void CaptureSessionInstruction(CapturedRoom.SessionInstruction sessionInstruction)
        {
            if(_isSessionInstructionEnable) return;
            StartCoroutine(SessionInstructionAction(sessionInstruction));
        }
        
        private IEnumerator SessionInstructionAction(CapturedRoom.SessionInstruction sessionInstruction)
        {
            if(textCustomSessionInstruction == null) yield break;
            _isSessionInstructionEnable = true;
            
            var targetText = string.Empty;
            if (sessionInstructionText.Count > 0)
            {
                var element = sessionInstructionText.Find(t => t.sessionInstruction == sessionInstruction);
                if (element != null) targetText = element.text;
            }
            
            textCustomSessionInstruction.text = targetText;
            textCustomSessionInstruction.gameObject.SetActive(true);
            
            yield return new WaitForSeconds(delayTime);
            
            textCustomSessionInstruction.text = string.Empty;
            textCustomSessionInstruction.gameObject.SetActive(false);
            _isSessionInstructionEnable = false;
            yield break;
        }

        #endregion
    }
}