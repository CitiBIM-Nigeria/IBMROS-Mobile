#if UNITY_XR_ARKIT_LOADER_ENABLED

using SilverTau.RoomPlanUnity.XR;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace SilverTau.Sample
{
    [RequireComponent(typeof(ARSession))]
    public sealed class RPU_XRSessionService : MonoBehaviour
    {
        private ARSession arSession;
        
        private void Awake()
        {
            arSession = GetComponent<ARSession>();
        }

        private void Start()
        {
        }

        private void OnEnable()
        {
            ARSession.stateChanged += ARSessionOnStateChanged;
        }

        private void OnDisable()
        {
            ARSession.stateChanged -= ARSessionOnStateChanged;
        }

        private void ARSessionOnStateChanged(ARSessionStateChangedEventArgs args)
        {
            switch (args.state)
            {
                case ARSessionState.None:
                case ARSessionState.Unsupported:
                case ARSessionState.CheckingAvailability:
                    return;
                case ARSessionState.NeedsInstall:
                case ARSessionState.Installing:
                    return;
                case ARSessionState.Ready:
                    break;
                case ARSessionState.SessionInitializing:
                    break;
                case ARSessionState.SessionTracking:
                    arSession.Combine_And_Start_RPU_XR_Session();
                    break;
                default:
                    return;
            }
        }
    }
}

#endif
