using UnityEditor;
using UnityEngine;

namespace SilverTau.RoomPlanUnity
{
    [CustomEditor(typeof(CapturedRoomObject))]
    public class CapturedRoomObjectEditor : RoomPlanUnityPackageEditor
    {
        private CapturedRoomObject _target;
        
        private void OnEnable()
        {
            if (target) _target = (CapturedRoomObject)target;
        }
        
        public override void OnInspectorGUI()
        {
            BoxLogo(_target, " <b> Captured Room Object</b>");
            
            serializedObject.Update();
            
            EditorGUI.BeginChangeCheck();
            
            GUILayout.Box(new GUIContent("<b>Main Settings</b>", EditorGUIUtility.IconContent("d_MoreOptions@2x").image), labelHeader2);
            GUILayout.Space(10);
            
            base.OnInspectorGUI();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_target);
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}