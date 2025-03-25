// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HierarchicalSplatting.Runtime;
using GaussianSplatting.Editor;
using GaussianSplatting.Runtime;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using HierarchicalSplatRenderer = HierarchicalSplatting.Runtime.HierarchicalSplatRenderer;

namespace HierarchicalSplatting.Editor
{
    [CustomEditor(typeof(HierarchicalSplatRenderer))]
    [CanEditMultipleObjects]
    public class HierarchicalSplatRendererEditor : UnityEditor.Editor
    {
        const string kPrefExportBake = "nesnausk.GaussianSplatting.ExportBakeTransform";

        SerializedProperty m_PropAsset;
        SerializedProperty m_PropSortNthFrame;
        SerializedProperty m_PropShaderSplats;
        SerializedProperty m_PropShaderComposite;
        SerializedProperty m_PropCSSplatUtilities;
        SerializedProperty m_PropCSHierarchicalCut;

        SerializedProperty m_PropTau;

        bool m_ExportBakeTransform;
        bool m_ResourcesExpanded = false;

        static int s_EditStatsUpdateCounter = 0;

        static HashSet<HierarchicalSplatRendererEditor> s_AllEditors = new();

        public static void BumpGUICounter()
        {
            ++s_EditStatsUpdateCounter;
        }

        public static void RepaintAll()
        {
            foreach (var e in s_AllEditors)
                e.Repaint();
        }

        public void OnEnable()
        {
            m_ExportBakeTransform = EditorPrefs.GetBool(kPrefExportBake, false);
            m_PropAsset = serializedObject.FindProperty("m_Asset");
            m_PropTau = serializedObject.FindProperty("tau");
            m_PropShaderSplats = serializedObject.FindProperty("m_ShaderSplats");
            m_PropShaderComposite = serializedObject.FindProperty("m_ShaderComposite");
            m_PropCSSplatUtilities = serializedObject.FindProperty("m_CSSplatUtilities");
            m_PropCSHierarchicalCut = serializedObject.FindProperty("m_CSHierarchicalCut");
            
            s_AllEditors.Add(this);
        }

        public void OnDisable()
        {
            s_AllEditors.Remove(this);
        }

        public override void OnInspectorGUI()
        {
            var hs = target as HierarchicalSplatRenderer;
            if (!hs)
                return;

            serializedObject.Update();

            GUILayout.Label("Data Asset", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_PropAsset);

            if (!hs.HasValidAsset)
            {
                var msg = hs.asset != null && hs.asset.formatVersion != HierarchicalSplatAsset.kCurrentVersion
                    ? "Hierarchical Splat asset version is not compatible, please recreate the asset"
                    : "Hierarchical Splat asset is not assigned or is empty";
                EditorGUILayout.HelpBox(msg, MessageType.Error);
            }
            EditorGUILayout.PropertyField(m_PropTau);
            m_ResourcesExpanded = EditorGUILayout.Foldout(m_ResourcesExpanded, "Resources", true, EditorStyles.foldoutHeader);
            if (m_ResourcesExpanded)
            {
                EditorGUILayout.PropertyField(m_PropShaderSplats);
                EditorGUILayout.PropertyField(m_PropShaderComposite);
                EditorGUILayout.PropertyField(m_PropCSSplatUtilities);
                EditorGUILayout.PropertyField(m_PropCSHierarchicalCut);
            }
            bool validAndEnabled = hs && hs.enabled && hs.gameObject.activeInHierarchy && hs.HasValidAsset;
            if (!validAndEnabled)// && !hs.HasValidRenderSetup)
            {
                EditorGUILayout.HelpBox("Shader resources are not set up", MessageType.Error);
                validAndEnabled = false;
            }
            serializedObject.ApplyModifiedProperties();
            /*var hs = target as HierarchicalSplatRenderer;
            if (!hs)
                return;

            serializedObject.Update();

            GUILayout.Label("Data Asset", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_PropAsset);

            if (!hs.HasValidAsset)
            {
                var msg = hs.asset != null && hs.asset.formatVersion != HierarchicalSplatAsset.kCurrentVersion
                    ? "Hierarchical Splat asset version is not compatible, please recreate the asset"
                    : "Hierarchical Splat asset is not assigned or is empty";
                EditorGUILayout.HelpBox(msg, MessageType.Error);
            }

            EditorGUILayout.Space();
            GUILayout.Label("Render Options", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_PropTau);

            EditorGUILayout.Space();
            m_ResourcesExpanded = EditorGUILayout.Foldout(m_ResourcesExpanded, "Resources", true, EditorStyles.foldoutHeader);
            if (m_ResourcesExpanded)
            {
                EditorGUILayout.PropertyField(m_PropShaderSplats);
                EditorGUILayout.PropertyField(m_PropShaderComposite);
                EditorGUILayout.PropertyField(m_PropShaderDebugPoints);
                EditorGUILayout.PropertyField(m_PropShaderDebugBoxes);
                EditorGUILayout.PropertyField(m_PropCSSplatUtilities);
            }
            bool validAndEnabled = hs && hs.enabled && hs.gameObject.activeInHierarchy && hs.HasValidAsset;
            if (validAndEnabled && !hs.HasValidRenderSetup)
            {
                EditorGUILayout.HelpBox("Shader resources are not set up", MessageType.Error);
                validAndEnabled = false;
            }

            if (validAndEnabled && targets.Length == 1)
            {
                EditGUI(hs);
            }
            if (validAndEnabled && targets.Length > 1)
            {
                MultiEditGUI();
            }

            serializedObject.ApplyModifiedProperties();*/
        }

