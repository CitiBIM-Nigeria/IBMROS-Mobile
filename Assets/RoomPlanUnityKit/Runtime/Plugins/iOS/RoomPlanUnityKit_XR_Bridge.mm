#import <ARKit/ARKit.h>
#include <stdint.h>

typedef struct UnityXRNativeSessionPtr
{
    int version;
    void* session;
} UnityXRNativeSessionPtr;

extern "C"
{
    void setRoomPlanUnityKitXRSession(ARSession* s);
    void RoomPlanUnityKit_Сombine_XRSession(UnityXRNativeSessionPtr* nativeSession)
    {
        ARSession* session = (__bridge ARSession*)nativeSession->session;
        setRoomPlanUnityKitXRSession(session);
    }
}
