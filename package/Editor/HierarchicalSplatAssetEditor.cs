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

            long sizePos = hs.posData?.dataSize ?? 0;
            long sizeOther = hs.otherData?.dataSize ?? 0;
            long sizeColor = hs.colorData?.dataSize ?? 0;
            long sizeSHs = 0;
            if (hs.shData != null)
            {
                for (int i = 0; i < hs.shData.Length; i++)
                {
                    sizeSHs += hs.shData[i].dataSize;
                }
            }
            long sizeNodes = hs.nodeData?.dataSize ?? 0;
            long sizeBoxes = hs.boxData?.dataSize ?? 0;

            long totalSize = sizePos + sizeOther + sizeColor + sizeSHs + sizeNodes + sizeBoxes;

            string formattedSizePos = EditorUtility.FormatBytes(sizePos);
            string formattedSizeOther = EditorUtility.FormatBytes(sizeOther);
            string formattedSizeColor = EditorUtility.FormatBytes(sizeColor);
            string formattedSizeSHs = EditorUtility.FormatBytes(sizeSHs);
            string formattedSizeNodes = EditorUtility.FormatBytes(sizeNodes);
            string formattedSizeBoxes = EditorUtility.FormatBytes(sizeBoxes);
            string formattedTotalSize = EditorUtility.FormatBytes(totalSize);

            EditorGUILayout.LabelField("Memory", formattedTotalSize);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Positions", formattedSizePos);
            EditorGUILayout.LabelField("Other Data", formattedSizeOther);
            EditorGUILayout.LabelField("Colors", formattedSizeColor);
            EditorGUILayout.LabelField("SHs", formattedSizeSHs);
            EditorGUILayout.LabelField("Nodes", formattedSizeNodes);
            EditorGUILayout.LabelField("Boxes", formattedSizeBoxes);
            EditorGUI.indentLevel--;

            EditorGUILayout.TextField("Data Hash", hs.dataHash.ToString());
        }
    }
}