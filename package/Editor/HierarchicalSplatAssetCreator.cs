// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using GaussianSplatting.Editor.Utils;
using HierarchicalSplatting.Editor.Utils;
using GaussianSplatting.Runtime;
using HierarchicalSplatting.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace HierarchicalSplatting.Editor
{
    [BurstCompile]
    public class HierarchicalSplatAssetCreator : EditorWindow
    {
        const string kProgressTitle = "Creating Hierarchical Splat Asset";
        const string kPrefOutputFolder = "nesnausk.GaussianSplatting.CreatorOutputFolder";

        readonly FilePickerControl m_FilePicker = new();
        [SerializeField] string m_InputModelFile;
        [SerializeField] string m_InputScaffoldFile;
        [SerializeField] string m_OutputFolder = "Assets/GaussianAssets";

        string m_ErrorMessage;
        string m_PrevFilePath;
        int m_PrevVertexCount;
        long m_PrevFileSize;

        [MenuItem("Tools/Gaussian Splats/Create HierarchicalSplat Asset")]
        public static void Init()
        {
            var window = GetWindowWithRect<HierarchicalSplatAssetCreator>(new Rect(50, 50, 360, 340), false, "Hierarchical Splat Creator", true);
            window.minSize = new Vector2(320, 320);
            window.maxSize = new Vector2(1500, 1500);
            window.Show();
        }

        void Awake()
        {
            m_OutputFolder = EditorPrefs.GetString(kPrefOutputFolder, "Assets/GaussianAssets");
        }

        void OnGUI()
        {
            EditorGUILayout.Space();
            GUILayout.Label("Input data", EditorStyles.boldLabel);

            var rect_model = EditorGUILayout.GetControlRect(true);
            var rect_scaffold = EditorGUILayout.GetControlRect(true);
            m_InputModelFile = m_FilePicker.PathFieldGUI(rect_model, new GUIContent("Input Merged Hierarchy"), m_InputModelFile, "hier", "ModelPathFile");
            m_InputScaffoldFile = m_FilePicker.PathFieldGUI(rect_scaffold, new GUIContent("Input Scaffold Path"), m_InputScaffoldFile, null, "ScaffoldPathFile");

            if (m_InputModelFile != m_PrevFilePath && !string.IsNullOrWhiteSpace(m_InputModelFile))
            {
                m_PrevVertexCount = HierarchyFileReader.ReadFileHeader(m_InputModelFile);
                m_PrevFileSize = File.Exists(m_InputModelFile) ? new FileInfo(m_InputModelFile).Length : 0;
                m_PrevFilePath = m_InputModelFile;
            }

            string formattedSize = (m_PrevVertexCount > 0)
                ? $"{EditorUtility.FormatBytes(m_PrevFileSize)} - {m_PrevVertexCount:N0} total splats"
                : string.Empty;

            if (m_PrevVertexCount > 0)
                EditorGUILayout.LabelField("File Size", formattedSize);
            else
                GUILayout.Space(EditorGUIUtility.singleLineHeight);

            EditorGUILayout.Space();
            GUILayout.Label("Output", EditorStyles.boldLabel);
            var rect = EditorGUILayout.GetControlRect(true);
            string newOutputFolder = m_FilePicker.PathFieldGUI(rect, new GUIContent("Output Folder"), m_OutputFolder, null, "GaussianAssetOutputFolder");
            
            if (newOutputFolder != m_OutputFolder)
            {
                m_OutputFolder = newOutputFolder;
                EditorPrefs.SetString(kPrefOutputFolder, m_OutputFolder);
            }
            
            GUILayout.Space(EditorGUIUtility.singleLineHeight);
            EditorGUILayout.Space();

            GUILayout.BeginHorizontal();
            GUILayout.Space(30);
            if (GUILayout.Button("Create Asset"))
            {
                CreateAsset();
            }
            GUILayout.Space(30);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(m_ErrorMessage))
            {
                EditorGUILayout.HelpBox(m_ErrorMessage, MessageType.Error);
            }
        }

        static T CreateOrReplaceAsset<T>(T asset, string path) where T : UnityEngine.Object
        {
            T result = AssetDatabase.LoadAssetAtPath<T>(path);
            if (result == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                result = asset;
            }
            else
            {
                if (typeof(Mesh).IsAssignableFrom(typeof(T))) { (result as Mesh)?.Clear(); }
                EditorUtility.CopySerialized(asset, result);
            }
            return result;
        }

        unsafe void CreateAsset()
        {
            m_ErrorMessage = null;
            if (string.IsNullOrWhiteSpace(m_InputModelFile))
            {
                m_ErrorMessage = $"Select merged hierarchy file";
                return;
            }
            if (string.IsNullOrWhiteSpace(m_InputScaffoldFile))
            {
                m_ErrorMessage = $"Select input scaffold file";
                return;
            }
            if (string.IsNullOrWhiteSpace(m_OutputFolder) || !m_OutputFolder.StartsWith("Assets/"))
            {
                m_ErrorMessage = $"Output folder must be within project, was '{m_OutputFolder}'";
                return;
            }
            if (!Directory.Exists(m_OutputFolder)) 
            {
                Directory.CreateDirectory(m_OutputFolder);
            }

            EditorUtility.DisplayProgressBar(kProgressTitle, "Reading merged hierarchy file", 0.0f);

            NativeArray<uint> pos;
            NativeArray<uint> other;
            NativeArray<float4> color;
            NativeArray<uint> shs;
            NativeArray<Node> nodes;
            NativeArray<Box> boxes;

            int splatCount = HierarchyFileReader.LoadHierarchy(m_InputModelFile, out pos, out shs,  out other,  out color, out nodes, out boxes);


            if (splatCount == 0)
            {
                EditorUtility.ClearProgressBar();
                DisposeHierarchy(ref pos, ref other, ref color, ref shs, ref nodes, ref boxes);
                return;
            }

            EditorUtility.DisplayProgressBar(kProgressTitle, "Reading scaffold files", 0.5f);

            NativeArray<uint> skyboxPos;
            NativeArray<uint> skyboxOther;
            NativeArray<float4> skyboxColor;
            NativeArray<uint> skyboxSHs;

            int skyboxNum = HierarchyFileReader.loadScaffold(m_InputScaffoldFile, out skyboxPos, out skyboxSHs, out skyboxColor, out skyboxOther);

            EditorUtility.DisplayProgressBar(kProgressTitle, "Sending Data Jobs", 0.7f);

            long budget = 16000L;
            int GAUSS_MEMLIMIT = (int)((budget * 1000000L - (484L * skyboxNum + 168L)) / 681L);
            if (GAUSS_MEMLIMIT < 0)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError("Memory budget insufficient");
                return;
            }
            GAUSS_MEMLIMIT = splatCount < GAUSS_MEMLIMIT ? splatCount : GAUSS_MEMLIMIT;
            int count = GAUSS_MEMLIMIT + skyboxNum;
            
            string baseName = Path.GetFileNameWithoutExtension(FilePickerControl.PathToDisplayString(m_InputModelFile));

            EditorUtility.DisplayProgressBar(kProgressTitle, "Creating Asset", 0.7f);
            
            HierarchicalSplatAsset asset = ScriptableObject.CreateInstance<HierarchicalSplatAsset>();
            asset.Initialize(splatCount, skyboxNum);
            asset.name = baseName;

            asset.posData = pos.ToArray();
            asset.otherData = other.ToArray();
            var nativeColorArray = color.ToArray();
            var converted = new Vector4[nativeColorArray.Length];
            for (int i = 0; i < nativeColorArray.Length; i++)
            {
                converted[i] = (Vector4)nativeColorArray[i];
            }
            asset.colorData = converted;
            asset.shData = shs.ToArray();
            asset.nodeData = nodes.ToArray();
            asset.boxData = boxes.ToArray();

            var (width, height) = HierarchicalSplatAsset.CalcTextureSize(count);
            asset.padded = width * height;

            asset.allPos = skyboxPos.ToArray();
            asset.allOther = skyboxOther.ToArray();
            asset.allSHs = skyboxSHs.ToArray();

            var nativeAllColorArray = skyboxColor.ToArray();
            var allConverted = new Vector4[nativeAllColorArray.Length];
            for (int i = 0; i < nativeAllColorArray.Length; i++)
            {
                allConverted[i] = (Vector4)nativeAllColorArray[i];
            }
            asset.colorData = allConverted;

            EditorUtility.DisplayProgressBar(kProgressTitle, "Initial texture import", 0.85f);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUncompressedImport);

            EditorUtility.DisplayProgressBar(kProgressTitle, "Setup data onto asset", 0.95f);

            var assetPath = Path.Combine(m_OutputFolder, $"{baseName}.asset");
            var savedAsset = CreateOrReplaceAsset(asset, assetPath);

            EditorUtility.DisplayProgressBar(kProgressTitle, "Saving assets", 0.99f);
            AssetDatabase.SaveAssets();
            
            EditorUtility.ClearProgressBar();

            Selection.activeObject = savedAsset;

            DisposeHierarchy(ref pos, ref other, ref color, ref shs, ref nodes, ref boxes);
            DisposeScaffold(ref skyboxPos, ref skyboxOther, ref skyboxColor, ref skyboxSHs);

        }

        void DisposeHierarchy(          
            ref NativeArray<uint> pos, 
            ref NativeArray<uint> other, 
            ref NativeArray<float4> color, 
            ref NativeArray<uint> shs, 
            ref NativeArray<Node> nodes, 
            ref NativeArray<Box> boxes) 
        {
            if (pos.IsCreated) pos.Dispose();
            if (other.IsCreated) other.Dispose();
            if (color.IsCreated) color.Dispose();
            if (shs.IsCreated) shs.Dispose();
            if (nodes.IsCreated) nodes.Dispose();
            if (boxes.IsCreated) boxes.Dispose();
        }

        void DisposeScaffold (
            ref NativeArray<uint> pos, 
            ref NativeArray<uint> other, 
            ref NativeArray<float4> color, 
            ref NativeArray<uint> shs
        )
        {
            if (pos.IsCreated) pos.Dispose();
            if (other.IsCreated) other.Dispose();
            if (color.IsCreated) color.Dispose();
            if (shs.IsCreated) shs.Dispose();
        }
    }
}