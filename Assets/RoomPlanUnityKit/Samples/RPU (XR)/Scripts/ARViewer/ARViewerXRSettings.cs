#if UNITY_XR_ARKIT_LOADER_ENABLED

using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace SilverTau.Sample
{
    [RequireComponent(typeof(ARViewer))]
    public sealed class ARViewerXRSettings : MonoBehaviour
    {
        [SerializeField] private ARSession targetARSession;
        
        private ARViewer _arViewer;
        
        private void Awake()
        {
            _arViewer = GetComponent<ARViewer>();
        }

        private void Start()
        {
        }

        private void OnEnable()
        {
            if (targetARSession == null) return;
            _arViewer.onStartNewScan += OnStartNewScan;
            _arViewer.onStopScanning += OnStopScanning;
        }

        private void OnDisable()
        {
            if (targetARSession == null) return;
            _arViewer.onStartNewScan -= OnStartNewScan;
            _arViewer.onStopScanning -= OnStopScanning;
        }

        /// <summary>
        /// A function that launches AR with a specific parameter.
        /// </summary>
        /// <param name="value">AR stage parameter.</param>
        private void OnStartNewScan(int value)
        {
            targetARSession.enabled = true;
        }
        
        /// <summary>
        /// A function that stops AR.
        /// </summary>
        private void OnStopScanning()
        {
            targetARSession.enabled = false;
        }
    }
}

#endif
