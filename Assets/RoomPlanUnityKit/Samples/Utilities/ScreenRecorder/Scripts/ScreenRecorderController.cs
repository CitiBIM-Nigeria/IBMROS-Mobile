using UnityEngine;
using UnityEngine.UI;

namespace SilverTau.Utilities.Sample
{
    public class ScreenRecorderController : MonoBehaviour
    {
        [SerializeField] private Button buttonStart;
        [SerializeField] private Button buttonStop;
        
        private void Start()
        {
            buttonStart.onClick.AddListener(StartScreenRecorder);
            buttonStop.onClick.AddListener(StopScreenRecorder);
        }

        /// <summary>
        /// A function that starts recording the screen with certain parameters.
        /// </summary>
        private void StartScreenRecorder()
        {
            ScreenRecorder.StartScreenRecorder(true, true);
        }

        /// <summary>
        /// A function that stops screen recording.
        /// </summary>
        private void StopScreenRecorder()
        {
            ScreenRecorder.StopScreenRecorder();
        }
    }
}
