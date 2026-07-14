using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SilverTau.Sample
{
    public class MergeCell : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI txtTitle;
        [SerializeField] private TextMeshProUGUI txtDate;
        public Button button;
        
        private void Start()
        {
        }

        private void OnDisable()
        {
        }

        public void Init(Scan inputScan)
        {
            txtTitle.text = inputScan.name;
            txtDate.text = inputScan.creationTime;
        }
    }
}
