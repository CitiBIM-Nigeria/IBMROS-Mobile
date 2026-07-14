using SilverTau.RoomPlanUnity;
using UnityEngine;

namespace SilverTau.Sample
{
    [RequireComponent(typeof(CapturedRoomObject))]
    public class IndoorModelCROVisualizer : CROVisualizer
    {
        [SerializeField] private bool autoSetTRS = false;
        [SerializeField] private float slerpSmoothValue = 1.0f;
        [SerializeField] private bool animatedPosition = false;
        [SerializeField] private bool animatedRotation = false;
        [SerializeField] private bool animatedScale = true;
        
        [SerializeField] private IndoorModelsSettings indoorModelsSettings;
        [SerializeField] private MeshRenderer meshRenderer;
        
        private Vector3 _currentPosition;
        private Quaternion _currentRotation;
        private Vector3 _currentScale;
        private bool _isUpdateTransform;

        private CapturedRoom.Category _lastCategory;
        private GameObject _lastIndoorModel;
        
        public override void Awake()
        {
            base.Awake();
        }

        public override void Start()
        {
            base.Start();
            CurrentCapturedRoomObject.autoSetTRS = autoSetTRS;
        }

        private void Update()
        {
            if(!_isUpdateTransform) return;

            if (animatedPosition)
            {
                if (transform.localPosition != _currentPosition)
                {
                    transform.localPosition = Vector3.Slerp(transform.localPosition, _currentPosition, Time.smoothDeltaTime * slerpSmoothValue * 2);
                }
            }

            if (animatedRotation)
            {
                if (transform.localRotation != _currentRotation)
                {
                    transform.localRotation = Quaternion.Slerp(transform.localRotation, _currentRotation, Time.smoothDeltaTime * slerpSmoothValue * 2);
                }
            }

            if (animatedScale)
            {
                if (transform.localScale != _currentScale)
                {
                    transform.localScale = Vector3.Slerp(transform.localScale, _currentScale, Time.smoothDeltaTime * slerpSmoothValue * 2);
                }
            }

            if (transform.localScale == _currentScale &&
                transform.localRotation == _currentRotation &&
                transform.localPosition == _currentPosition)
            {
                _isUpdateTransform = false;
            }
        }
        
        private void UpdateTransform()
        {
            _currentPosition = CurrentCapturedRoomObject.CurrentPosition;
            if (!animatedPosition) transform.localPosition = _currentPosition;
            
            _currentRotation = CurrentCapturedRoomObject.CurrentRotation;
            if (!animatedRotation) transform.localRotation = _currentRotation;
            
            _currentScale = CurrentCapturedRoomObject.CurrentScale;
            if (!animatedScale) transform.localScale = _currentScale;
            
            _isUpdateTransform = true;
        }

        public override void OnInit()
        {
            UpdateTransform();
            
            if(CurrentCapturedRoomObject == null) return;
            
            UpdateCategoryObject();
            _lastCategory = CurrentCapturedRoomObject.category;
        }

        public override void OnUpdate()
        {
            UpdateTransform();
            UpdateCategoryObject();
        }
        
        private void UpdateCategoryObject()
        {
            if(indoorModelsSettings == null) return;
            if(_lastCategory == CurrentCapturedRoomObject.category) return;

            var indoorModelOption = indoorModelsSettings.indoorModels.Find(x => x.category == CurrentCapturedRoomObject.category);
            
            if (_lastIndoorModel != null) Destroy(_lastIndoorModel.gameObject);
            
            if (indoorModelOption == null)
            {
                meshRenderer.enabled = true;
                return;
            }

            _lastIndoorModel = Instantiate(indoorModelOption.prefab, transform);
            meshRenderer.enabled = false;
            _lastCategory = CurrentCapturedRoomObject.category;
        }
    }
}