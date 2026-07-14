#if UNITY_EDITOR
using System.Collections.Generic;
using SilverTau.RoomPlanUnity;
using UnityEditor;
using UnityEngine;

namespace SilverTau.Utilities
{
    public class RPUInspector : EditorWindow
    {
        public static List<RoomBuilder> RoomBuilders = new List<RoomBuilder>();
        public static List<CapturedRoomSnapshot> CapturedRoomSnapshot = new List<CapturedRoomSnapshot>();
        
        private Vector2 scrollPos;
        
        private Dictionary<int, bool[]> _expandRoomBuilderValues = new Dictionary<int, bool[]>();
        private Dictionary<string, bool[]> _expandRoomBuilderSurfeceValues = new Dictionary<string, bool[]>();
        private Dictionary<string, bool[]> _expandRoomBuilderObjectValues = new Dictionary<string, bool[]>();
        private Dictionary<string, bool[]> _expandRoomBuilderSectionValues = new Dictionary<string, bool[]>();
        private Dictionary<string, float> _areaRoomBuilderValues = new Dictionary<string, float>();
        
        private Dictionary<int, bool[]> _expandSnapshotValues = new Dictionary<int, bool[]>();
        private Dictionary<string, bool[]> _expandSnapshotSurfeceValues = new Dictionary<string, bool[]>();
        private Dictionary<string, bool[]> _expandSnapshotObjectValues = new Dictionary<string, bool[]>();
        private Dictionary<string, bool[]> _expandSnapshotSectionValues = new Dictionary<string, bool[]>();
        private Dictionary<string, float> _areaSnapshotValues = new Dictionary<string, float>();
        
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
        protected GUIStyle buttonStyleH1;
        protected GUIStyle buttonStyleH2;
        protected GUIStyle buttonStyleH3;
        protected GUIStyle buttonStyleH4;
        protected GUIStyle foldoutStyleH1;
        
        protected GUIStyle buttonStyleSurfaces;
        protected GUIStyle buttonStyleObjects;
        protected GUIStyle buttonStyleSections;
        
        protected GUIStyle buttonStyleSurfacesChild;
        protected GUIStyle buttonStyleObjectsChild;
        protected GUIStyle buttonStyleSectionsChild;
        
        protected Color colorDarkBlue;
        protected Color colorBlack;
        protected Color colorDodgerBlue;
        
        protected Color colorSurfaces;
        protected Color colorObjects;
        protected Color colorSections;
        
        protected Color colorSurfacesChild;
        protected Color colorObjectsChild;
        protected Color colorSectionsChild;
        
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
        
        [MenuItem("Window/Silver Tau/RoomPlan for Unity Kit/RPU Inspector")]
        public static void WindowRPUInspector()
        {
            EditorWindow wnd = GetWindow<RPUInspector>();
            wnd.titleContent = new GUIContent("RPU Inspector");
        }
        
