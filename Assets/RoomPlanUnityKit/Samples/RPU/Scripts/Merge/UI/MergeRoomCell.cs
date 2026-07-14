using System;
using System.Collections.Generic;
using SilverTau.RoomPlanUnity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SilverTau.Sample
{
    public class MergeRoomCell : MonoBehaviour
    {
        [SerializeField] private Image image;
        [SerializeField] [ColorUsage(true)] private Color colorDefault;
        [SerializeField] [ColorUsage(true)] private Color colorSelect;
        public Button button;
        public TextMeshProUGUI txtTitle;
        
        private CapturedRoom _capturedRoom;
        private bool _selected;
        
        private void Start()
        {
        }

        private void OnDisable()
        {
            _selected = false;
            image.color = colorDefault;
        }

        public void Init(CapturedRoom inputScan)
        {
            _capturedRoom = inputScan;
        }

        public void Select(List<CapturedRoom> selectedScans)
        {
            _selected = !_selected;
            
            if (_selected)
            {
                image.color = colorSelect;
                selectedScans.Add(_capturedRoom);
                return;
            }
            
            image.color = colorDefault;
            selectedScans.Remove(_capturedRoom);
        }
    }
}
