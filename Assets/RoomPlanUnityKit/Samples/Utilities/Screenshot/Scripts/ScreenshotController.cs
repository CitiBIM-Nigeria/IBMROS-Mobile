using UnityEngine;
using UnityEngine.UI;

namespace SilverTau.Utilities.Sample
{
    public class ScreenshotController : MonoBehaviour
    {
        [SerializeField] private Button buttonScreenshot;
        [SerializeField] private Button buttonScreenshotShare;
        
        private void Start()
        {
            buttonScreenshot.onClick.AddListener(TakeScreenshot);
            buttonScreenshotShare.onClick.AddListener(ScreenshotShare);
        }

        /// <summary>
        /// A function that takes a screenshot with certain parameters.
        /// </summary>
        private void TakeScreenshot()
        {
            Screenshot.TakeScreenshot(true, true);
        }

        /// <summary>
        /// The function that performs the action shares the last screenshot taken.
        /// </summary>
        private void ScreenshotShare()
        {
            Screenshot.ScreenshotShare();
        }
    }
}
