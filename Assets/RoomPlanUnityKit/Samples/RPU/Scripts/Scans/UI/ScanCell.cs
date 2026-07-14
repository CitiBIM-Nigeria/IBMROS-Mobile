using System;
using System.Collections.Generic;
using SilverTau.RoomPlanUnity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SilverTau.Sample
{
    public class ScanCell : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI txtTitle;
        [SerializeField] private TextMeshProUGUI txtRoomCount;
        [SerializeField] private TextMeshProUGUI txtDate;
        [SerializeField] private RectTransform buttonRoomContainer;
        [SerializeField] private RoomCell prefabButtonRoom;
        [SerializeField] private Button button;
        [SerializeField] private Button buttonRooms;
        public Button buttonDelete;

        protected Scan _scan;
        private CapturedStructure _capturedStructure;
        private RoomBuilder _roomBuilder;
        private List<RoomCell> _roomCells = new List<RoomCell>();
        private bool _roomsListOpened;
        private ModelViewer _modelViewer;
        
        public virtual void Start()
        {
            _modelViewer = ModelViewer.Instance;
            
            button.onClick.AddListener(SelectMainButton);
            buttonRooms.onClick.AddListener(OpenRoomList);
        }

        private void OnEnable()
        {
            buttonRoomContainer.gameObject.SetActive(false);
            buttonRooms.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            foreach (var roomButton in _roomCells)
            {
                Destroy(roomButton.gameObject);
            }
            
            _roomCells.Clear();
            buttonRoomContainer.gameObject.SetActive(false);
            buttonRooms.gameObject.SetActive(false);
        }

        public void Init(Scan inputScan)
        {
            txtTitle.text = inputScan.name;
            txtDate.text = inputScan.creationTime;
            _scan = inputScan;
            
            _roomBuilder = new RoomBuilder();
            _capturedStructure = _roomBuilder.GetCapturedStructureFromSnapshot(_scan.snapshot);

            txtRoomCount.text = "Rooms: ";
            txtRoomCount.text += _capturedStructure.rooms.Count == 0 ? "1" : _capturedStructure.rooms.Count.ToString();
            
            if(prefabButtonRoom == null) return;
            if(_capturedStructure.rooms.Count < 2) return;
            buttonRooms.gameObject.SetActive(true);
            
            for (int i = 0; i < _capturedStructure.rooms.Count; i++)
            {
                var index = i;
                
                var roomButton = Instantiate(prefabButtonRoom, buttonRoomContainer);
                roomButton.txtTitle.text = "Room " + (i + 1).ToString();
                
                roomButton.button.onClick.AddListener(() =>
                {
                    SelectRoomButton(_capturedStructure.rooms[index], index);
                });
                
                _roomCells.Add(roomButton);
            }
        }

        private void SelectMainButton()
        {
            _modelViewer.GetTitleElement.text = _scan.name;
            _modelViewer.OpenFrom(_scan);
        }

        private void SelectRoomButton(CapturedRoom room, int index)
        {
            _modelViewer.GetTitleElement.text = _scan.name + " - " + "Room " + (index + 1).ToString();
            _modelViewer.OpenFrom(room);
        }

        private void OpenRoomList()
        {
            _roomsListOpened = !_roomsListOpened;
            buttonRoomContainer.gameObject.SetActive(_roomsListOpened);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)buttonRoomContainer.parent.transform);
        }
    }
}
