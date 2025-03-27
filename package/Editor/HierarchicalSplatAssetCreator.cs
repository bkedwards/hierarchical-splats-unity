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

            Vector3 [] eigenpos;
            Vector3 [] eigenscale;
            Vector4 [] eigenrot;
            SHs [] shs;
            float [] alphas;
            Node [] nodes;
            Box [] boxes;

            int P1 = HierarchyFileReader.LoadHierarchy(m_InputModelFile, out eigenpos, out shs,  out alphas,  out eigenscale,  out eigenrot, out nodes, out boxes);

            Print(eigenpos, eigenrot, eigenscale, shs, alphas , nodes, boxes);

            if (P1 == 0)
            {
                EditorUtility.ClearProgressBar();
                //DisposeHierarchy(ref eigenpos, ref eigenrot, ref eigenscale, ref shs, ref alphas, ref nodes, ref boxes);
                return;
            }
            Debug.Log($"CreateAsset::SH.Length: {shs.Length}");

            EditorUtility.DisplayProgressBar(kProgressTitle, "Reading scaffold files", 0.5f);

            Vector3 [] skyboxpos;
            Vector3 [] skyboxscale;
            Vector4 [] skyboxrot;
            SHs [] skyboxsh;
            float [] skyboxalpha;

            int skyboxnum = HierarchyFileReader.loadScaffold(m_InputScaffoldFile, out skyboxpos, out skyboxsh, out skyboxalpha, out skyboxscale, out skyboxrot);


            string baseName = Path.GetFileNameWithoutExtension(FilePickerControl.PathToDisplayString(m_InputModelFile));

            EditorUtility.DisplayProgressBar(kProgressTitle, "Creating asset objects", 0.7f);
            
            HierarchicalSplatAsset asset = ScriptableObject.CreateInstance<HierarchicalSplatAsset>();
            asset.Initialize(P1, skyboxnum);
            asset.name = baseName;
            EditorUtility.DisplayProgressBar(kProgressTitle, "Creating data hash", 0.75f);

            var dataHash = new Hash128((uint)asset.splatCount, (uint)asset.formatVersion, 0, 0);

            asset.SetDataHash(dataHash);

            EditorUtility.DisplayProgressBar(kProgressTitle, "Initial texture import", 0.85f);
            //AssetDatabase.Refresh(ImportAssetOptions.ForceUncompressedImport);

            EditorUtility.DisplayProgressBar(kProgressTitle, "Setup data onto asset", 0.95f);

            asset.SetHierarchyData(ref eigenpos, ref eigenscale, ref eigenrot, ref alphas, ref shs, ref boxes, ref nodes);
            asset.SetScaffoldData(ref skyboxpos, ref skyboxscale, ref skyboxrot, ref skyboxalpha, ref skyboxsh);

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
            [ReadOnly] public NativeArray<Vector3> m_InputPos;
            [NativeDisableParallelForRestriction] public NativeArray<byte> m_Output;

            public unsafe void Execute(int index)
            {
                byte* outputPtr = (byte*) m_Output.GetUnsafePtr() + index * 12;
                float3 v = m_InputPos[index];
                *(float*) outputPtr = v.x;
                *(float*) (outputPtr + 4) = v.y;
                *(float*) (outputPtr + 8) = v.z;
            }
        }

        void CreatePositionsData(NativeArray<Vector3> inputPos, string filePath, ref Hash128 dataHash)
        {
            int dataLen = inputPos.Length * 12; //sizeof(Vector3)
            NativeArray<byte> data = new(dataLen, Allocator.TempJob);

            CreatePositionsDataJob job = new CreatePositionsDataJob
            {
                m_InputPos = inputPos,
                m_Output = data
            };
            job.Schedule(inputPos.Length, 8192).Complete();

            dataHash.Append(data);

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                fs.Write(data);
            }

            data.Dispose();
        }

        [BurstCompile]
        struct CreateOtherDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Vector4> m_InputRot;
            [ReadOnly] public NativeArray<Vector3> m_InputScale;
            [NativeDisableParallelForRestriction] public NativeArray<byte> m_Output;

            public unsafe void Execute(int index)
            {
                byte* outputPtr = (byte*) m_Output.GetUnsafePtr() + index * 16;

                Vector4 rotQ = m_InputRot[index];
                uint enc = (uint)(rotQ.x * 1023.5f) | ((uint)(rotQ.y * 1023.5f) << 10) | ((uint)(rotQ.z * 1023.5f) << 20) | ((uint)(rotQ.w * 3.5f) << 30);
                *(uint*) outputPtr = enc;

                float3 v = m_InputScale[index];
                *(float*) (outputPtr + 4) = v.x;
                *(float*) (outputPtr + 8) = v.y;
                *(float*) (outputPtr + 12) = v.z;

            }
        }
        void CreateOtherData(NativeArray<Vector4> rot, NativeArray<Vector3> scale, string filePath, ref Hash128 dataHash)
        {
            int dataLen = rot.Length * 16;
            NativeArray<byte> data = new(dataLen, Allocator.TempJob);

            CreateOtherDataJob job = new CreateOtherDataJob
            {
                m_InputRot = rot,
                m_InputScale = scale,
                m_Output = data
            };
            job.Schedule(rot.Length, 8192).Complete();

            dataHash.Append(data);

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                fs.Write(data);
            }

            data.Dispose();
        }

        void Print(
            Vector3[] eigenpos, 
            Vector4[] eigenrot, 
            Vector3[] eigenscale, 
            SHs [] shs, 
            float [] alphas, 
            Node [] nodes, 
            Box [] boxes)
        {
            string ans = "";
            for (int i = 0; i<5; i++)
            {
                ans += "(" + eigenpos[i].x.ToString() + ", " + eigenpos[i].y.ToString() + ", " + eigenpos[i].z.ToString() + ") ";
            }
            Debug.Log("Pos: " + ans);
            ans = "";
            for (int i = 0; i<5; i++)
            {
                ans += "(" + eigenscale[i].x.ToString() + ", " + eigenscale[i].y.ToString() + ", " + eigenscale[i].z.ToString() + ") ";
            }
            Debug.Log("Scale: " + ans);
            ans = "";
            for (int i = 0; i<5; i++)
            {
                ans += "(" + eigenrot[i].x.ToString() + ", " + eigenrot[i].y.ToString() + ", " + eigenrot[i].z.ToString() + ", " + eigenrot[i].w.ToString() + ") ";
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


        static int SplatIndexToTextureIndex(uint idx)
        {
            uint width = HierarchicalSplatAsset.kTextureWidth;
            uint x = idx % width;
            uint y = idx / width;
            return (int)(y * width + x);
        }

        [BurstCompile]
        struct CreateColorDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SHs> m_InputSHs;
            [ReadOnly] public NativeArray<float> m_InputAlphas;
            [NativeDisableParallelForRestriction] public NativeArray<float4> m_Output;

            public void Execute(int index)
            {
                int i = SplatIndexToTextureIndex((uint)index);
                SHs sh = m_InputSHs[index];
                m_Output[i] = new float4(sh.dc0.x, sh.dc0.y, sh.dc0.z, m_InputAlphas[index]);
            }
        }

        [BurstCompile]
        struct ConvertColorJob : IJobParallelFor
        {
            public int width, height;
            [ReadOnly] public NativeArray<float4> inputData;
            [NativeDisableParallelForRestriction] public NativeArray<byte> outputData;
            public int formatBytesPerPixel;

            public unsafe void Execute(int y)
            {
                int srcIdx = y * width;
                byte* dstPtr = (byte*) outputData.GetUnsafePtr() + y * width * formatBytesPerPixel;
                for (int x = 0; x < width; ++x)
                {
                    float4 pix = inputData[srcIdx];

                    *(float4*) dstPtr = pix;

                    srcIdx++;
                    dstPtr += formatBytesPerPixel;
                }
            }
        }

        void CreateColorData(NativeArray<SHs> shs, NativeArray<float> alphas, string filePath, ref Hash128 dataHash)
        {
            var (width, height) = HierarchicalSplatAsset.CalcTextureSize(shs.Length);
            NativeArray<float4> data = new(width * height, Allocator.TempJob);

            CreateColorDataJob job = new CreateColorDataJob
            {
                m_InputSHs = shs,
                m_InputAlphas = alphas,
                m_Output = data
            };
            job.Schedule(shs.Length, 8192).Complete();

            dataHash.Append(data);
            dataHash.Append(0);

            GraphicsFormat gfxFormat = GraphicsFormat.R32G32B32A32_SFloat;
            int dstSize = (int)GraphicsFormatUtility.ComputeMipmapSize(width, height, gfxFormat);

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {

                if (GraphicsFormatUtility.IsCompressedFormat(gfxFormat))
                {
                    Texture2D tex = new Texture2D(width, height, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate);
                    tex.SetPixelData(data, 0);
                    EditorUtility.CompressTexture(tex, GraphicsFormatUtility.GetTextureFormat(gfxFormat), 100);
                    NativeArray<byte> cmpData = tex.GetPixelData<byte>(0);
                    
                    fs.Write(cmpData);
                    cmpData.Dispose();

                    DestroyImmediate(tex);
                }
                else
                {
                    ConvertColorJob jobConvert = new ConvertColorJob
                    {
                        width = width,
                        height = height,
                        inputData = data,
                        outputData = new NativeArray<byte>(dstSize, Allocator.TempJob),
                        formatBytesPerPixel = dstSize / width / height
                    };
                    jobConvert.Schedule(height, 1).Complete();
                    fs.Write(jobConvert.outputData);
                    jobConvert.outputData.Dispose();
                }
            }

            data.Dispose();
        }

        [BurstCompile]
        public struct CreateSHDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SHs> m_InputSHs; 
            [WriteOnly] public NativeArray<byte> m_Output;

            public long startOffset;

            public unsafe void Execute(int index)
            {

                long offset = startOffset + index * 192; // 48 * 4 bytes per SH struct

                SHs sh = m_InputSHs[index];

                byte* outputPtr = (byte*)m_Output.GetUnsafePtr() + index * 192;
                float* inputPtr = (float*)m_InputSHs.GetUnsafeReadOnlyPtr() + startOffset; 

                for (int i = 0; i < 48; i++)
                {
                    *(float*)outputPtr = inputPtr[i];
                    outputPtr += 4;
                }
            }

        }

        public string[] CreateSHData(NativeArray<SHs> shs, string fileName, ref Hash128 dataHash, ref bool batchFiles)
        {
            long dataLen = (long)shs.Length * 192; // 192 = 48 * 4. Total length of shs in bytes
            int maxBatchSize = 2013265920; //Slightly less than 2GB, maxmimum amount of SHs you can fit inside 2GB file
            int BatchSize = dataLen < (long)(maxBatchSize) ? (int)dataLen : (maxBatchSize);
            
            string[] filePaths;
            if (BatchSize == maxBatchSize) 
            {
                batchFiles = true;
                filePaths = new string[(int)(dataLen / (long)maxBatchSize + 1)];
            }
            else
            {
                batchFiles = false;
                filePaths = new string[1];
            }

            for (long i = 0; i < dataLen; i += (long)BatchSize)
            {
                int currentBatchSize = (dataLen - i) < (long)BatchSize ? (int)(dataLen - i) : BatchSize;

                NativeArray<byte> buffer = new NativeArray<byte>(currentBatchSize, Allocator.TempJob);

                CreateSHDataJob job = new CreateSHDataJob
                {
                    m_InputSHs = shs,
                    m_Output = buffer,
                    startOffset = i
                };

                job.Schedule(currentBatchSize / 192, 4096).Complete();

                dataHash.Append(buffer);
                
                string filePath = batchFiles ? fileName + (i / BatchSize).ToString() + "_shs.bytes" : fileName + "_shs.bytes";
                
                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(buffer);
                }
                filePaths[i / BatchSize] = filePath;
                buffer.Dispose();
            }


            return filePaths;
        }

        [BurstCompile]
        struct CreateNodeDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Node> m_InputNodes;
            [NativeDisableParallelForRestriction] public NativeArray<byte> m_Output;

            public unsafe void Execute(int index)
            {
                byte* outputPtr = (byte*) m_Output.GetUnsafePtr() + index * 28;
                Node n = m_InputNodes[index];
                *(int*) outputPtr = n.depth;
                *(int*) (outputPtr + 4) = n.parent;
                *(int*) (outputPtr + 8) = n.start;
                *(int*) (outputPtr + 12) = n.count_leafs;
                *(int*) (outputPtr + 16) = n.count_merged;
                *(int*) (outputPtr + 20) = n.start_children;
                *(int*) (outputPtr + 24) = n.count_children;
            }
        }

        void CreateNodeData(NativeArray<Node> inputNodes, string filePath, ref Hash128 dataHash)
        {
            int dataLen = inputNodes.Length * 28; //sizeof(Node) = 28 bytes
            NativeArray<byte> data = new(dataLen, Allocator.TempJob);

            CreateNodeDataJob job = new CreateNodeDataJob
            {
                m_InputNodes = inputNodes,
                m_Output = data
            };
            job.Schedule(inputNodes.Length, 8192).Complete();

            dataHash.Append(data);

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                fs.Write(data);
            }

            data.Dispose();
        }
        [BurstCompile]
        struct CreateBoxDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Box> m_InputBoxes;
            [NativeDisableParallelForRestriction] public NativeArray<byte> m_Output;

            public unsafe void Execute(int index)
            {
                byte* outputPtr = (byte*) m_Output.GetUnsafePtr() + index * 32;
                Box b = m_InputBoxes[index];
                *(float*) outputPtr = b.minn.x;
                *(float*) (outputPtr + 4) = b.minn.y;
                *(float*) (outputPtr + 8) = b.minn.z;
                *(float*) (outputPtr + 12) = b.maxx.x;
                *(float*) (outputPtr + 16) = b.maxx.y;
                *(float*) (outputPtr + 20) = b.maxx.z;
            }
        }

        void CreateBoxData(NativeArray<Box> inputBoxes, string filePath, ref Hash128 dataHash)
        {
            int dataLen = inputBoxes.Length * 32;
            NativeArray<byte> data = new(dataLen, Allocator.TempJob);

            CreateBoxDataJob job = new CreateBoxDataJob
            {
                m_InputBoxes = inputBoxes,
                m_Output = data
            };
            job.Schedule(inputBoxes.Length, 8192).Complete();

            dataHash.Append(data);

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                fs.Write(data);
            }

            data.Dispose();
        }
    }
}