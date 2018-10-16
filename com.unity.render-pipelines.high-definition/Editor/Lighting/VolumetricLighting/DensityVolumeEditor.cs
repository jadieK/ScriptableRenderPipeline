using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.HDPipeline;
using System.Collections.Generic;
using UnityEditorInternal;

namespace UnityEditor.Experimental.Rendering.HDPipeline
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(DensityVolume))]
    class DensityVolumeEditor : Editor
    {
        const int maxDisplayedBox = 10;

        const EditMode.SceneViewEditMode EditShape = EditMode.SceneViewEditMode.ReflectionProbeBox;
        const EditMode.SceneViewEditMode EditBlend = EditMode.SceneViewEditMode.GridBox;

        static class Styles
        {
            public static readonly GUIContent[] s_Toolbar_Contents = new GUIContent[]
            {
                EditorGUIUtility.IconContent("EditCollider", "|Modify the base shape. (SHIFT+1)"),
                EditorGUIUtility.IconContent("PreMatCube", "|Modify the influence volume. (SHIFT+2)")
            };

            public static readonly GUIContent s_Size = new GUIContent("Size", "The size of this density volume which is transform's scale independent.");
            public static readonly GUIContent s_AlbedoLabel = new GUIContent("Single Scattering Albedo", "Hue and saturation control the color of the fog (the wavelength of in-scattered light). Value controls scattering (0 = max absorption & no scattering, 1 = no absorption & max scattering).");
            public static readonly GUIContent s_MeanFreePathLabel = new GUIContent("Mean Free Path", "Controls the density, which determines how far you can seen through the fog. It's the distance in meters at which 50% of background light is lost in the fog (due to absorption and out-scattering).");
            public static readonly GUIContent s_VolumeTextureLabel = new GUIContent("Density Mask Texture");
            public static readonly GUIContent s_TextureScrollLabel = new GUIContent("Texture Scroll Speed");
            public static readonly GUIContent s_TextureTileLabel = new GUIContent("Texture Tiling Amount");
            public static readonly GUIContent s_BlendLabel = new GUIContent("Blend Distance", "Distance from size where the linear fade is done.");
            public static readonly GUIContent s_InvertFadeLabel = new GUIContent("Invert Fade", "Inverts fade values in such a way that (0 -> 1), (0.5 -> 0.5) and (1 -> 0).");
            public static readonly GUIContent s_NormalModeContent = new GUIContent("Normal", "Normal parameters mode.");
            public static readonly GUIContent s_AdvancedModeContent = new GUIContent("Advanced", "Advanced parameters mode.");

            public static readonly Color k_GizmoColorBase = new Color(180 / 255f, 180 / 255f, 180 / 255f, 8 / 255f).gamma;

            public static readonly Color[] k_BaseHandlesColor = new Color[]
            {
                new Color(180 / 255f, 180 / 255f, 180 / 255f, 255 / 255f).gamma,
                new Color(180 / 255f, 180 / 255f, 180 / 255f, 255 / 255f).gamma,
                new Color(180 / 255f, 180 / 255f, 180 / 255f, 255 / 255f).gamma,
                new Color(180 / 255f, 180 / 255f, 180 / 255f, 255 / 255f).gamma,
                new Color(180 / 255f, 180 / 255f, 180 / 255f, 255 / 255f).gamma,
                new Color(180 / 255f, 180 / 255f, 180 / 255f, 255 / 255f).gamma
            }; 
        }

        SerializedProperty densityParams;
        SerializedProperty albedo;
        SerializedProperty meanFreePath;

        SerializedProperty volumeTexture;
        SerializedProperty textureScroll;
        SerializedProperty textureTile;

        SerializedProperty size;

        SerializedProperty positiveFade;
        SerializedProperty negativeFade;
        SerializedProperty uniformFade;
        SerializedProperty advancedFade;
        SerializedProperty invertFade;

        static Dictionary<DensityVolume, HierarchicalBox> shapeBoxes = new Dictionary<DensityVolume, HierarchicalBox>();
        static Dictionary<DensityVolume, HierarchicalBox> blendBoxes = new Dictionary<DensityVolume, HierarchicalBox>();

        void OnEnable()
        {
            densityParams = serializedObject.FindProperty("parameters");

            albedo = densityParams.FindPropertyRelative("albedo");
            meanFreePath = densityParams.FindPropertyRelative("meanFreePath");

            volumeTexture = densityParams.FindPropertyRelative("volumeMask");
            textureScroll = densityParams.FindPropertyRelative("textureScrollingSpeed");
            textureTile = densityParams.FindPropertyRelative("textureTiling");

            size = densityParams.FindPropertyRelative("size");

            positiveFade = densityParams.FindPropertyRelative("m_PositiveFade");
            negativeFade = densityParams.FindPropertyRelative("m_NegativeFade");
            uniformFade = densityParams.FindPropertyRelative("m_UniformFade");
            advancedFade = densityParams.FindPropertyRelative("advancedFade");
            invertFade = densityParams.FindPropertyRelative("invertFade");

            shapeBoxes.Clear();
            blendBoxes.Clear();
            int max = Mathf.Min(targets.Length, maxDisplayedBox);
            for (int i = 0; i < max; ++i)
            {
                var shapeBox = shapeBoxes[targets[i] as DensityVolume] = new HierarchicalBox(Styles.k_GizmoColorBase, Styles.k_BaseHandlesColor);
                shapeBox.monoHandle = false;
                blendBoxes[targets[i] as DensityVolume] = new HierarchicalBox(Styles.k_GizmoColorBase, InfluenceVolumeUI.k_HandlesColor, container: shapeBox);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Drawer_AdvancedSwitch();
            
            EditorGUI.BeginChangeCheck();
            {
                EditorGUILayout.PropertyField(albedo, Styles.s_AlbedoLabel);
                EditorGUILayout.PropertyField(meanFreePath, Styles.s_MeanFreePathLabel);

                EditorGUILayout.Space();

                EditorGUILayout.PropertyField(size, Styles.s_Size);
                if(advancedFade.boolValue)
                {
                    CoreEditorUtils.DrawVector6(Styles.s_BlendLabel, positiveFade, negativeFade, Vector3.zero, Vector3.one, InfluenceVolumeUI.k_HandlesColor);
                }
                else
                {
                    EditorGUILayout.PropertyField(uniformFade, Styles.s_BlendLabel);
                }
                EditorGUILayout.PropertyField(invertFade, Styles.s_InvertFadeLabel);

                EditorGUILayout.Space();

                EditorGUILayout.PropertyField(volumeTexture, Styles.s_VolumeTextureLabel);
                EditorGUILayout.PropertyField(textureScroll, Styles.s_TextureScrollLabel);
                EditorGUILayout.PropertyField(textureTile, Styles.s_TextureTileLabel);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Vector3 posFade = new Vector3();
                posFade.x = Mathf.Clamp01(positiveFade.vector3Value.x);
                posFade.y = Mathf.Clamp01(positiveFade.vector3Value.y);
                posFade.z = Mathf.Clamp01(positiveFade.vector3Value.z);

                Vector3 negFade = new Vector3();
                negFade.x = Mathf.Clamp01(negativeFade.vector3Value.x);
                negFade.y = Mathf.Clamp01(negativeFade.vector3Value.y);
                negFade.z = Mathf.Clamp01(negativeFade.vector3Value.z);

                positiveFade.vector3Value = posFade;
                negativeFade.vector3Value = negFade;
            }

            serializedObject.ApplyModifiedProperties();
        }


        void Drawer_AdvancedSwitch()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                bool advanced = advancedFade.boolValue;
                advanced = !GUILayout.Toggle(!advanced, Styles.s_NormalModeContent, EditorStyles.miniButtonLeft, GUILayout.Width(60f), GUILayout.ExpandWidth(false));
                advanced = GUILayout.Toggle(advanced, Styles.s_AdvancedModeContent, EditorStyles.miniButtonRight, GUILayout.Width(60f), GUILayout.ExpandWidth(false));
                foreach (var containedBox in blendBoxes.Values)
                {
                    containedBox.monoHandle = !advanced;
                }
                if (advancedFade.boolValue ^ advanced)
                {
                    advancedFade.boolValue = advanced;
                }
            }
        }

        static Vector3 CenterBlendLocalPosition(DensityVolume densityVolume)
        {
            Vector3 size = densityVolume.parameters.size;
            Vector3 posBlend = densityVolume.parameters.positiveFade;
            posBlend.x *= size.x;
            posBlend.y *= size.y;
            posBlend.z *= size.z;
            Vector3 negBlend = densityVolume.parameters.negativeFade;
            negBlend.x *= size.x;
            negBlend.y *= size.y;
            negBlend.z *= size.z;
            Vector3 localPosition = (negBlend - posBlend) * 0.5f;
            return localPosition;
        }
        static Vector3 BlendSize(DensityVolume densityVolume)
        {
            Vector3 size = densityVolume.parameters.size;
            Vector3 blendSize = Vector3.one - densityVolume.parameters.positiveFade - densityVolume.parameters.negativeFade;
            blendSize.x *= size.x;
            blendSize.y *= size.y;
            blendSize.z *= size.z;
            return blendSize;
        }
        
        [DrawGizmo(GizmoType.Selected|GizmoType.Active)]
        static void DrawGizmosSelected(DensityVolume densityVolume, GizmoType gizmoType)
        {
            using (new Handles.DrawingScope(Matrix4x4.TRS(densityVolume.transform.position, densityVolume.transform.rotation, Vector3.one)))
            {
                //// Positive fade box.
                //Vector3 size = densityVolume.parameters.size;
                //Handles.color = Color.red;
                //Vector3 posFade = densityVolume.parameters.positiveFade;
                //posFade.x *= size.x;
                //posFade.y *= size.y;
                //posFade.z *= size.z;
                //Vector3 posCenter = -0.5f * densityVolume.parameters.positiveFade;
                //posCenter.x *= size.x;
                //posCenter.y *= size.y;
                //posCenter.z *= size.z;
                //Handles.DrawWireCube(posCenter, size - posFade);

                //// Negative fade box.
                //Handles.color = Color.blue;
                //Vector3 negFade = densityVolume.parameters.negativeFade;
                //negFade.x *= size.x;
                //negFade.y *= size.y;
                //negFade.z *= size.z;
                //Vector3 negCenter = 0.5f * densityVolume.parameters.negativeFade;
                //negCenter.x *= size.x;
                //negCenter.y *= size.y;
                //negCenter.z *= size.z;
                //Handles.DrawWireCube(negCenter, size - negFade);

                // Blend box
                HierarchicalBox blendBox = blendBoxes[densityVolume];
                blendBox.center = CenterBlendLocalPosition(densityVolume);
                blendBox.size = BlendSize(densityVolume);
                blendBox.baseColor = densityVolume.parameters.albedo;
                blendBox.DrawHull(EditMode.editMode == EditShape);
                
                // Bounding box.
                HierarchicalBox shapeBox = shapeBoxes[densityVolume];
                shapeBox.center = Vector3.zero;
                shapeBox.size = densityVolume.parameters.size;
                shapeBox.DrawHull(EditMode.editMode == EditShape);

                //color reseted at end of scope
            }
        }

        void OnSceneGUI()
        {
            DensityVolume densityVolume = target as DensityVolume;
            HierarchicalBox shapeBox = shapeBoxes[densityVolume];
            HierarchicalBox blendBox = blendBoxes[densityVolume];
            
            using (new Handles.DrawingScope(Matrix4x4.TRS(densityVolume.transform.position, densityVolume.transform.rotation, Vector3.one)))
            {
                //contained must be initialized in all case
                shapeBox.center = Vector3.zero;
                shapeBox.size = densityVolume.parameters.size;

                blendBox.monoHandle = !densityVolume.parameters.advancedFade;
                blendBox.center = CenterBlendLocalPosition(densityVolume);
                blendBox.size = BlendSize(densityVolume);
                EditorGUI.BeginChangeCheck();
                blendBox.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(densityVolume, "Change Density Volume Blend");

                    //work in local space to compute the change on positiveFade and negativeFade
                    Vector3 newCenterBlendLocalPosition = blendBox.center;
                    Vector3 halfSize = blendBox.size * 0.5f;
                    Vector3 size = densityVolume.parameters.size;
                    Vector3 posFade = newCenterBlendLocalPosition + halfSize;
                    posFade.x = 0.5f - posFade.x / size.x;
                    posFade.y = 0.5f - posFade.y / size.y;
                    posFade.z = 0.5f - posFade.z / size.z;
                    Vector3 negFade = newCenterBlendLocalPosition - halfSize;
                    negFade.x = 0.5f + negFade.x / size.x;
                    negFade.y = 0.5f + negFade.y / size.y;
                    negFade.z = 0.5f + negFade.z / size.z;
                    densityVolume.parameters.positiveFade = posFade;
                    densityVolume.parameters.negativeFade = negFade;
                }

                shapeBox.monoHandle = !densityVolume.parameters.advancedFade;
                EditorGUI.BeginChangeCheck();
                shapeBox.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObjects(new Object[] { densityVolume, densityVolume.transform }, "ChangeDensity Volume Bounding Box");
                    densityVolume.transform.position = shapeBox.center;
                    densityVolume.parameters.size = shapeBox.size;
                    Vector3 halfSize = shapeBox.size * 0.5f;
                }
            }
        }
    }
}
