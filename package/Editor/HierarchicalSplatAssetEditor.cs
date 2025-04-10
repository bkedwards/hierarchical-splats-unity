// SPDX-License-Identifier: MIT

using HierarchicalSplatting.Runtime;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

namespace HierarchicalSplatting.Editor
{
    [CustomEditor(typeof(HierarchicalSplatAsset))]
    [CanEditMultipleObjects]
    public class HierarchicalSplatAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (!(target is HierarchicalSplatAsset hs))
                return;


            using var _ = new EditorGUI.DisabledScope(true);

            SingleAssetGUI(hs);
        }

        static void SingleAssetGUI(HierarchicalSplatAsset hs)
        {
            var splatCount = hs.splatCount;
            EditorGUILayout.TextField("Splats", splatCount.ToString());

            // Get the cached memory size from the asset
            long totalSize = hs.CalculateMemorySize();
            string formattedTotalSize = EditorUtility.FormatBytes(totalSize);

            EditorGUILayout.LabelField("Memory", formattedTotalSize);

            // Display the memory breakdown
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Positions", EditorUtility.FormatBytes(splatCount * 3 * sizeof(float)));
            EditorGUILayout.LabelField("Scales", EditorUtility.FormatBytes(splatCount * 3 * sizeof(float)));
            EditorGUILayout.LabelField("Rotations", EditorUtility.FormatBytes(splatCount * 4 * sizeof(float)));
            EditorGUILayout.LabelField("Opacities", EditorUtility.FormatBytes(splatCount * sizeof(float)));
            EditorGUILayout.LabelField("SHs", EditorUtility.FormatBytes(splatCount * 48 * sizeof(float)));
            EditorGUILayout.LabelField("Nodes", EditorUtility.FormatBytes(splatCount * 7 * sizeof(int)));
            EditorGUILayout.LabelField("Boxes", EditorUtility.FormatBytes(splatCount * 8 * sizeof(float)));
            EditorGUI.indentLevel--;
        }
    }
}