        public static void AddRoomBuilder(RoomBuilder roomBuilder)
        {
            RoomBuilders.Add(roomBuilder);
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        void OnGUI()
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
            
            ColorUtility.TryParseHtmlString("#24807b", out colorSurfaces);
            ColorUtility.TryParseHtmlString("#245480", out colorObjects);
            ColorUtility.TryParseHtmlString("#242780", out colorSections);
            
            ColorUtility.TryParseHtmlString("#195956", out colorSurfacesChild);
            ColorUtility.TryParseHtmlString("#173857", out colorObjectsChild);
            ColorUtility.TryParseHtmlString("#171954", out colorSectionsChild);
            
            buttonStyleObjects = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 28.0f,
                richText = true,
                fontSize = 15,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorObjects),
                    textColor = Color.white
                },
                hover =
                {
                    background = CreateTexture2D(2, 2, colorDarkBlue),
                    textColor = Color.white
                },
                active =
                {
                    background = CreateTexture2D(2, 2, colorDodgerBlue),
                    textColor = Color.white
                }
            };
            
            buttonStyleSurfaces = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 28.0f,
                richText = true,
                fontSize = 15,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorSurfaces),
                    textColor = Color.white
                },
                hover =
                {
                    background = CreateTexture2D(2, 2, colorDarkBlue),
                    textColor = Color.white
                },
                active =
                {
                    background = CreateTexture2D(2, 2, colorDodgerBlue),
                    textColor = Color.white
                }
            };
            
            buttonStyleSections = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 28.0f,
                richText = true,
                fontSize = 15,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorSections),
                    textColor = Color.white
                },
                hover =
                {
                    background = CreateTexture2D(2, 2, colorDarkBlue),
                    textColor = Color.white
                },
                active =
                {
                    background = CreateTexture2D(2, 2, colorDodgerBlue),
                    textColor = Color.white
                }
            };
            
            buttonStyleObjectsChild = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 26.0f,
                richText = true,
                fontSize = 14,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorObjectsChild),
                    textColor = Color.white
                },
                hover =
                {
                    background = CreateTexture2D(2, 2, colorDarkBlue),
                    textColor = Color.white
                },
                active =
                {
                    background = CreateTexture2D(2, 2, colorDodgerBlue),
                    textColor = Color.white
                }
            };
            
            buttonStyleSurfacesChild = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 26.0f,
                richText = true,
                fontSize = 14,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorSurfacesChild),
                    textColor = Color.white
                },
                hover =
                {
                    background = CreateTexture2D(2, 2, colorDarkBlue),
                    textColor = Color.white
                },
                active =
                {
                    background = CreateTexture2D(2, 2, colorDodgerBlue),
                    textColor = Color.white
                }
            };
            
            buttonStyleSectionsChild = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 26.0f,
                richText = true,
                fontSize = 14,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorSectionsChild),
                    textColor = Color.white
                },
                hover =
                {
                    background = CreateTexture2D(2, 2, colorDarkBlue),
                    textColor = Color.white
                },
                active =
                {
                    background = CreateTexture2D(2, 2, colorDodgerBlue),
                    textColor = Color.white
                }
            };
            
            buttonStyleH3 = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 26.0f,
                richText = true,
                fontSize = 14,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorBlack),
                    textColor = Color.white
                },
                hover =
                {
                    background = CreateTexture2D(2, 2, colorDarkBlue),
                    textColor = Color.white
                },
                active =
                {
                    background = CreateTexture2D(2, 2, colorDodgerBlue),
                    textColor = Color.white
                }
            };
            
            buttonStyleH4 = new GUIStyle
            {
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 26.0f,
                richText = true,
                fontSize = 14,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorBlack),
                    textColor = Color.white
                },
                hover =
                {
                    background = CreateTexture2D(2, 2, colorDarkBlue),
                    textColor = Color.white
                },
                active =
                {
                    background = CreateTexture2D(2, 2, colorDodgerBlue),
                    textColor = Color.white
                }
            };
            
            buttonStyleH2 = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 28.0f,
                richText = true,
                fontSize = 15,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorBlack),
                    textColor = Color.white
                },
                hover =
                {
                    background = CreateTexture2D(2, 2, colorDarkBlue),
                    textColor = Color.white
                },
                active =
                {
                    background = CreateTexture2D(2, 2, colorDodgerBlue),
                    textColor = Color.white
                }
            };
            
            buttonStyleH1 = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 30.0f,
                richText = true,
                fontSize = 16,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorBlack),
                    textColor = Color.white
                },
                hover =
                {
                    background = CreateTexture2D(2, 2, colorDarkBlue),
                    textColor = Color.white
                },
                active =
                {
                    background = CreateTexture2D(2, 2, colorDodgerBlue),
                    textColor = Color.white
                }
            };
            
            foldoutStyleH1 = new GUIStyle(EditorStyles.foldout)
            {
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 21.0f,
                richText = true,
                fontSize = 14,
                normal = new GUIStyleState
                {
                    textColor = Color.white
                }
            };
            
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
            
            helpBox = new GUIStyle(GUI.skin.FindStyle("HelpBox"))
            {
                alignment = TextAnchor.MiddleLeft,
                richText = true,
                fontSize = 12,
                imagePosition = ImagePosition.ImageLeft,
                padding = new RectOffset(10, 10, 0, 0),
                normal = 
                {
                    background = CreateTexture2D(2, 2, colorDarkBlue), 
                    textColor = Color.white
                }
            };
            
            GUILayout.Box(new GUIContent("RoomPlan for Unity Kit Inspector [0.1.0] (beta)", IconOnly), labelHeader3);
            GUILayout.Space(5);
            
            GUILayout.Box(new GUIContent("\n" 
                                         + "RoomPlan for Unity Kit Inspector is a utility that helps you manage and analyze data from scanned objects."
                                         + "\n\n"
                                         + "The utility is still under development. We will improve it with each update. We strive to make this tool as efficient and user-friendly as possible."
                                         + "\n\n"
                                         + "Thank you for being with us. Have a great project development and good ideas!"
                                         + "\n", EditorGUIUtility.IconContent("console.infoicon").image), helpBox);
            
            GUILayout.Space(10);
            
            if (!Application.isPlaying)
            {
                GUILayout.Space(5);
                GUILayout.Box(new GUIContent("\n" 
                                             + "To get started, switch to play mode in the Unity Engine."
                                             + "\n", EditorGUIUtility.IconContent("console.infoicon").image), helpBox);
            
                GUILayout.Space(10);
                return;
            }
            
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Width(EditorGUIUtility.currentViewWidth), GUILayout.ExpandHeight(true));
            
            EditorGUILayout.BeginVertical();

            CreateCapturedRoomSnapshot(_expandSnapshotValues);
            CreateRoomBuilders(_expandRoomBuilderValues);

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        private void CreateRoomBuilders(Dictionary<int, bool[]> _expandValues)
        {
            if(RoomBuilders.Count == 0) return;
            
            if (_expandValues.Count != RoomBuilders.Count)
            {
                for (int i = 0; i < RoomBuilders.Count; i++)
                {
                    _expandValues.Add(i, new bool[999]);
                }
            }
            
            for (var rbIndex = 0; rbIndex < RoomBuilders.Count; rbIndex++)
            {
                var globalIndex = 0;
                
                if(GUILayout.Button("Room Builder: " + (rbIndex + 1), buttonStyleH1))
                {
                    _expandValues[rbIndex][globalIndex] = !_expandValues[rbIndex][globalIndex];
                }
                
                GUILayout.Space(5);
                
                if(!_expandValues[rbIndex][0]) continue;
                
                var container = RoomBuilders[rbIndex].container;
                var prefabRoomPlanObject = RoomBuilders[rbIndex].prefabRoomPlanObject;
                var targetRoomPlanUnityKitSettings = RoomBuilders[rbIndex].TargetRoomPlanUnityKitSettings;
                var rooms = RoomBuilders[rbIndex].GetCapturedRooms;
                var roomObjects = RoomBuilders[rbIndex].GetContentRooms;
                
                if (rooms == null && rooms.Count == 0) continue;
                
                EditorGUILayout.BeginVertical();

                EditorGUILayout.BeginHorizontal(new[] {GUILayout.MinWidth(10)});
                
                GUILayout.Label("Container:");
                EditorGUILayout.Space(5);
                
                if(GUILayout.Button(container != null ? container.name : "NULL"))
                {
                    Selection.activeGameObject = container.gameObject;
                }
                
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(5);
                
                EditorGUILayout.BeginHorizontal(new[] {GUILayout.MinWidth(10)});
                
                GUILayout.Label("Prefab:");
                EditorGUILayout.Space(5);
                
                if(GUILayout.Button(prefabRoomPlanObject != null ? prefabRoomPlanObject.name : "NULL"))
                {
                    Selection.activeObject = prefabRoomPlanObject;
                }
                
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(5);
                
                EditorGUILayout.BeginHorizontal(new[] {GUILayout.MinWidth(10)});

                GUILayout.Label("RPU Settings (SO):");
                EditorGUILayout.Space(5);
                
                if(GUILayout.Button(targetRoomPlanUnityKitSettings != null ? targetRoomPlanUnityKitSettings.name : "NULL"))
                {
                    Selection.activeObject = targetRoomPlanUnityKitSettings;
                }
                
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(5);
                
                if (targetRoomPlanUnityKitSettings == null)
                {
                    RoomBuilders[rbIndex].concavity = EditorGUILayout.DoubleField("Concavity", RoomBuilders[rbIndex].concavity);
                    RoomBuilders[rbIndex].createFloor = EditorGUILayout.Toggle("Create floor", RoomBuilders[rbIndex].createFloor);
                    RoomBuilders[rbIndex].typeFloorConstructor = (CapturedRoom.TypeFloorConstructor) EditorGUILayout.EnumPopup("Type Floor Constructor", RoomBuilders[rbIndex].typeFloorConstructor);
                    RoomBuilders[rbIndex].useMeshBooleanOperations = EditorGUILayout.Toggle("Use Mesh Boolean Operations", RoomBuilders[rbIndex].useMeshBooleanOperations);
                    RoomBuilders[rbIndex].meshBooleanOperations = (CSG.MeshBooleanOperations) EditorGUILayout.EnumFlagsField("Mesh Boolean Operations", RoomBuilders[rbIndex].meshBooleanOperations);
                    EditorGUILayout.Space(5);
                }
                
                EditorGUILayout.Space(5);
                
                globalIndex++;
                
                if(GUILayout.Button("Rooms (" + rooms.Count + ")", buttonStyleH2))
                {
                    _expandValues[rbIndex][globalIndex] = !_expandValues[rbIndex][globalIndex];
                }

                if (_expandValues[rbIndex][globalIndex])
                {
                    EditorGUILayout.Space(5);
                    
                    for (var index = 0; index < rooms.Count; index++)
                    {
                        var indexValue = index;
                        var room = rooms[indexValue];
                        globalIndex++;
                        
                        if(GUILayout.Button("Room: " + (indexValue + 1), buttonStyleH3))
                        {
                            _expandValues[rbIndex][globalIndex] = !_expandValues[rbIndex][globalIndex];
                        }
                        
                        if (_expandValues[rbIndex][globalIndex])
                        {
                            EditorGUILayout.ObjectField("Room " + (indexValue + 1), roomObjects[indexValue].room, typeof(GameObject), true);
                            GUILayout.Space(5);
                            EditorGUILayout.TextField("Identifier", room.identifier);
                            GUILayout.Space(5);
                            EditorGUILayout.IntField("Version", room.version);
                            GUILayout.Space(5);
                            EditorGUILayout.IntField("Story", room.story);
                            GUILayout.Space(5);
                            
                            if (room.floors != null && room.floors.Count > 0)
                            {
                                var area = 0.0f;
                                if (!_areaRoomBuilderValues.ContainsKey("Room Builder: " + (rbIndex + 1) + "Room " + (indexValue + 1)))
                                {
                                    foreach (var floor in room.floors)
                                    {
                                        if(floor.roomPlanObject == null) continue;
                                        if(!floor.roomPlanObject.gameObject.TryGetComponent<MeshFilter>(out var meshFilter)) continue;
                                        area += SilverTau.Triangulation.MeshSurfaceCreator.GetAreaOfMesh(meshFilter.mesh);
                                    }
                                }
                                else
                                {
                                    area = _areaRoomBuilderValues["Room Builder: " + (rbIndex + 1) + "Room " + (indexValue + 1)];
                                }

                                if (area > 0)
                                {
                                    EditorGUILayout.FloatField("Area (m2)", area * 2);
                                    GUILayout.Space(5);
                                }
                            }
                            
                            globalIndex++;
                            if (GUILayout.Button("Surfeces", buttonStyleSurfaces))
                            {
                                _expandValues[rbIndex][globalIndex] = !_expandValues[rbIndex][globalIndex];
                            }

                            if (_expandValues[rbIndex][globalIndex])
                            {
                                GUILayout.Space(5);
                                CreateSurfaceList(_expandRoomBuilderSurfeceValues, room, "Room Surface" + globalIndex);
                                GUILayout.Space(5);
                            }
                            
                            GUILayout.Space(5);

                            globalIndex++;
                            if (GUILayout.Button("Objects", buttonStyleObjects))
                            {
                                _expandValues[rbIndex][globalIndex] = !_expandValues[rbIndex][globalIndex];
                            }

                            if (_expandValues[rbIndex][globalIndex])
                            {
                                GUILayout.Space(5);
                                CreateObjectList(_expandRoomBuilderObjectValues, room, "Room Object" + globalIndex);
                                GUILayout.Space(5);
                            }
                            
                            GUILayout.Space(5);
                            
                            globalIndex++;
                            if (GUILayout.Button("Sections", buttonStyleSections))
                            {
                                _expandValues[rbIndex][globalIndex] = !_expandValues[rbIndex][globalIndex];
                            }

                            if (_expandValues[rbIndex][globalIndex])
                            {
                                GUILayout.Space(5);
                                CreateSectionList(_expandRoomBuilderSectionValues, room, "Room Section" + globalIndex);
                                GUILayout.Space(5);
                            }
                            
                            GUILayout.Space(10);
                        }
                    
                        GUILayout.Space(10);
                    }
                    
                    GUILayout.Space(10);
                }
                
                GUILayout.Space(10);
                
                EditorGUILayout.EndVertical();
            }
        }
        
        private void CreateCapturedRoomSnapshot(Dictionary<int, bool[]> _expandValues)
        {
            var crss = GameObject.FindObjectsOfType<CapturedRoomSnapshot>();
            
            if(crss.Length == 0) return;
            
            if (crss.Length != CapturedRoomSnapshot.Count)
            {
                CapturedRoomSnapshot.AddRange(crss);
            }
            
            if (_expandValues.Count != CapturedRoomSnapshot.Count)
            {
                for (int i = 0; i < CapturedRoomSnapshot.Count; i++)
                {
                    _expandValues.Add(i, new bool[999]);
                }
            }
            
            for (var rbIndex = 0; rbIndex < CapturedRoomSnapshot.Count; rbIndex++)
            {
                var globalIndex = 0;
                
                if(GUILayout.Button("Captured Room Snapshot: " + (rbIndex + 1), buttonStyleH1))
                {
                    _expandValues[rbIndex][globalIndex] = !_expandValues[rbIndex][globalIndex];
                }
                
                GUILayout.Space(5);
                
                if(!_expandValues[rbIndex][0]) continue;
                
                var container = CapturedRoomSnapshot[rbIndex].CapturedRoomContainer;
                var prefabRoomPlanObject = CapturedRoomSnapshot[rbIndex].CapturedRoomObjectPrefab;
                var targetRoomPlanUnityKitSettings = CapturedRoomSnapshot[rbIndex].TargetRoomPlanUnityKitSettings;
                var rooms = CapturedRoomSnapshot[rbIndex].GetCapturedRoom;
                var roomObjects = CapturedRoomSnapshot[rbIndex].GetContentRooms;
                
                if (rooms == null) continue;
                
                EditorGUILayout.BeginVertical();

                EditorGUILayout.BeginHorizontal(new[] {GUILayout.MinWidth(10)});
                
                GUILayout.Label("Container:");
                EditorGUILayout.Space(5);
                
                if(GUILayout.Button(container != null ? container.name : "NULL"))
                {
                    Selection.activeGameObject = container.gameObject;
                }
                
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(5);
                
                EditorGUILayout.BeginHorizontal(new[] {GUILayout.MinWidth(10)});
                
                GUILayout.Label("Prefab:");
                EditorGUILayout.Space(5);
                
                if(GUILayout.Button(prefabRoomPlanObject != null ? prefabRoomPlanObject.name : "NULL"))
                {
                    Selection.activeObject = prefabRoomPlanObject;
                }
                
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(5);
                
                EditorGUILayout.BeginHorizontal(new[] {GUILayout.MinWidth(10)});

                GUILayout.Label("RPU Settings (SO):");
                EditorGUILayout.Space(5);
                
                if(GUILayout.Button(targetRoomPlanUnityKitSettings != null ? targetRoomPlanUnityKitSettings.name : "NULL"))
                {
                    Selection.activeObject = targetRoomPlanUnityKitSettings;
                }
                
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(5);
                
                if (targetRoomPlanUnityKitSettings == null)
                {
                    RoomBuilders[rbIndex].concavity = EditorGUILayout.DoubleField("Concavity", RoomBuilders[rbIndex].concavity);
                    RoomBuilders[rbIndex].createFloor = EditorGUILayout.Toggle("Create floor", RoomBuilders[rbIndex].createFloor);
                    RoomBuilders[rbIndex].typeFloorConstructor = (CapturedRoom.TypeFloorConstructor) EditorGUILayout.EnumPopup("Type Floor Constructor", RoomBuilders[rbIndex].typeFloorConstructor);
                    RoomBuilders[rbIndex].useMeshBooleanOperations = EditorGUILayout.Toggle("Use Mesh Boolean Operations", RoomBuilders[rbIndex].useMeshBooleanOperations);
                    RoomBuilders[rbIndex].meshBooleanOperations = (CSG.MeshBooleanOperations) EditorGUILayout.EnumFlagsField("Mesh Boolean Operations", RoomBuilders[rbIndex].meshBooleanOperations);
                    EditorGUILayout.Space(5);
                }
                
                EditorGUILayout.Space(5);
                
                globalIndex++;
                
                if(GUILayout.Button("Rooms (" + 1+ ")", buttonStyleH2))
                {
                    _expandValues[rbIndex][globalIndex] = !_expandValues[rbIndex][globalIndex];
                }

                if (_expandValues[rbIndex][globalIndex])
                {
                    EditorGUILayout.Space(5);
                    
                    var indexValue = 0;
                    var room = rooms;
                    globalIndex++;
                        
                    if(GUILayout.Button("Room: " + (indexValue + 1), buttonStyleH3))
                    {
                        _expandValues[rbIndex][globalIndex] = !_expandValues[rbIndex][globalIndex];
                    }
                        
                    if (_expandValues[rbIndex][globalIndex])
                    {
                        EditorGUILayout.ObjectField("Room " + (indexValue + 1), roomObjects[indexValue].room, typeof(GameObject), true);
                        GUILayout.Space(5);
                        EditorGUILayout.TextField("Identifier", room.identifier);
                        GUILayout.Space(5);
                        EditorGUILayout.IntField("Version", room.version);
                        GUILayout.Space(5);
                        EditorGUILayout.IntField("Story", room.story);
                        GUILayout.Space(5);
                            
                        if (room.floors != null && room.floors.Count > 0)
                        {
                            var area = 0.0f;
                            if (!_areaSnapshotValues.ContainsKey("Room Builder: " + (rbIndex + 1) + "Room " + (indexValue + 1)))
                            {
                                foreach (var floor in room.floors)
                                {
                                    if(floor.roomPlanObject == null) continue;
                                    if(!floor.roomPlanObject.gameObject.TryGetComponent<MeshFilter>(out var meshFilter)) continue;
                                    area += SilverTau.Triangulation.MeshSurfaceCreator.GetAreaOfMesh(meshFilter.mesh);
                                }
                            }
                            else
                            {
                                area = _areaSnapshotValues["Room Builder: " + (rbIndex + 1) + "Room " + (indexValue + 1)];
                            }

                            if (area > 0)
                            {
                                EditorGUILayout.FloatField("Area (m2)", area * 2);
                                GUILayout.Space(5);
                            }
                        }
                            
                        globalIndex++;
                        if (GUILayout.Button("Surfeces", buttonStyleSurfaces))
                        {
                            _expandValues[rbIndex][globalIndex] = !_expandValues[rbIndex][globalIndex];
                        }

                        if (_expandValues[rbIndex][globalIndex])
                        {
                            GUILayout.Space(5);
                            CreateSurfaceList(_expandSnapshotSurfeceValues, room, "Room Surface" + globalIndex);
                            GUILayout.Space(5);
                        }
                            
                        GUILayout.Space(5);

                        globalIndex++;
                        if (GUILayout.Button("Objects", buttonStyleObjects))
                        {
                            _expandValues[rbIndex][globalIndex] = !_expandValues[rbIndex][globalIndex];
                        }

                        if (_expandValues[rbIndex][globalIndex])
                        {
                            GUILayout.Space(5);
                            CreateObjectList(_expandSnapshotObjectValues, room, "Room Object" + globalIndex);
                            GUILayout.Space(5);
                        }
                            
                        GUILayout.Space(5);
                            
                        globalIndex++;
                        if (GUILayout.Button("Sections", buttonStyleSections))
                        {
                            _expandValues[rbIndex][globalIndex] = !_expandValues[rbIndex][globalIndex];
                        }

                        if (_expandValues[rbIndex][globalIndex])
                        {
                            GUILayout.Space(5);
                            CreateSectionList(_expandSnapshotSectionValues, room, "Room Section" + globalIndex);
                            GUILayout.Space(5);
                        }
                        GUILayout.Space(10);
                    }
                    GUILayout.Space(10);
                }
                
                GUILayout.Space(10);
                
                EditorGUILayout.EndVertical();
            }
        }
        
        private void CreateSurfaceList(Dictionary<string, bool[]> _expandSurfeceValues, CapturedRoom room, string id)
        {
            if (!_expandSurfeceValues.ContainsKey(id))
            {
                _expandSurfeceValues.Add(id, new bool[999]);
            }

            var globalIndex = -1;
            
            if (room.windows != null && room.windows.Count > 0)
            {
                globalIndex++;
                
                if (GUILayout.Button("Windows: " + room.windows.Count, buttonStyleSurfacesChild))
                {
                    _expandSurfeceValues[id][globalIndex] = !_expandSurfeceValues[id][globalIndex];
                }
                
                if (_expandSurfeceValues[id][globalIndex])
                {
                    GUILayout.Space(5);
                    
                    for (int d = 0; d < room.windows.Count; d++)
                    {
                        globalIndex++;
                        
                        if (GUILayout.Button(" Window " + room.windows[d].category + " (" + room.windows[d].identifier + ")", buttonStyleH4))
                        {
                            _expandSurfeceValues[id][globalIndex] = !_expandSurfeceValues[id][globalIndex];
                        }
                        
                        GUILayout.Space(5);

                        if (_expandSurfeceValues[id][globalIndex])
                        {
                            WriteSurfaceInfo(room.windows[d], globalIndex);
                        }
                    }
                }
            
                GUILayout.Space(5);
            }
            
            if (room.walls != null && room.walls.Count > 0)
            {
                globalIndex++;
                
                if (GUILayout.Button("Walls: " + room.walls.Count, buttonStyleSurfacesChild))
                {
                    _expandSurfeceValues[id][globalIndex] = !_expandSurfeceValues[id][globalIndex];
                }
                
                if (_expandSurfeceValues[id][globalIndex])
                {
                    GUILayout.Space(5);
                    
                    for (int d = 0; d < room.walls.Count; d++)
                    {
                        globalIndex++;
                        
                        if (GUILayout.Button(" Wall " + room.walls[d].category + " (" + room.walls[d].identifier + ")", buttonStyleH4))
                        {
                            _expandSurfeceValues[id][globalIndex] = !_expandSurfeceValues[id][globalIndex];
                        }
                        
                        GUILayout.Space(5);

                        if (_expandSurfeceValues[id][globalIndex])
                        {
                            WriteSurfaceInfo(room.walls[d], globalIndex);
                        }
                    }
                }
            
                GUILayout.Space(5);
            }
            
            if (room.openings != null && room.openings.Count > 0)
            {
                globalIndex++;
                
                if (GUILayout.Button("Openings: " + room.openings.Count, buttonStyleSurfacesChild))
                {
                    _expandSurfeceValues[id][globalIndex] = !_expandSurfeceValues[id][globalIndex];
                }
                
                if (_expandSurfeceValues[id][globalIndex])
                {
                    GUILayout.Space(5);
                    
                    for (int d = 0; d < room.openings.Count; d++)
                    {
                        globalIndex++;
                        
                        if (GUILayout.Button(" Opening " + room.openings[d].category + " (" + room.openings[d].identifier + ")", buttonStyleH4))
                        {
                            _expandSurfeceValues[id][globalIndex] = !_expandSurfeceValues[id][globalIndex];
                        }
                        
                        GUILayout.Space(5);

                        if (_expandSurfeceValues[id][globalIndex])
                        {
                            WriteSurfaceInfo(room.openings[d], globalIndex);
                        }
                    }
                }
            
                GUILayout.Space(5);
            }
            
            if (room.doors != null && room.doors.Count > 0)
            {
                globalIndex++;
                
                if (GUILayout.Button("Doors: " + room.doors.Count, buttonStyleSurfacesChild))
                {
                    _expandSurfeceValues[id][globalIndex] = !_expandSurfeceValues[id][globalIndex];
                }
                
                if (_expandSurfeceValues[id][globalIndex])
                {
                    GUILayout.Space(5);
                    
                    for (int d = 0; d < room.doors.Count; d++)
                    {
                        globalIndex++;
                        
                        if (GUILayout.Button(" Door " + room.doors[d].category + " (" + room.doors[d].identifier + ")", buttonStyleH4))
                        {
                            _expandSurfeceValues[id][globalIndex] = !_expandSurfeceValues[id][globalIndex];
                        }
                        
                        GUILayout.Space(5);

                        if (_expandSurfeceValues[id][globalIndex])
                        {
                            WriteSurfaceInfo(room.doors[d], globalIndex);
                        }
                    }
                }
            
                GUILayout.Space(5);
            }
            
            if (room.floors != null && room.floors.Count > 0)
            {
                globalIndex++;
                
                if (GUILayout.Button("Floors: " + room.floors.Count, buttonStyleSurfacesChild))
                {
                    _expandSurfeceValues[id][globalIndex] = !_expandSurfeceValues[id][globalIndex];
                }
                
                if (_expandSurfeceValues[id][globalIndex])
                {
                    GUILayout.Space(5);
                    
                    for (int d = 0; d < room.floors.Count; d++)
                    {
                        globalIndex++;
                        
                        if (GUILayout.Button(" Floor " + room.floors[d].category + " (" + room.floors[d].identifier + ")", buttonStyleH4))
                        {
                            _expandSurfeceValues[id][globalIndex] = !_expandSurfeceValues[id][globalIndex];
                        }
                        
                        GUILayout.Space(5);

                        if (_expandSurfeceValues[id][globalIndex])
                        {
                            WriteSurfaceInfo(room.floors[d], globalIndex);
                        }
                    }
                }
            }
            
            GUILayout.Space(5);
        }

        private void WriteSurfaceInfo(CapturedRoom.Surface surface, int index)
        {
            if(surface == null) return;
            
            EditorGUILayout.ObjectField(surface.category.ToString(), surface.roomPlanObject, typeof(RoomPlanObject), true);
            EditorGUILayout.Space(5);

            EditorGUILayout.TextField("Identifier", surface.identifier);
            GUILayout.Space(5);
            EditorGUILayout.TextField("Parent Identifier", surface.parentIdentifier);
            GUILayout.Space(5);
            EditorGUILayout.IntField("Story", surface.story);
            GUILayout.Space(5);
            EditorGUILayout.EnumPopup("Category", surface.category);
            GUILayout.Space(5);
            EditorGUILayout.EnumPopup("Confidence", surface.confidence);
            GUILayout.Space(10);
                                            
            GUILayout.Label("Curve:");
            GUILayout.Space(5);
            EditorGUILayout.Vector2Field("Center", surface.curve.center);
            GUILayout.Space(5);
            EditorGUILayout.FloatField("Radius", surface.curve.radius);
            GUILayout.Space(5);
            EditorGUILayout.FloatField("Start Angle", surface.curve.startAngle);
            GUILayout.Space(5);
            EditorGUILayout.FloatField("End Angle", surface.curve.endAngle);
            GUILayout.Space(10);
            
            EditorGUILayout.Vector3Field("Dimensions", surface.dimensions);
            GUILayout.Space(10);  
            
            GUILayout.Label("Transform: \n" + surface.transform);
            GUILayout.Space(10);
        }
        
        private void CreateObjectList(Dictionary<string, bool[]> _expandObjectValues, CapturedRoom room, string id)
        {
            if (!_expandObjectValues.ContainsKey(id))
            {
                _expandObjectValues.Add(id, new bool[999]);
            }

            var globalIndex = -1;
            
            if (room.objects != null && room.objects.Count > 0)
            {
                globalIndex++;
                if (GUILayout.Button("Objects: " + room.objects.Count, buttonStyleObjectsChild))
                {
                    _expandObjectValues[id][globalIndex] = !_expandObjectValues[id][globalIndex];
                }
                
                if (_expandObjectValues[id][globalIndex])
                {
                    GUILayout.Space(5);
                    
                    for (int d = 0; d < room.objects.Count; d++)
                    {
                        globalIndex++;
                        
                        if (GUILayout.Button(" Object " + room.objects[d].category + " (" + room.objects[d].identifier + ")", buttonStyleH4))
                        {
                            _expandObjectValues[id][globalIndex] = !_expandObjectValues[id][globalIndex];
                        }
                        
                        GUILayout.Space(5);

                        if (_expandObjectValues[id][globalIndex])
                        {
                            WriteObjectInfo(room.objects[d], globalIndex);
                        }
                    }
                }
            }
        }
        
        private void WriteObjectInfo(CapturedRoom.Object obj, int index)
        {
            if(obj == null) return;
            
            EditorGUILayout.ObjectField(obj.category.ToString(), obj.roomPlanObject, typeof(RoomPlanObject), true);
            EditorGUILayout.Space(5);

            EditorGUILayout.TextField("Identifier", obj.identifier);
            GUILayout.Space(5);
            EditorGUILayout.TextField("Parent Identifier", obj.parentIdentifier);
            GUILayout.Space(5);
            EditorGUILayout.IntField("Story", obj.story);
            GUILayout.Space(5);
            EditorGUILayout.EnumPopup("Category", obj.category);
            GUILayout.Space(5);
            EditorGUILayout.EnumPopup("Confidence", obj.confidence);
            GUILayout.Space(10);

            
            EditorGUILayout.Vector3Field("Dimensions", obj.dimensions);
            GUILayout.Space(10);  
            
            GUILayout.Label("Transform: \n" + obj.transform);
            GUILayout.Space(10);
        }
        
        private void CreateSectionList(Dictionary<string, bool[]> _expandSectionValues, CapturedRoom room, string id)
        {
            if (!_expandSectionValues.ContainsKey(id))
            {
                _expandSectionValues.Add(id, new bool[999]);
            }

            var globalIndex = -1;
            
            if (room.sections != null && room.sections.Count > 0)
            {
                globalIndex++;
                if (GUILayout.Button("Sections: " + room.sections.Count, buttonStyleSectionsChild))
                {
                    _expandSectionValues[id][globalIndex] = !_expandSectionValues[id][globalIndex];
                }
                
                if (_expandSectionValues[id][globalIndex])
                {
                    GUILayout.Space(5);
                    
                    for (int d = 0; d < room.sections.Count; d++)
                    {
                        globalIndex++;
                        
                        if (GUILayout.Button(" Section " + "(" + room.sections[d].label + ")", buttonStyleH4))
                        {
                            _expandSectionValues[id][globalIndex] = !_expandSectionValues[id][globalIndex];
                        }
                        
                        GUILayout.Space(5);

                        if (_expandSectionValues[id][globalIndex])
                        {
                            WriteSectionInfo(room.sections[d]);
                        }
                    }
                }
            }
        }
        
        private void WriteSectionInfo(CapturedRoom.Section section)
        {
            if(section == null) return;
            
            EditorGUILayout.Vector3Field("Center", section.center);
            GUILayout.Space(5);
            EditorGUILayout.IntField("Story", section.story);
            GUILayout.Space(5);
            EditorGUILayout.EnumPopup("Label", section.label);
            GUILayout.Space(10);
        }
    }
}

#endif