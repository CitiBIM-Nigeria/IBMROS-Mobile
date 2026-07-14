using System;
using System.Collections.Generic;
using SilverTau.RoomPlanUnity;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SilverTau.Sample
{
    public class PlaceObjectAR : MonoBehaviour
    {
        [Tooltip("The prefab to be placed.")]
        [SerializeField] private GameObject prefabToPlace;
        
        /// <summary>
        /// The prefab to be placed.
        /// </summary>
        public GameObject CurrentPrefabToPlace
        {
            get => prefabToPlace;
            set => prefabToPlace = value;
        }
        
        private List<GameObject> _instantiateObjects = new List<GameObject>();
        
        private void Start()
        {
        
        }

        private void OnEnable()
        {
            AllReset();
        }

        private void OnDisable()
        {
            AllReset();
        }

        private void AllReset()
        {
            if(_instantiateObjects.Count == 0) return;

            foreach (var instantiateObject in _instantiateObjects)
            {
                Destroy(instantiateObject.gameObject);
            }
            
            _instantiateObjects = new List<GameObject>();
        }

        private void Update()
        {
#if !UNITY_EDITOR
            if(!RoomPlanUnityKit.RPUCaptureSessionActive) return;
#endif
            if (!Input.GetMouseButtonDown(0)) return;
            if (EventSystem.current.IsPointerOverUI()) return;
            
            var ray = RPUSessionCamera.CurrentSessionCamera.CurrentARCamera.ScreenPointToRay(Input.mousePosition);

            if (!Physics.Raycast(ray, out var hit)) return;

            var instantiateObject = Instantiate(CurrentPrefabToPlace, hit.point, Quaternion.identity);
            _instantiateObjects.Add(instantiateObject);
        }
    }
}
