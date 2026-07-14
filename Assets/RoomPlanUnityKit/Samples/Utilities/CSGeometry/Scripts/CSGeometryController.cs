using UnityEngine;
using UnityEngine.UI;

namespace SilverTau.Utilities.Sample
{
    public class CSGeometryController : MonoBehaviour
    {
        [SerializeField] private Button buttonReset;
        [SerializeField] private Button buttonAdditive;
        [SerializeField] private Button buttonSubtractive;
        [SerializeField] private Button buttonIntersect;
        [SerializeField] private Button buttonDeIntersect;
        
        [SerializeField] private Transform container;
        [SerializeField] private CSGeometryTestModel prefabTestModel;

        private CSGeometryTestModel _testModel;
        
        private void Start()
        {
            buttonReset.onClick.AddListener(ResetScene);
            buttonAdditive.onClick.AddListener(Additive);
            buttonSubtractive.onClick.AddListener(Subtractive);
            buttonIntersect.onClick.AddListener(Intersect);
            buttonDeIntersect.onClick.AddListener(DeIntersect);

            ResetScene();
        }
        
        /// <summary>
        /// A function that resets the settings.
        /// </summary>
        private void ResetScene()
        {
            if (_testModel != null)
            {
                Destroy(_testModel.gameObject);
            }

            _testModel = Instantiate(prefabTestModel, container);
        }

        /// <summary>
        /// A function that combines objects.
        /// </summary>
        private void Additive()
        {
            CSGeometry.Additive(_testModel.targetObject, _testModel.arrayObject.ToArray());
            DisableObjects(_testModel.container);
        }

        /// <summary>
        /// A function that subtractive objects.
        /// </summary>
        private void Subtractive()
        {
            CSGeometry.Subtractive(_testModel.targetObject, _testModel.arrayObject.ToArray());
            DisableObjects(_testModel.container);
        }

        /// <summary>
        /// A function that intersect objects.
        /// </summary>
        private void Intersect()
        {
            CSGeometry.Intersect(_testModel.targetObject, _testModel.arrayObject.ToArray());
            DisableObjects(_testModel.container);
        }

        /// <summary>
        /// A function that deintersect objects.
        /// </summary>
        private void DeIntersect()
        {
            CSGeometry.DeIntersect(_testModel.targetObject, _testModel.arrayObject.ToArray());
            DisableObjects(_testModel.container);
        }

        /// <summary>
        /// A function that disables a specific object after performing an action.
        /// </summary>
        /// <param name="go">Specific object.</param>
        private void DisableObjects(Transform go)
        {
            go.gameObject.SetActive(false);
        }
    }
}
