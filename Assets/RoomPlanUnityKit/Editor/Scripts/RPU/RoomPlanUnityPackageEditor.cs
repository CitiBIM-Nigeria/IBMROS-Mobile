using UnityEditor;
using UnityEngine;

namespace SilverTau.RoomPlanUnity
{
    public class RoomPlanUnityPackageEditor : Editor
    {
        protected static Texture2D Icon;
        protected static Texture2D IconOnly;
        protected static Texture2D IconMini;
        protected static Texture2D Background;
        protected GUIStyle LogoStyle;
        protected GUIStyle BackgroundStyle;
        
        protected GUIStyle helpBox;
        protected GUIStyle labelHeader;
        protected GUIStyle labelHeader2;
        protected GUIStyle labelHeader3;
        protected Color colorDarkBlue;
        protected Color colorBlack;
        protected Color colorDodgerBlue;
        
        public virtual void Awake()
        {
            Icon = EditorGUIUtility.Load("Packages/com.silvertau.roomplanunitykit/Editor/Images/icon.png") as Texture2D;
            
            if (Icon == null)
            {
                Icon = EditorGUIUtility.Load("Assets/Silver Tau/RoomPlanUnityKit/Editor/Images/icon.png") as Texture2D;
            }
            
            IconOnly = EditorGUIUtility.Load("Packages/com.silvertau.roomplanunitykit/Editor/Images/icon.png") as Texture2D;
            
            if (IconOnly == null)
            {
                IconOnly = EditorGUIUtility.Load("Assets/Silver Tau/RoomPlanUnityKit/Editor/Images/icon_only.png") as Texture2D;
            }

            IconMini = EditorGUIUtility.Load("Packages/com.silvertau.roomplanunitykit/Editor/Images/icon_mini.png") as Texture2D;
            
            if (IconMini == null)
            {
                IconMini = EditorGUIUtility.Load("Assets/Silver Tau/RoomPlanUnityKit/Editor/Images/icon_mini.png") as Texture2D;
            }
            
            Background = CreateTexture2D(2, 2, new Color(0.0f, 0.0f, 0.0f, 0.5f));
            
            BackgroundStyle = new GUIStyle
            {
                fixedHeight = 32.0f,
                stretchWidth = true,
                normal = new GUIStyleState
                {
                    background = Background
                }
            };
            
            LogoStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleLeft,
                stretchWidth = true,
                stretchHeight = true,
                fixedHeight = 32.0f,
                fontSize = 21,
                richText = true
            };
            
            ColorUtility.TryParseHtmlString("#212121", out colorBlack);
            ColorUtility.TryParseHtmlString("#174ead", out colorDodgerBlue);
            ColorUtility.TryParseHtmlString("#122d4a", out colorDarkBlue);
            
            labelHeader = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 26.0f,
                richText = true,
                fontSize = 14,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorBlack),
                    textColor = Color.white
                }
            };
            
            labelHeader2 = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 30.0f,
                richText = true,
                fontSize = 14,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorDodgerBlue),
                    textColor = Color.white
                }
            };
            
            labelHeader3 = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 30.0f,
                richText = true,
                fontSize = 16,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorBlack),
                    textColor = Color.white
                }
            };
        }

        public Texture2D CreateTexture2D(int width, int height, Color col)
        {
            var pix = new Color[width * height];
            for(var i = 0; i < pix.Length; ++i)
            {
                pix[i] = col;
            }
            var result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        protected void BoxLogo(MonoBehaviour behaviour = null, string value = "")
        {
            if (Icon != null)
            {
                if(behaviour) EditorGUIUtility.SetIconForObject(behaviour, Icon);
            }
            
            GUILayout.Box(new GUIContent(value, IconOnly), labelHeader3);
            
            GUILayout.Space(7);
        }
    }
}
