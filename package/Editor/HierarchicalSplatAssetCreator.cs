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
    public unsafe class HierarchicalSplatAssetCreator : EditorWindow
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

            NativeArray<float3> eigenPos;
            NativeArray<float3> eigenScale;
            NativeArray<float4> eigenRot;
            NativeArray<SHs> shs;
            NativeArray<float> alphas;
            NativeArray<Node> nodes;
            NativeArray<Box> boxes;

            int P = HierarchyFileReader.LoadHierarchy(m_InputModelFile, out eigenPos, out shs,  out alphas,  out eigenScale,  out eigenRot, out nodes, out boxes);

            //Print(eigenPos, eigenRot, eigenScale, shs, alphas , nodes, boxes);

            if (P == 0)
            {
                EditorUtility.ClearProgressBar();
                DisposeHierarchy(ref eigenPos, ref eigenRot, ref eigenScale, ref shs, ref alphas, ref nodes, ref boxes);
                return;
            }

            EditorUtility.DisplayProgressBar(kProgressTitle, "Reading scaffold files", 0.5f);

            NativeArray<float3> skyboxPos;
            NativeArray<float3> skyboxScale;
            NativeArray<float4> skyboxRot;
            NativeArray<SHs> skyboxSh;
            NativeArray<float> skyboxAlpha;

            int skyboxNum = HierarchyFileReader.loadScaffold(m_InputScaffoldFile, out skyboxPos, out skyboxSh, out skyboxAlpha, out skyboxScale, out skyboxRot);

            Print(eigenPos, eigenRot, eigenScale, shs, alphas , default, default);

            string baseName = Path.GetFileNameWithoutExtension(FilePickerControl.PathToDisplayString(m_InputModelFile));

            EditorUtility.DisplayProgressBar(kProgressTitle, "Creating asset objects", 0.7f);
            
            HierarchicalSplatAsset asset = ScriptableObject.CreateInstance<HierarchicalSplatAsset>();
            asset.Initialize(P, skyboxNum);
            asset.name = baseName;
            EditorUtility.DisplayProgressBar(kProgressTitle, "Creating data hash", 0.75f);

            var dataHash = new Hash128((uint)asset.splatCount, (uint)asset.formatVersion, 0, 0);

            //This is the hierarchy data
            uint[] posData;
            uint[] otherData;
            float4[] colorData;
            uint[] shData;
            //This is the skybox data, with enough space allocated to hold all hierarchy data as well
            uint[] allPos;
            uint[] allOther;
            float4[] allColor;
            uint[] allSH;

            CreatePositionsData(eigenPos, skyboxPos, out posData, out allPos, ref dataHash);
            CreateOtherData(eigenScale, eigenRot, skyboxScale, skyboxRot, out otherData, out allOther, ref dataHash);
            CreateColorData(shs, alphas, skyboxSh, skyboxAlpha, out colorData, out allColor, ref dataHash);
            CreateSHData(shs, skyboxSh, out shData, out allSH, ref dataHash);
            asset.SetDataHash(dataHash);

            EditorUtility.DisplayProgressBar(kProgressTitle, "Initial texture import", 0.85f);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUncompressedImport);

            EditorUtility.DisplayProgressBar(kProgressTitle, "Setup data onto asset", 0.95f);

            asset.posData = posData;
            asset.otherData = otherData;
            asset.shData = shData;
            asset.colorData = colorData;

            asset.allPos = allPos;
            asset.allOther = allOther;
            asset.allColor = allColor;
            asset.allSH = allSH;

            var assetPath = Path.Combine(m_OutputFolder, $"{baseName}.asset");
            var savedAsset = CreateOrReplaceAsset(asset, assetPath);

            EditorUtility.DisplayProgressBar(kProgressTitle, "Saving assets", 0.99f);
            AssetDatabase.SaveAssets();
            
            EditorUtility.ClearProgressBar();

            Selection.activeObject = savedAsset;
        }

        [BurstCompile]
        struct CreatePositionsDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> m_InputPos;
            [NativeDisableParallelForRestriction] public NativeArray<uint> m_Output;

            public unsafe void Execute(int index)
            {
                float3 pos = m_InputPos[index];
                int baseIdx = index * 3;
                m_Output[baseIdx] = math.asuint(pos.x);
                m_Output[baseIdx + 1] = math.asuint(pos.y);
                m_Output[baseIdx + 2] = math.asuint(pos.z);
            }
        }
        static int NextMultipleOf(int size, int multipleOf)
        {
            return (size + multipleOf - 1) / multipleOf * multipleOf;
        }
        void CreatePositionsData(NativeArray<float3> eigenPos, NativeArray<float3> skyboxPos, out uint[] posData, out uint[] allPos, ref Hash128 dataHash)
        {
            int eigenDataLen = (eigenPos.Length) * 3; // 3 uints per float
            int allDataLen = (eigenPos.Length + skyboxPos.Length) * 3; 
            
            eigenDataLen = NextMultipleOf(eigenDataLen, 2);
            allDataLen = NextMultipleOf(allDataLen, 2);

            NativeArray<uint> eigenData = new NativeArray<uint>(eigenDataLen, Allocator.TempJob);
            NativeArray<uint> allData = new NativeArray<uint>(allDataLen, Allocator.TempJob);

            CreatePositionsDataJob eigenJob = new CreatePositionsDataJob
            {
                m_InputPos = eigenPos,
                m_Output = eigenData
            };
            eigenJob.Schedule(eigenPos.Length, 8192).Complete();

            CreatePositionsDataJob allJob = new CreatePositionsDataJob
            {
                m_InputPos = allPos,
                m_Output = allData
            };
            allJob.Schedule(skyboxPos.Length, 8192).Complete();

            dataHash.Append(allData);

            posData = new uint[eigenDataLen];
            eigenData.CopyTo(posData);
            allPos = new uint[allDataLen];
            allData.CopyTo(allPos);

            allData.Dispose();
            eigenData.Dispose();
        }

        [BurstCompile]
        struct CreateOtherDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> m_InputRot;
            [ReadOnly] public NativeArray<float3> m_InputScale;
            [NativeDisableParallelForRestriction] public NativeArray<uint> m_Output;

            public unsafe void Execute(int index)
            {
                float4 rotQ = m_InputRot[index];
                uint enc = (uint)(rotQ.x * 1023.5f) | ((uint)(rotQ.y * 1023.5f) << 10) | ((uint)(rotQ.z * 1023.5f) << 20) | ((uint)(rotQ.w * 3.5f) << 30);
                baseIdx = index * 4;
                m_Output[baseIdx] = enc;

                float3 scale = m_InputScale[index];
                m_Output[baseIdx + 1] = math.asuint(scale.x)
                m_Output[baseIdx + 2] = math.asuint(scale.y)
                m_Output[baseIdx + 3] = math.asuint(scale.z)
            }
        }

        void CreateOtherData(NativeArray<float3> eigenScale, NativeArray<float4> eigenRot, NativeArray<float3> skyboxScale, NativeArray<float4> skyboxRot, out uint[] otherData, out uint[] allOther, ref Hash128 dataHash)
        {
            int eigenDataLen = (eigenScale.Length) * 4; //4 uints in a float4
            int allDataLen = (eigenScale.Length + skyboxScale.Length) * 4;

            eigenDataLen = NextMultipleOf(eigenDataLen, 2);
            allDataLen = NextMultipleOf(allDataLen, 2);
            
            NativeArray<uint> eigenData = new NativeArray<uint>(eigenDataLen, Allocator.TempJob);
            NativeArray<uint> allData = new NativeArray<uint>(allDataLen, Allocator.TempJob);

            CreateOtherDataJob eigenJob = new CreateOtherDataJob
            {
                m_InputRot = eigenRot,
                m_InputScale = eigenScale,
                m_Output = eigenData
            };
            eigenJob.Schedule(eigenRot.Length, 8192).Complete();

            CreateOtherDataJob allJob = new CreateOtherDataJob
            {
                m_InputRot = skyboxRot,
                m_InputScale = skyboxScale,
                m_Output = allData
            };
            allJob.Schedule(skyboxRot.Length, 8192).Complete();

            otherData = new uint[eigenDataLen];
            eigenData.CopyTo(otherData);
            allOther = new uint[allDataLen];
            allData.CopyTo(allOther);

            allData.Dispose();
            eigenData.Dispose();

            dataHash.Append(data);
        }

        void Print(
            NativeArray<float3> eigenPos, 
            NativeArray<float4> eigenRot, 
            NativeArray<float3> eigenScale, 
            NativeArray<SHs> shs, 
            NativeArray<float> alphas, 
            NativeArray<Node> nodes, 
            NativeArray<Box> boxes)
        {
            string ans = "";
            for (int i = 0; i<5; i++)
            {
                ans += "(" + eigenPos[i].x.ToString() + ", " + eigenPos[i].y.ToString() + ", " + eigenPos[i].z.ToString() + ") ";
            }
            Debug.Log("Pos: " + ans);
            ans = "";
            for (int i = 0; i<5; i++)
            {
                ans += "(" + eigenScale[i].x.ToString() + ", " + eigenScale[i].y.ToString() + ", " + eigenScale[i].z.ToString() + ") ";
            }
            Debug.Log("Scale: " + ans);
            ans = "";
            for (int i = 0; i<5; i++)
            {
                ans += "(" + eigenRot[i].x.ToString() + ", " + eigenRot[i].y.ToString() + ", " + eigenRot[i].z.ToString() + ", " + eigenRot[i].w.ToString() + ") ";
            }
            Debug.Log("Rot: " + ans);
            ans = "";
            for (int i = 0; i<10; i++)
            {
                ans += alphas[i] + " ";
            }
            Debug.Log("alpha: " + ans);
            ans = "\n";
            for (int i = 0; i < 5; i++) 
            {
                ans += "\t[" + i.ToString() + "]: " + shs[i].dc0.ToString() + " " + shs[i].sh1.ToString() + " " + shs[i].sh2.ToString() + "\n";
            }
            Debug.Log("shs: " +  ans);
            if (nodes != default)
            {
                ans = "";
                for (int i = 0; i < 5; i++ ) {
                    ans += "\t[" + i.ToString() + "]: " + nodes[i].depth.ToString() + " " + nodes[i].parent.ToString() + " " + nodes[i].start.ToString() + " " + nodes[i].count_leafs.ToString() + " " + nodes[i].count_merged.ToString() + " "  + nodes[i].start_children.ToString() + " " + nodes[i].count_children.ToString() + " \n";

                }
                Debug.Log("nodes: " + ans);
            }
            if (boxes != default)
            {
                ans = "";
                for (int i = 0; i < 5; i++ ) {
                    ans += "\t[" + i.ToString() + "]: " + boxes[i].minn.ToString() + " " + boxes[i].maxx.ToString() + "\n";
                }
                Debug.Log("boxes: " + ans);
            }
        }

        [BurstCompile]
        struct CreateColorDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SHs> m_InputSHs;
            [ReadOnly] public NativeArray<float> m_InputAlphas;
            [NativeDisableParallelForRestriction] public NativeArray<float4> m_Output;

            public void Execute(int index)
            {
                SHs sh = m_InputSHs[index];
                m_Output[i] = new float4(sh.dc0.x, sh.dc0.y, sh.dc0.z, m_InputAlphas[index]);
            }
        }

        void CreateColorData(NativeArray<SHs> shs, NativeArray<float> alphas, NativeArray<SHs> skyboxSh, NativeArray<float> skyboxAlpha, out uint[] colorData, out uint[] allColor, ref Hash128 dataHash)
        {
            var (eigenWidth, eigenHeight) = HierarchicalSplatAsset.CalcTextureSize(shs.Length);
            var (allWidth, allHeight) = HierarchicalSplatAsset.CalcTextureSize(shs.Length + skyboxSh.Length);

            NativeArray<float4> eigenData = new(eigenWidth * eigenHeight, Allocator.TempJob);
            NativeArray<float4> allData = new(allWidth * allHeight, Allocator.TempJob);

            CreateColorDataJob eigenJob = new CreateColorDataJob
            {
                m_InputSHs = shs,
                m_InputAlphas = alphas,
                m_Output = eigenData
            };
            eigenJob.Schedule(shs.Length, 8192).Complete();

            CreateColorDataJob allJob = new CreateColorDataJob
            {
                m_InputSHs = skyboxSh,
                m_InputAlphas = skyboxAlpha,
                m_Output = allData
            };
            allJob.Schedule(skyboxSh.Length, 8192).Complete();

            colorData = new uint[eigenWidth * eigenHeight];
            eigenData.CopyTo(colorData);
            allColor = new uint[allWidth * allHeight];
            allData.CopyTo(allColor);

            allData.Dispose();
            eigenData.Dispose();

            dataHash.Append(allData);
        }

        [BurstCompile]
        public struct CreateSHDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SHs> m_Input; 
            [WriteOnly] public NativeArray<uint> m_Output;

            public unsafe void Execute(int index)
            {
                SH* shPointer = ((SH*)m_Input.GetUnsafePtr()) + index;
                uint* uintPointer = ((uint*)shPointer) + 3;
                int baseIdx = index * 48;
                for (int i = 0; i < 45; i++)
                {
                    m_Output[baseIdx + i] = *uintPointer;
                    uintPointer ++;
                }
                m_Output[baseIdx + 45] = 0;
                m_Output[baseIdx + 46] = 0;
                m_Output[baseIdx + 47] = 0;
            }

        }
        public string[] CreateSHData(NativeArray<SHs> shs, NativeArray<SHs> skyboxSh, out uint[] shData, out uint[] allSH, ref Hash128 dataHash)
        {
            int eigenDataLen = (shs.Length) * 48; // SH is 48 uints (16 * float3)
            int allDataLen = (shs.Length + skyboxSh.Length) * 48; 

            NativeArray<uint> eigenData = new NativeArray<uint>(eigenDataLen, Allocator.TempJob);
            NativeArray<uint> allData = new NativeArray<uint>(allDataLen, Allocator.TempJob);

            CreateSHDataJob eigenJob = new CreateSHDataJob
            {
                m_Input = shs,
                m_Output = eigenData
            };
            eigenJob.Schedule(shs.Length, 8192).Complete();

            CreateSHDataJob allJob = new CreateSHDataJob
            {
                m_Input = skyboxSh
                m_Output = allData
            };
            allJob.Schedule(skyboxSh.Length, 8192).Complete();

            shData = new uint[eigenDataLen];
            eigenData.CopyTo(shData);
            allSH = new uint[allDataLen];
            allData.CopyTo(allSH);

            allData.Dispose();
            eigenData.Dispose();

            dataHash.Append(data);
        }
    }
}