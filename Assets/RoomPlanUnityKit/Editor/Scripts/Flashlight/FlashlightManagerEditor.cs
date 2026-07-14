using UnityEditor;

namespace SilverTau.RoomPlanUnity
{
    [CustomEditor(typeof(FlashlightManager))]
    public class FlashlightManagerEditor : RoomPlanUnityPackageEditor
    {
        private FlashlightManager _target;
        
        private void OnEnable()
        {
            if (target) _target = (FlashlightManager)target;
        }
        
        public override void OnInspectorGUI()
        {
            BoxLogo(_target, " <b><color=#ffffff>Flashlight Manager</color></b>");
            
            base.OnInspectorGUI();
        }
    }
}