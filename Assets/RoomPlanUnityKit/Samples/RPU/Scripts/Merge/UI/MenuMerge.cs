using System.Collections.Generic;
using SilverTau.RoomPlanUnity;
using UnityEngine;
using UnityEngine.UI;

namespace SilverTau.Sample
{
    public class MenuMerge : MonoBehaviour
    {
        [SerializeField] private RoomPlanUnityKit roomPlanUnityKit;
        [SerializeField] private MyScans myScans;
        [SerializeField] private GameObject menuEmpty;
        [SerializeField] private Transform container;
        [SerializeField] private MergeCell prefabScanCell;
        [SerializeField] private PanelMerge panelMerge;
        [SerializeField] private GameObject panelWarning;
        [SerializeField] private Button buttonWarningOk;
        
        private List<MergeCell> _cells = new List<MergeCell>();
        private List<Scan> _scans = new List<Scan>();
        
        private void Start()
        {
            buttonWarningOk.onClick.AddListener(() =>
            {
                panelWarning.SetActive(false);
            });
        }

        private void OnEnable()
        {
            if (!RoomPlanHelper.AvailableIOS17())
            {
                panelWarning.SetActive(true);
            }

            ReInit();
        }

        public void ReInit()
        {
            Dispose();
            
            _scans.Clear();
            _scans = myScans.Scans;

            if (_scans.Count == 0)
            {
                if(menuEmpty) menuEmpty.SetActive(true);
                return;
            }
            
            if(menuEmpty) menuEmpty.SetActive(false);
            CreateCells();
        }

        private void OnDisable()
        {
            panelWarning.SetActive(false);
            Dispose();
        }

        private void CreateCells()
        {
            _cells.Clear();
            
            foreach (var scan in _scans)
            {
                var cell = Instantiate(prefabScanCell, container);
                cell.Init(scan);
                cell.button.onClick.AddListener(() =>
                {
                    OpenMergePanel(scan);
                });
                
                _cells.Add(cell);
            }
        }
        
        private void OpenMergePanel(Scan scan)
        {
            panelMerge.TargetScan = scan;
            panelMerge.gameObject.SetActive(true);
        }

        private void Dispose()
        {
            StopAllCoroutines();
            
            foreach (var cell in _cells)
            {
                if(cell == null) continue;
                Destroy(cell.gameObject);
            }

            panelMerge.gameObject.SetActive(false);
            _cells.Clear();
        }
    }
}
