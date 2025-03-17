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
            EditorGUILayout.TextField("Splats", splatCount.ToString("N0"));
            var prevBackColor = GUI.backgroundColor;
            if (hs.formatVersion != HierarchicalSplatAsset.kCurrentVersion)
                GUI.backgroundColor *= Color.red;
            EditorGUILayout.IntField("Version", hs.formatVersion);
            GUI.backgroundColor = prevBackColor;

            long sizePos = hs.Pos.Length * 3 * sizeof(float);
            long sizeSHs = hs.SHs.Length * 48 * sizeof(float);
            long sizeRots = hs.Rots.Length * 4 * sizeof(float);
            long sizeScales = hs.Scales.Length * 3 * sizeof(float);
            long sizeAlphas = hs.Alphas.Length * sizeof(float);
            long sizeNodes = hs.Nodes.Length * 7 * sizeof(int);
            long sizeBoxes = hs.Boxes.Length * 8 * sizeof(float);

            long totalSize = sizePos + sizeSHs + sizeRots + sizeScales + sizeAlphas + sizeNodes + sizeBoxes;

            string formattedSizePos = EditorUtility.FormatBytes(sizePos);
            string formattedSizeRots = EditorUtility.FormatBytes(sizeRots);
            string formattedSizeScales = EditorUtility.FormatBytes(sizeScales);
            string formattedSizeAlphas = EditorUtility.FormatBytes(sizeAlphas);
            string formattedSizeSHs = EditorUtility.FormatBytes(sizeSHs);
            string formattedSizeNodes = EditorUtility.FormatBytes(sizeNodes);
            string formattedSizeBoxes = EditorUtility.FormatBytes(sizeBoxes);
            string formattedTotalSize = EditorUtility.FormatBytes(totalSize);

            EditorGUILayout.LabelField("Memory", formattedTotalSize);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Positions", formattedSizePos);
            EditorGUILayout.LabelField("Scales", formattedSizeScales);
            EditorGUILayout.LabelField("Rotations", formattedSizeRots);
            EditorGUILayout.LabelField("Opacities", formattedSizeAlphas);
            EditorGUILayout.LabelField("SHs", formattedSizeSHs);
            EditorGUILayout.LabelField("Nodes", formattedSizeNodes);
            EditorGUILayout.LabelField("Boxes", formattedSizeBoxes);
            EditorGUI.indentLevel--;

            EditorGUILayout.TextField("Data Hash", hs.dataHash.ToString());
        }
    }
}