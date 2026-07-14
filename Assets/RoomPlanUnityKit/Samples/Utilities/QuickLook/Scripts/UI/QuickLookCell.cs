using SilverTau.Sample;
using UnityEngine;
using UnityEngine.UI;

namespace SilverTau.Utilities.Sample
{
    public class QuickLookCell : ScanCell
    {
        [SerializeField] private Button buttonQuickLook;
        
        public override void Start()
        {
            base.Start();
            buttonQuickLook.onClick.AddListener(ARQuickLook);
        }
        
        /// <summary>
        /// A function that opens the USDZ model for quick viewing.
        /// </summary>
        private void ARQuickLook()
        {
            QuickLook.OpenQuickLook(_scan.usdzPath);
        }
    }
}
