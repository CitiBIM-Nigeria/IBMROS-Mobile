using SilverTau.Sample;
using UnityEngine;
using UnityEngine.UI;

namespace SilverTau.Utilities.Sample
{
    public class ShareCell : ScanCell
    {
        [SerializeField] private Button buttonShareFolder;
        [SerializeField] private Button buttonShareFile;
        
        public override void Start()
        {
            base.Start();
            
            buttonShareFolder.onClick.AddListener(ShareFolder);
            buttonShareFile.onClick.AddListener(ShareFile);
        }

        /// <summary>
        /// A function that allows you to share a folder.
        /// </summary>
        private void ShareFolder()
        {
            Share.ShareFolder(_scan.directoryPath);
        }

        /// <summary>
        /// A function that allows you to share a file.
        /// </summary>
        private void ShareFile()
        {
            Share.ShareFile(_scan.usdzPath);
        }
    }
}