        void MultiEditGUI()
        {
            /*DrawSeparator();
            CountTargetSplats(out var totalSplats, out var totalObjects);
            EditorGUILayout.LabelField("Total Objects", $"{totalObjects}");
            EditorGUILayout.LabelField("Total Splats", $"{totalSplats:N0}");
            if (totalSplats > HierarchicalSplatAsset.kMaxSplats)
            {
                EditorGUILayout.HelpBox($"Can't merge, too many splats (max. supported {HierarchicalSplatAsset.kMaxSplats:N0})", MessageType.Warning);
                return;
            }

            var targetHs = (HierarchicalSplatRenderer) target;
            if (!targetHs || !targetHs.HasValidAsset || !targetHs.isActiveAndEnabled)
            {
                EditorGUILayout.HelpBox($"Can't merge into {target.name} (no asset or disable)", MessageType.Warning);
                return;
            }*/
        }

        void CountTargetSplats(out int totalSplats, out int totalObjects)
        {
            totalObjects = 0;
            totalSplats = 0;
            foreach (var obj in targets)
            {
                var hs = obj as HierarchicalSplatRenderer;
                if (!hs || !hs.HasValidAsset || !hs.isActiveAndEnabled)
                    continue;
                ++totalObjects;
                totalSplats += hs.splatCount;
            }
        }

        void EditGUI(HierarchicalSplatRenderer hs)
        {
            /*++s_EditStatsUpdateCounter;

            DrawSeparator();
            bool wasToolActive = ToolManager.activeContextType == typeof(GaussianToolContext);
            GUILayout.BeginHorizontal();
            bool isToolActive = GUILayout.Toggle(wasToolActive, "Edit", EditorStyles.miniButton);
            using (new EditorGUI.DisabledScope(!hs.editModified))
            {
                if (GUILayout.Button("Reset", GUILayout.ExpandWidth(false)))
                {
                    if (EditorUtility.DisplayDialog("Reset Splat Modifications?",
                            $"This will reset edits of {hs.name} to match the {hs.asset.name} asset. Continue?",
                            "Yes, reset", "Cancel"))
                    {
                        hs.enabled = false;
                        hs.enabled = true;
                    }
                }
            }

            GUILayout.EndHorizontal();
            if (!wasToolActive && isToolActive)
            {
                ToolManager.SetActiveContext<GaussianToolContext>();
                if (Tools.current == Tool.View)
                    Tools.current = Tool.Move;
            }

            if (wasToolActive && !isToolActive)
            {
                ToolManager.SetActiveContext<GameObjectToolContext>();
            }

            if (isToolActive)
            {
                EditorGUILayout.HelpBox("Splat move/rotate/scale tools need Very High splat quality preset", MessageType.Warning);
            }

            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("No Cutouts"))
            {
                hs.m_Cutouts = Array.Empty<GaussianCutout>();
                hs.UpdateEditCountsAndBounds();
                EditorUtility.SetDirty(hs);
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.PropertyField(m_PropCutouts);

            bool hasCutouts = hs.m_Cutouts != null && hs.m_Cutouts.Length != 0;
            bool modifiedOrHasCutouts = hs.editModified || hasCutouts;

            var asset = hs.asset;
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            m_ExportBakeTransform = EditorGUILayout.Toggle("Export in world space", m_ExportBakeTransform);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(kPrefExportBake, m_ExportBakeTransform);
            }

            bool displayEditStats = isToolActive || modifiedOrHasCutouts;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Splats", $"{hs.splatCount:N0}");
            if (displayEditStats)
            {
                EditorGUILayout.LabelField("Cut", $"{hs.editCutSplats:N0}");
                EditorGUILayout.LabelField("Deleted", $"{hs.editDeletedSplats:N0}");
                EditorGUILayout.LabelField("Selected", $"{hs.editSelectedSplats:N0}");
                if (hasCutouts)
                {
                    if (s_EditStatsUpdateCounter > 10)
                    {
                        hs.UpdateEditCountsAndBounds();
                        s_EditStatsUpdateCounter = 0;
                    }
                }
            }*/
        }

        static void DrawSeparator()
        {
            EditorGUILayout.Space(12f, true);
            GUILayout.Box(GUIContent.none, "sv_iconselector_sep", GUILayout.Height(2), GUILayout.ExpandWidth(true));
            EditorGUILayout.Space();
        }

        bool HasFrameBounds()
        {
            return true;
        }

        /*Bounds OnGetFrameBounds()
        {
            var hs = target as HierarchicalSplatRenderer;
            if (!hs || !hs.HasValidRenderSetup)
                return new Bounds(Vector3.zero, Vector3.one);
            Bounds bounds = default;
            bounds.SetMinMax(hs.asset.boundsMin, hs.asset.boundsMax);
            if (hs.editSelectedSplats > 0)
            {
                bounds = hs.editSelectedBounds;
            }
            bounds.extents *= 0.7f;
            return TransformBounds(hs.transform, bounds);
            
        }*/

        /*public static Bounds TransformBounds(Transform tr, Bounds bounds )
        {
            var center = tr.TransformPoint(bounds.center);

            var ext = bounds.extents;
            var axisX = tr.TransformVector(ext.x, 0, 0);
            var axisY = tr.TransformVector(0, ext.y, 0);
            var axisZ = tr.TransformVector(0, 0, ext.z);

            // sum their absolute value to get the world extents
            ext.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
            ext.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
            ext.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

            return new Bounds { center = center, extents = ext };
        }*/
    }
}