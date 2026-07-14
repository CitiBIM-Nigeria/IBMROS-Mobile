using UnityEditor;

namespace SilverTau.RoomPlanUnity
{
    [CustomEditor(typeof(CustomSessionInstruction))]
    public class CustomSessionInstructionEditor : RoomPlanUnityPackageEditor
    {
        private CustomSessionInstruction _target;
        
        private void OnEnable()
        {
            if (target) _target = (CustomSessionInstruction)target;
        }
        
        public override void OnInspectorGUI()
        {
            BoxLogo(_target, " <b><color=#ffffff>Custom Session Instruction</color></b>");
            
            base.OnInspectorGUI();
        }
    }
}