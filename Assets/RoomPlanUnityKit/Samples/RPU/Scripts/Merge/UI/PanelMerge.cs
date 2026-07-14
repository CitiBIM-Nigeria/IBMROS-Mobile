using System.Collections;
using System.Collections.Generic;
using System.IO;
using SilverTau.RoomPlanUnity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SilverTau.Sample
{
    public class PanelMerge : MonoBehaviour
    {
        [SerializeField] private RoomPlanUnityKit roomPlanUnityKit;
        [SerializeField] private MenuMerge menuMerge;
        [SerializeField] private string nameScanDirectory = "model-viewer";
        [SerializeField] private Transform container;
        [SerializeField] private MergeRoomCell prefabMergeRoomCell;
        [SerializeField] private GameObject panelMergeSave;
        [SerializeField] private Button buttonMerge;
        [SerializeField] private Button buttonClose;
        [SerializeField] private Button buttonMergeScanSave;
        [SerializeField] private Button buttonMergeScanClose;
        [SerializeField] private TMP_InputField inputFieldMergeScanName;

        public Scan TargetScan { get; set; }
        
        private CapturedStructure _capturedStructure;
        private RoomBuilder _roomBuilder;
        private List<MergeRoomCell> _roomCells = new List<MergeRoomCell>();
        
        private List<CapturedRoom> _selectedCapturedRooms = new List<CapturedRoom>();
        
        private void Start()
        {
            buttonMerge.onClick.AddListener(OpenSavePanel);
            buttonClose.onClick.AddListener(() =>
            {
                gameObject.SetActive(false);
            });
            buttonMergeScanSave.onClick.AddListener(MergeScans);
            buttonMergeScanClose.onClick.AddListener(CloseSavePanel);
        }

        private void OnEnable()
        {
            ReInit();
        }
        
        private void Dispose()
        {
            StopAllCoroutines();
            
            foreach (var cell in _roomCells)
            {
                if(cell == null) continue;
                Destroy(cell.gameObject);
            }

            inputFieldMergeScanName.text = string.Empty;
            buttonMerge.interactable = false;
            panelMergeSave.SetActive(false);
            _roomCells.Clear();
            _selectedCapturedRooms.Clear();
        }
        
        private void ReInit()
        {
            Dispose();
            
            _roomCells.Clear();

            if(TargetScan == null) return;
            _roomBuilder = new RoomBuilder();
            _capturedStructure = _roomBuilder.GetCapturedStructureFromSnapshot(TargetScan.snapshot);
            
            CreateCells();
        }

        private void OnDisable()
        {
            Dispose();
        }

        private void CreateCells()
        {
            if(prefabMergeRoomCell == null) return;
            
            for (int i = 0; i < _capturedStructure.rooms.Count; i++)
            {
                var roomButton = Instantiate(prefabMergeRoomCell, container);
                roomButton.txtTitle.text = "Room " + (i + 1).ToString();
                
                roomButton.Init(_capturedStructure.rooms[i]);
                roomButton.button.onClick.AddListener(() =>
                {
                    roomButton.Select(_selectedCapturedRooms);
                    ActiveButton();
                });
                
                _roomCells.Add(roomButton);
            }
        }

        private void ActiveButton()
        {
            if (_selectedCapturedRooms.Count < 2)
            {
                buttonMerge.interactable = false;
                return;
            }
            
            buttonMerge.interactable = true;
        }

        private void OpenSavePanel()
        {
            inputFieldMergeScanName.text = string.Empty;
            panelMergeSave.SetActive(true);
        }

        private void CloseSavePanel()
        {
            panelMergeSave.SetActive(false);
        }

        private void MergeScans()
        {
            if (_selectedCapturedRooms.Count < 2) return;
            if(string.IsNullOrEmpty(inputFieldMergeScanName.text)) return;
            StartCoroutine(IeMergeScans(_selectedCapturedRooms));
        }

        private IEnumerator IeMergeScans(List<CapturedRoom> rooms)
        {
            var path = Path.Combine(Application.persistentDataPath, nameScanDirectory);
            if (!Directory.Exists(path)) yield break;
            
            var scansCount = Directory.GetDirectories(path).Length;
            
            roomPlanUnityKit.MergeScans(rooms.ToArray(), inputFieldMergeScanName.text, nameScanDirectory);
            
            yield return new WaitUntil(() => Directory.GetDirectories(path).Length > scansCount);
            menuMerge.ReInit();
        }
    }
}
