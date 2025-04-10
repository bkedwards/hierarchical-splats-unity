// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR;
using GaussianSplatting.Runtime;

namespace HierarchicalSplatting.Runtime
{
    unsafe class HierarchicalSplatRenderSystem
    {
        // ReSharper disable MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        internal static readonly ProfilerMarker s_ProfDraw = new(ProfilerCategory.Render, "HierarchicalSplat.Draw", MarkerFlags.SampleGPU);
        internal static readonly ProfilerMarker s_ProfCompose = new(ProfilerCategory.Render, "HierarchicalSplat.Compose", MarkerFlags.SampleGPU);
        internal static readonly ProfilerMarker s_ProfCalcView = new(ProfilerCategory.Render, "HierarchicalSplat.CalcView", MarkerFlags.SampleGPU);
        // ReSharper restore MemberCanBePrivate.Global
        readonly HashSet<Camera> m_CameraCommandBuffersDone = new();
        public static HierarchicalSplatRenderSystem instance => ms_Instance ??= new HierarchicalSplatRenderSystem();
        static HierarchicalSplatRenderSystem ms_Instance;

        HierarchicalSplatRenderer hs;
        MaterialPropertyBlock mat;
        int frameCounter;
        bool cleanup;
        (int, int, MemSet) curr_res;
        CommandBuffer m_CommandBuffer;

        public void RegisterSplat(HierarchicalSplatRenderer r)
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                    Camera.onPreCull += OnPreCullCamera;
            hs = r;
            mat = new MaterialPropertyBlock();
            frameCounter = 0;
            cleanup = false;
        }



        public void UnregisterSplat(HierarchicalSplatRenderer r)
        {
            hs = null;
            mat = null;
            if (m_CameraCommandBuffersDone != null)
            {
                if (m_CommandBuffer != null)
                {
                    foreach (var cam in m_CameraCommandBuffersDone)
                    {
                        if (cam)
                            cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, m_CommandBuffer);
                    }
                }
            }
            m_CommandBuffer?.Dispose();
            m_CommandBuffer = null;
            Camera.onPreCull -= OnPreCullCamera;
        }

        // ReSharper disable once MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        public Material SortAndRenderSplats(Camera cam, CommandBuffer cmb)
        {
            Material matComposite = null;
            hs.EnsureMaterials();
            matComposite = hs.m_MatComposite;
            var mpb = mat;

            // sort
            var matrix = hs.transform.localToWorldMatrix;
            if (frameCounter == 1 || frameCounter%2 == 0)
                hs.SortPoints(cmb, cam, matrix);

            // cache view
            mat.Clear();
            Material displayMat = hs.m_MatSplats;
            if (displayMat == null)
                continue;

            hs.SetAssetDataOnMaterial(mpb);
            mpb.SetBuffer(HierarchicalSplatRenderer.Props.SplatChunks, hs.m_GpuChunks);

            mpb.SetBuffer(HierarchicalSplatRenderer.Props.SplatViewData, hs.m_GpuView);

            mpb.SetBuffer(HierarchicalSplatRenderer.Props.OrderBuffer, hs.m_GpuSortKeys);
            mpb.SetFloat(HierarchicalSplatRenderer.Props.SplatScale, hs.m_SplatScale);
            mpb.SetFloat(HierarchicalSplatRenderer.Props.SplatOpacityScale, hs.m_OpacityScale);
            mpb.SetFloat(HierarchicalSplatRenderer.Props.SplatSize, hs.m_PointDisplaySize);
            mpb.SetInteger(HierarchicalSplatRenderer.Props.SHOrder, hs.m_SHOrder);
            mpb.SetInteger(HierarchicalSplatRenderer.Props.SHOnly, hs.m_SHOnly ? 1 : 0);
            mpb.SetInteger(HierarchicalSplatRenderer.Props.DisplayIndex, 0);
            mpb.SetInteger(HierarchicalSplatRenderer.Props.DisplayChunks, 0);

            cmb.BeginSample(s_ProfCalcView);
            hs.CalcViewData(cmb, cam);
            cmb.EndSample(s_ProfCalcView);

            // draw
            int indexCount = 6;
            int instanceCount = hs.splatCount;
            MeshTopology topology = MeshTopology.Triangles;

            cmb.BeginSample(s_ProfDraw);
            cmb.DrawProcedural(hs.m_GpuIndexBuffer, matrix, displayMat, 0, topology, indexCount, instanceCount, mpb);
            cmb.EndSample(s_ProfDraw);
            return matComposite;
        }

        // ReSharper disable once MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        // ReSharper disable once UnusedMethodReturnValue.Global - used by HDRP/URP features that are not always compiled
        public CommandBuffer InitialClearCmdBuffer(Camera cam)
        {
            m_CommandBuffer ??= new CommandBuffer {name = "RenderHierarchicalSplats"};
            if (GraphicsSettings.currentRenderPipeline == null && cam != null && !m_CameraCommandBuffersDone.Contains(cam))
            {
                cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, m_CommandBuffer);
                m_CameraCommandBuffersDone.Add(cam);
            }

            // get render target for all splats
            m_CommandBuffer.Clear();
            return m_CommandBuffer;
        }
        void OnPreCullCamera(Camera cam)
        {
            if (!hs.resourcesAreSetUp || !hs.HasValidAsset)
                return;

            InitialClearCmdBuffer(cam);

            m_CommandBuffer.GetTemporaryRT(HierarchicalSplatRenderer.Props.GaussianSplatRT, -1, -1, 0, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat);
            m_CommandBuffer.SetRenderTarget(HierarchicalSplatRenderer.Props.GaussianSplatRT, BuiltinRenderTextureType.CurrentActive);
            m_CommandBuffer.ClearRenderTarget(RTClearFlags.Color, new Color(0, 0, 0, 0), 0, 0);
            m_CommandBuffer.SetGlobalTexture(HierarchicalSplatRenderer.Props.CameraTargetTexture, BuiltinRenderTextureType.CameraTarget);

            frameCounter++;   

            hs.SetViewpoint(cam);

            cleanup |= frameCounter % 10 == 0;
            if (frameCounter == 1 || frameCounter % 2 == 0)
            {

                if (frameCounter == 1)
                   curr_res = hs.CreateHierarchicalCut(false);

                (hs.currSet, hs.otherSet) = (hs.otherSet, hs.currSet);

                int numGetChildren = curr_res.Item1;

                if (curr_res.Item3 == hs.otherMem)
                    (hs.otherMem, hs.currMem) = (hs.currMem, hs.otherMem);

                if (numGetChildren != 0)
                    hs.DispatchSetStarts(1024, numGetChildren);
                
                hs.tau2Limit(cam);
                curr_res = hs.CreateHierarchicalCut(cleanup);
                cleanup = false;
            }

            hs.tau2Limit(cam);
            hs.DispatchComputeTsIndexed(1024);

            hs.CreateRenderBuffers();

            Material matComposite = SortAndRenderSplats(cam, m_CommandBuffer);

            m_CommandBuffer.BeginSample(s_ProfCompose);
            m_CommandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            m_CommandBuffer.DrawProcedural(Matrix4x4.identity, matComposite, 0, MeshTopology.Triangles, 3, 1);
            m_CommandBuffer.EndSample(s_ProfCompose);
            m_CommandBuffer.ReleaseTemporaryRT(HierarchicalSplatRenderer.Props.GaussianSplatRT);
        }
    
    }

    [ExecuteInEditMode]
    public unsafe class HierarchicalSplatRenderer : MonoBehaviour
    {
        [SerializeField] public HierarchicalSplatAsset m_Asset;

        [Range(0.1f, 2.0f)] [Tooltip("Additional scaling factor for the splats")]
        public float m_SplatScale = 1.0f;
        [Range(0.05f, 20.0f)] [Tooltip("Additional scaling factor for opacity")]
        public float m_OpacityScale = 1.0f;
        [Range(0, 3)] [Tooltip("Spherical Harmonics order to use")]
        public int m_SHOrder = 3;
        [Tooltip("Show only Spherical Harmonics contribution, using gray color")]
        public bool m_SHOnly;
        [Range(1,30)] [Tooltip("Sort splats only every N frames")]
        public int m_SortNthFrame = 1;
        [Range(1.00f, 20.0f)] [Tooltip("Granularity threshold for Node inclusion")]
        [SerializeField] public float tau = 9.0f;

        public int m_RenderMode = 0;
        [Range(1.0f,15.0f)] public float m_PointDisplaySize = 3.0f;

        public float sizeLimit = 0.03f;
        public Shader m_ShaderSplats;
        public Shader m_ShaderComposite;
        [Tooltip("Gaussian splatting compute shader")]
        public ComputeShader m_CSSplatUtilities;
        [Tooltip("Hierarchy Cut selection compute shader")]
        public ComputeShader m_CSHierarchicalCut;

        public uint[] CopyPos;
        public uint[] CopyOther;
        public float4[] CopyColor;
        public uint[] CopySHs;
        public Box[] CopyBoxes;
        public Node[] CopyNodes;

        int m_SplatCount;
        int GAUSS_MEMLIMIT;
        int ALLGAUSS;
        int numNeedChildren;
        public int numActiveNodesGpu;
        int nodesOffset = 0;
        int globalNodeCount = 0;
        int gaussiansOffset = 0;
        int skyboxOffset;
        Vector4 camPos;
        Vector4 camPosOld;
        Vector3 m_ZDirection;

        Matrix4x4 matView;
        Matrix4x4 matO2W;
        Matrix4x4 matW20;
        Vector4 screenPar;

        public LightSet currSet;
        public LightSet otherSet;
        public MemSet currMem;
        public MemSet otherMem;

        bool ranOut = false;

        int[] cuda2cpu;
        int[] packageParentStarts; //packageParentStarts
        int[] needChildren;
        int[] splits1;
        int[] splits2;
        int[] nodeIndices1; //active_nodes1
        int[] nodeIndices2;
        float[] interpTaus;
        int[] kids;

        GraphicsBuffer splitsBuff;
        GraphicsBuffer nodeIndices1Buff;
        GraphicsBuffer nodesToExpandBuff;
        GraphicsBuffer interpTausBuff;
        GraphicsBuffer kidsBuff;
        GraphicsBuffer NdstIBuff;
        GraphicsBuffer NsrcIBuff;
        GraphicsBuffer NsrcCBuff;
        GraphicsBuffer nodeIndices2Buff;
        GraphicsBuffer numIBuff;
        GraphicsBuffer outNBuff;

        GraphicsBuffer m_GpuSortDistances;
        internal GraphicsBuffer m_GpuSortKeys;
        GraphicsBuffer m_GpuPosData;
        GraphicsBuffer m_GpuOtherData;
        GraphicsBuffer m_GpuSHData;
        internal GraphicsBuffer m_GpuChunks;
        internal bool m_GpuChunksValid;
        Texture m_GpuColorData;
        internal GraphicsBuffer m_GpuView;
        internal GraphicsBuffer m_GpuIndexBuffer;
        GraphicsBuffer m_GpuEditCutouts;
        GpuSorting m_Sorter;
        GpuSorting.Args m_SorterArgs;

        internal Material m_MatSplats;
        internal Material m_MatComposite;
        internal int m_FrameCounter;
        HierarchicalSplatAsset m_PrevAsset;
        Hash128 m_PrevHash;
        bool m_Registered = false;

        static readonly ProfilerMarker s_ProfSort = new(ProfilerCategory.Render, "HierarchicalSplat.Sort", MarkerFlags.SampleGPU);

        internal static class Props
        {
            public static readonly int SplatPos = Shader.PropertyToID("_SplatPos");
            public static readonly int SplatOther = Shader.PropertyToID("_SplatOther");
            public static readonly int SplatSH = Shader.PropertyToID("_SplatSH");
            public static readonly int SplatColor = Shader.PropertyToID("_SplatColor");
            public static readonly int SplatBitsValid = Shader.PropertyToID("_SplatBitsValid");
            public static readonly int SplatViewData = Shader.PropertyToID("_SplatViewData");
            public static readonly int OrderBuffer = Shader.PropertyToID("_OrderBuffer");
            public static readonly int SplatScale = Shader.PropertyToID("_SplatScale");
            public static readonly int SplatOpacityScale = Shader.PropertyToID("_SplatOpacityScale");
            public static readonly int SplatSize = Shader.PropertyToID("_SplatSize");
            public static readonly int SplatCount = Shader.PropertyToID("_SplatCount");
            public static readonly int SHOrder = Shader.PropertyToID("_SHOrder");
            public static readonly int SHOnly = Shader.PropertyToID("_SHOnly");
            public static readonly int GaussianSplatRT = Shader.PropertyToID("_GaussianSplatRT");
            public static readonly int SplatSortKeys = Shader.PropertyToID("_SplatSortKeys");
            public static readonly int SplatSortDistances = Shader.PropertyToID("_SplatSortDistances");
            public static readonly int SrcBuffer = Shader.PropertyToID("_SrcBuffer");
            public static readonly int DstBuffer = Shader.PropertyToID("_DstBuffer");
            public static readonly int BufferSize = Shader.PropertyToID("_BufferSize");
            public static readonly int MatrixMV = Shader.PropertyToID("_MatrixMV");
            public static readonly int MatrixObjectToWorld = Shader.PropertyToID("_MatrixObjectToWorld");
            public static readonly int MatrixWorldToObject = Shader.PropertyToID("_MatrixWorldToObject");
            public static readonly int VecScreenParams = Shader.PropertyToID("_VecScreenParams");
            public static readonly int VecWorldSpaceCameraPos = Shader.PropertyToID("_VecWorldSpaceCameraPos");
            public static readonly int CameraTargetTexture = Shader.PropertyToID("_CameraTargetTexture");
            public static readonly int SelectionCenter = Shader.PropertyToID("_SelectionCenter");
            public static readonly int SelectionDelta = Shader.PropertyToID("_SelectionDelta");
            public static readonly int SelectionDeltaRot = Shader.PropertyToID("_SelectionDeltaRot");
            public static readonly int SelectionMode = Shader.PropertyToID("_SelectionMode");

        }

        public HierarchicalSplatAsset asset => m_Asset;
        public int splatCount => m_SplatCount;

        enum KernelIndices
        {
            SetIndices,
            CalcDistances,
            CalcViewData,
            ClearBuffer,
            InvertSelection,
            SelectAll,
            OrBuffers,
            SelectionUpdate,
            TranslateSelection,
            RotateSelection,
            ScaleSelection,
        }

        public bool HasValidAsset =>
            m_Asset != null &&
            m_Asset.splatCount > 0 &&
            m_Asset.scaffoldCount > 0 && 
            m_Asset.formatVersion == HierarchicalSplatAsset.kCurrentVersion &&
            m_Asset.posData != null &&
            m_Asset.otherData != null &&
            m_Asset.colorData != null &&
            m_Asset.shData != null &&
            m_Asset.nodeData != null &&
            m_Asset.boxData != null &&
            m_Asset.allPos != null &&
            m_Asset.allOther != null &&
            m_Asset.allColor != null &&
            m_Asset.allSHs != null;

        public bool HasValidRenderSetup => m_GpuPosData != null && m_GpuOtherData != null;
        const int kGpuViewDataSize = 40;

        void CreateResourcesForAsset()
        {            
            Debug.Log("CreateResourcesForAsset");
            if (!HasValidAsset)
                return;

            m_SplatCount = asset.splatCount;

            //Calculate GAUSS_MEMLIMIT and ALLGAUSS
            long budget = 16000L;
            GAUSS_MEMLIMIT = (int)((budget * 1000000L - (484L * asset.scaffoldCount + 168L)) / 681L);
            if (GAUSS_MEMLIMIT < 0)
            {
                Debug.LogError("Memory budget insufficient");
            }
            GAUSS_MEMLIMIT = asset.splatCount < GAUSS_MEMLIMIT ? asset.splatCount : GAUSS_MEMLIMIT;

            skyboxOffset = asset.scaffoldCount;

            ALLGAUSS = (GAUSS_MEMLIMIT + asset.scaffoldCount);

            currSet = new LightSet(GAUSS_MEMLIMIT);
            otherSet = new LightSet(GAUSS_MEMLIMIT);

            currMem = new MemSet(ALLGAUSS, GAUSS_MEMLIMIT, asset.allColor.Length);
            currMem.posBuff.SetData(asset.allPos, 0, 0, asset.allPos.Length);
            currMem.otherBuff.SetData(asset.allOther, 0, 0, asset.allOther.Length);
            currMem.colorBuff.SetData(asset.allColor, 0, 0, asset.allColor.Length);
            currMem.shsBuff.SetData(asset.allSHs, 0, 0, asset.allSHs.Length);

            otherMem = new MemSet(ALLGAUSS, GAUSS_MEMLIMIT, asset.allColor.Length);
            otherMem.posBuff.SetData(asset.allPos, 0, 0, asset.allPos.Length);
            otherMem.otherBuff.SetData(asset.allOther, 0, 0, asset.allOther.Length);
            otherMem.colorBuff.SetData(asset.allColor, 0, 0, asset.allColor.Length);
            otherMem.shsBuff.SetData(asset.allSHs, 0, 0, asset.allSHs.Length);

            CopyPos = new uint[GAUSS_MEMLIMIT * 3];
            CopyOther = new uint[GAUSS_MEMLIMIT * 3];
            CopyColor = new float4[GAUSS_MEMLIMIT];
            CopySHs = new uint [GAUSS_MEMLIMIT * 48];
            CopyBoxes = new Box [GAUSS_MEMLIMIT];
            CopyNodes = new Node [GAUSS_MEMLIMIT];
            
            //cam_pos
            //cam_pos_old
            //new_gauss_count -- used in clean up operations
            //newG -- used in clean up 
            //renderhelper -- used in cleanup

            //activenodes1, activenodes2, render_indices on cpu?

            //cuda2cpu1_cuda -- cleanup
            //cuda2cpu2_cuda -- cleanup
            //rect_cuda -- forward
            //radii_cuda -- forward
            //NsrcI2 -- cleanup
            //NdstI2 -- cleanup


            cuda2cpu = new int [GAUSS_MEMLIMIT];
            packageParentStarts = new int[GAUSS_MEMLIMIT];
            needChildren = new int[GAUSS_MEMLIMIT];
            splits1 = new int [GAUSS_MEMLIMIT];
            splits2 = new int [GAUSS_MEMLIMIT];
            nodeIndices1 = new int [GAUSS_MEMLIMIT];
            nodeIndices2 = new int [GAUSS_MEMLIMIT];
            interpTaus = new float[GAUSS_MEMLIMIT];
            kids = new int[GAUSS_MEMLIMIT];

            nodeIndices1[0] = 0;
            numActiveNodesGpu = 1;

            AddNodePackage(new int[] {0}, new int[] {-1}, currMem);

            InitGraphicsBuffers();
            SetGraphicsBuffers();

            m_GpuChunks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1,
            UnsafeUtility.SizeOf<HierarchicalSplatAsset.ChunkInfo>()) {name = "HierarchicalChunkData"};
            m_GpuChunksValid = false;

            m_GpuIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, 36, 2);
            m_GpuIndexBuffer.SetData(new ushort[]
            {
                0, 1, 2, 1, 3, 2,
                4, 6, 5, 5, 6, 7,
                0, 2, 4, 4, 2, 6,
                1, 5, 3, 5, 7, 3,
                0, 4, 1, 4, 5, 1,
                2, 3, 6, 3, 7, 6
            });
        }

        public void CreateRenderBuffers()
        {
            m_GpuPosData = currMem.posBuff;
            m_GpuOtherData = currMem.otherBuff;
            m_GpuSHData = currMem.shsBuff;

            int toRender = currSet.toRender + asset.scaffoldCount;
            var (texWidth, texHeight) = HierarchicalSplatAsset.CalcTextureSize(toRender);
            var texFormat = GraphicsFormat.R32G32B32A32_SFloat;
            var tex = new Texture2D(texWidth, texHeight, texFormat, TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate)   { name = "HierarchicalColorData" };

            float4[] colorArr = newfloat4[toRender];
            currMem.colorBuff.GetData(colorArr);
            tex.SetPixelData(colorArr, 0 , 0, colorArr.Length);
            tex.Apply(false, true);
            m_GpuColorData = tex;

            m_GpuView = new GraphicsBuffer(GraphicsBuffer.Target.Structured, toRender, kGpuViewDataSize);

            InitSortBuffers(toRender);
        }
        void InitGraphicsBuffers()
        {
            splitsBuff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            splitsBuff.SetData(splits1);
            nodeIndices1Buff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            nodeIndices1Buff.SetData(nodeIndices1);
            nodesToExpandBuff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            nodesToExpandBuff.SetData(splits1);
            interpTausBuff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(float));
            kidsBuff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            NsrcIBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GAUSS_MEMLIMIT, sizeof(int));
            NsrcIBuff.SetData(splits1);
            NdstIBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GAUSS_MEMLIMIT, sizeof(int));
            NdstIBuff.SetData(splits1);
            NsrcCBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GAUSS_MEMLIMIT, sizeof(int));
            NsrcCBuff.SetData(splits1);
            nodeIndices2Buff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            nodeIndices2Buff.SetData(nodeIndices2);
            numIBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int));
            numIBuff.SetData(new int[] { 0 });
            outNBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int));
            outNBuff.SetData(new int[] { 0 });
        }

        void SetGraphicsBuffers()
        {
            m_CSHierarchicalCut.SetBuffer(0, "splits", splitsBuff);
            m_CSHierarchicalCut.SetBuffer(2, "splits", splitsBuff);

            m_CSHierarchicalCut.SetBuffer(6, "nodes_to_expand", nodesToExpandBuff);

            m_CSHierarchicalCut.SetBuffer(5, "interpTaus", interpTausBuff);
            m_CSHierarchicalCut.SetBuffer(5, "kids", kidsBuff);

            m_CSHierarchicalCut.SetBuffer(0, "NdstI", NdstIBuff);
            m_CSHierarchicalCut.SetBuffer(1, "NdstI", NdstIBuff);
            m_CSHierarchicalCut.SetBuffer(3, "NdstI", NdstIBuff);
            m_CSHierarchicalCut.SetBuffer(6, "NdstI", NdstIBuff);
            m_CSHierarchicalCut.SetBuffer(7, "NdstI", NdstIBuff);

            m_CSHierarchicalCut.SetBuffer(0, "NsrcI", NsrcIBuff);
            m_CSHierarchicalCut.SetBuffer(1, "NsrcI", NsrcIBuff);
            m_CSHierarchicalCut.SetBuffer(2, "NsrcI", NsrcIBuff);
            m_CSHierarchicalCut.SetBuffer(3, "NsrcI", NsrcIBuff);
            m_CSHierarchicalCut.SetBuffer(4, "NsrcI", NsrcIBuff);
            m_CSHierarchicalCut.SetBuffer(6, "NsrcI", NsrcIBuff);
            m_CSHierarchicalCut.SetBuffer(7, "NsrcI", NsrcIBuff);

            m_CSHierarchicalCut.SetBuffer(0, "NsrcC", NsrcCBuff);
            m_CSHierarchicalCut.SetBuffer(6, "NsrcC", NsrcCBuff);
            m_CSHierarchicalCut.SetBuffer(7, "NsrcC", NsrcCBuff);

            m_CSHierarchicalCut.SetBuffer(6, "numI", numIBuff);
            m_CSHierarchicalCut.SetBuffer(7, "outN", outNBuff);

            m_CSHierarchicalCut.SetInt("N", numActiveNodesGpu);
        }

        bool AddNodePackage(int[] nIndices, int[] pIndices, MemSet useMem)
        {
            int nodeCopyCount = nIndices.Length;
            int gaussianCopyCount = 0;
            foreach (int id in nIndices) 
            {
                Node node = asset.nodeData[id];
                gaussianCopyCount  += node.count_leafs + node.count_merged;
            }

            if (nodeCopyCount + nodesOffset > GAUSS_MEMLIMIT ||
                gaussianCopyCount + gaussiansOffset > GAUSS_MEMLIMIT)
            {
                if (tau == 0)
                    tau = 1.0f;
                tau *= 1.05f;
                return false;
            }
            
            int copiedGaussians = 0;
            for (int i = 0; i < nIndices.Length; i++)
            {
                int id = nIndices[i];
                int parent = pIndices[i];
                Node node = asset.nodeData[id];

                int count = node.count_leafs + node.count_merged;
                for (int j = 0; j < count; j++)
                {
                    int src = node.start + j;
                    int dst = copiedGaussians + j;

                    // Pos: 3 uints per splat
                    CopyPos[dst * 3 + 0] = asset.posData[src * 3 + 0];
                    CopyPos[dst * 3 + 1] = asset.posData[src * 3 + 1];
                    CopyPos[dst * 3 + 2] = asset.posData[src * 3 + 2];

                    // Other: 4 uints per splat
                    CopyOther[dst * 4 + 0] = asset.otherData[src * 4 + 0];
                    CopyOther[dst * 4 + 1] = asset.otherData[src * 4 + 1];
                    CopyOther[dst * 4 + 2] = asset.otherData[src * 4 + 2];
                    CopyOther[dst * 4 + 3] = asset.otherData[src * 4 + 3];

                    // Color: 1 float4 per splat
                    CopyColor[dst] = asset.colorData[src];

                    // SH: 48 uints per splat
                    for (int k = 0; k < 48; k++)
                        CopySHs[dst * 48 + k] = asset.shData[src * 48 + k];
                }
                node.start_children = -1;
                node.start = gaussiansOffset + copiedGaussians;
                node.parent = parent;

                CopyNodes[i] = node;
                CopyBoxes[i] = asset.boxData[id];

                cuda2cpu[nodesOffset + i] = id;

                copiedGaussians += count;
            }

            int totalOffset = gaussiansOffset + asset.scaffoldCount;

            useMem.posBuff.SetData(CopyPos, 0, totalOffset * 3, gaussianCopyCount * 3);
            useMem.otherBuff.SetData(CopyOther, 0, totalOffset * 4, gaussianCopyCount * 4);
            useMem.colorBuff.SetData(CopyColor, 0, totalOffset, gaussianCopyCount);
            useMem.shsBuff.SetData(CopySHs, 0, totalOffset * 48, gaussianCopyCount * 48);
            useMem.nodesBuff.SetData(CopyNodes, 0, nodesOffset, nodeCopyCount);
            useMem.boxesBuff.SetData(CopyBoxes, 0, nodesOffset, nodeCopyCount);

            gaussiansOffset += gaussianCopyCount;
            nodesOffset += nodeCopyCount;

            globalNodeCount = nodeCopyCount;
            return true;

        }

        int createNodePackage(out int[] nIndices, out int[] pIndices)   //this is using the direct asset.Nodes --> no memSet at all
        {

            nodesToExpandBuff.GetData(needChildren, 0, 0, numNeedChildren);
            int numGetChildren = 0;
            int nodePackageCount = 0;
            for (int i = 0; i< numNeedChildren; i++)
            {
                int id = needChildren[i];
                int node_id = cuda2cpu[id];
                nodePackageCount += asset.nodeData[node_id].count_children;

                numGetChildren++;
            }

            nIndices = new int[nodePackageCount];
            pIndices = new int[nodePackageCount];

            int nodes_expanded = 0;
            for (int k = 0; k < numGetChildren; k++)
            {
                int id = needChildren[k];
                int node_id = cuda2cpu[id];
                Node node = asset.nodeData[node_id];
                for (int i = 0; i < node.count_children; i++)
                {
                    nIndices[nodes_expanded + i] = node.start_children + i;
                    pIndices[nodes_expanded + i] = id;
                }
                packageParentStarts[k] = nodesOffset + nodes_expanded;
                nodes_expanded += node.count_children;
            }

            return numGetChildren;
        }

        public void printSizeStep()
        {
            int[] h_active1 = new int[GAUSS_MEMLIMIT];
            int[] h_active2 = new int[GAUSS_MEMLIMIT];
            int[] h_splits = new int[GAUSS_MEMLIMIT];
            int[] h_render = new int[GAUSS_MEMLIMIT];
            int[] h_parent = new int[GAUSS_MEMLIMIT];
            int[] h_node_render = new int[GAUSS_MEMLIMIT];
            int[] h_expand = new int[GAUSS_MEMLIMIT];
            int[] h_nsrci = new int[GAUSS_MEMLIMIT];
            int[] h_ndsti = new int[GAUSS_MEMLIMIT];
            int[] h_nsrcc = new int[GAUSS_MEMLIMIT];
            int[] h_numi = new int[1];
            Node[] h_nodes = new Node[globalNodeCount];

            // Using GraphicsBuffer.GetData to retrieve data from GPU buffers
            nodeIndices1Buff.GetData(h_active1, 0, 0, numActiveNodesGpu);
            nodeIndices2Buff.GetData(h_active2, 0, 0, numActiveNodesGpu);
            currMem.nodesBuff.GetData(h_nodes, 0, nodesOffset, globalNodeCount);
            splitsBuff.GetData(h_splits, 0, 0, GAUSS_MEMLIMIT);
            otherSet.renderIndicesBuff.GetData(h_render, 0, 0, GAUSS_MEMLIMIT);
            otherSet.parentIndicesBuff.GetData(h_parent, 0, 0, GAUSS_MEMLIMIT);
            otherSet.nodesOfRenderIndicesBuff.GetData(h_node_render, 0, 0, GAUSS_MEMLIMIT);
            nodesToExpandBuff.GetData(h_expand, 0, 0, GAUSS_MEMLIMIT);
            NsrcIBuff.GetData(h_nsrci, 0, 0, GAUSS_MEMLIMIT);
            NdstIBuff.GetData(h_ndsti, 0, 0, GAUSS_MEMLIMIT);
            NsrcCBuff.GetData(h_nsrcc, 0, 0, GAUSS_MEMLIMIT);
            numIBuff.GetData(h_numi, 0, 0, 1);

            string ans = "";
            // Log values for debugging
            Debug.Log("\nsizeLimit: " + sizeLimit);
            Debug.Log("numActiveNodesGpu: " + numActiveNodesGpu);

            for (int i = 0; i < Mathf.Min(20, numActiveNodesGpu); i++)
            {
                ans += h_active1[i] + " ";
            }
            Debug.Log("activenodes1_cuda: " + ans);

            ans = "";
            for (int i = 0; i < Mathf.Min(20, numActiveNodesGpu); i++)
            {
                ans += h_active2[i] + " ";
            }
            Debug.Log("activenodes2_cuda: " + ans);

            ans = "";
            for (int i = 0; i < Mathf.Min(5, globalNodeCount); i++)
            {
                ans += $" [{i}] {h_nodes[i].depth} {h_nodes[i].parent} {h_nodes[i].start} {h_nodes[i].count_leafs} {h_nodes[i].count_merged} {h_nodes[i].start_children} {h_nodes[i].count_children}\n";
            }
            Debug.Log("useMem->nodes_cuda: " + ans);

            ans = "";
            for (int i = 0; i < 20; i++)
            {
                ans += h_splits[i] + " ";
            }
            Debug.Log("splits1_cuda: " + ans);

            ans = "";
            for (int i = 0; i < 20; i++)
            {
                ans+=h_render[i] + " ";
            }
            Debug.Log("otherSet->render_indices: " + ans);

            ans = "";
            for (int i = 0; i < 20; i++)
            {
                ans += h_parent[i] + " ";
            }
            Debug.Log("otherSet->parent_indices: " + ans);

            ans = "";
            for (int i = 0; i < 20; i++)
            {
                ans+=h_node_render[i] + " ";
            }
            Debug.Log("otherSet->nodes_of_render_indices: " + ans);

            ans = "";
            for (int i = 0; i < 20; i++)
            {
                ans+= h_expand[i] + " ";
            }
            Debug.Log("nodes_to_expand_cuda: " + ans);

            ans = "";
            for (int i = 0; i < 20; i++)
            {
                ans+= h_nsrci[i] + " ";
            }
            Debug.Log("NsrcI: " + ans);

            ans = "";
            for (int i = 0; i < 20; i++)
            {
                ans+= h_ndsti[i] + " ";
            }
            Debug.Log("NdstI: " + ans);

            ans = "";
            for (int i = 0; i < 20; i++)
            {
                ans+= h_nsrcc[i] + " ";
            }
            Debug.Log("NsrcC: " + ans);

            Debug.Log("numI: " + h_numi[0]);

            Debug.Log("otherSet->to_render: " + otherSet.toRender);

            Debug.Log("numNeedChildren: " + numNeedChildren);
        }

        // AsyncTask
        public (int, int, MemSet) CreateHierarchicalCut(bool cleanup)
        {

            MemSet useMem = currMem;

            //printSizeStep();

            if (!changeToSizeStep(useMem))
                Debug.LogError("Doing a step didn't work");

            (nodeIndices1Buff, nodeIndices2Buff) = (nodeIndices2Buff, nodeIndices1Buff);

            int numGetChildren = 0;
            int numTransferred = 0;
            if (!cleanup && numNeedChildren > 0)
            {
                if (!ranOut)
                {
                    int[] packageIndices;
                    int[] packageParentIndices;

                    int numNewParents = createNodePackage(out packageIndices, out packageParentIndices);

                    if (AddNodePackage(packageIndices, packageParentIndices, useMem))
                    {
                        NsrcIBuff.SetData(packageParentStarts);
                        numTransferred = packageIndices.Length;

                        numGetChildren = numNewParents;
                    }
                    else
                    {
                        ranOut = true;
                    }
                }
            }

            /*
                Cleanup Commands
            */

            return (numGetChildren, numTransferred, useMem);
        }

        bool changeToSizeStep(MemSet useMem)
        {
            m_CSHierarchicalCut.SetBuffer(0, "node_indices", nodeIndices1Buff);
            m_CSHierarchicalCut.SetBuffer(1, "node_indices", nodeIndices1Buff);

            numIBuff.SetData(new int[] { 0 });

            m_CSHierarchicalCut.SetBuffer(1, "new_node_indices", nodeIndices2Buff);
            m_CSHierarchicalCut.SetBuffer(2, "new_node_indices", nodeIndices2Buff);
            m_CSHierarchicalCut.SetBuffer(3, "new_node_indices", nodeIndices2Buff);

            m_CSHierarchicalCut.SetBuffer(0, "nodes", useMem.nodesBuff);
            m_CSHierarchicalCut.SetBuffer(1, "nodes", useMem.nodesBuff);
            m_CSHierarchicalCut.SetBuffer(2, "nodes", useMem.nodesBuff);
            m_CSHierarchicalCut.SetBuffer(3, "nodes", useMem.nodesBuff);
            m_CSHierarchicalCut.SetBuffer(4, "nodes", useMem.nodesBuff);

            m_CSHierarchicalCut.SetBuffer(0, "boxes", useMem.boxesBuff);
            
            m_CSHierarchicalCut.SetBuffer(3, "render_indices", otherSet.renderIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(3, "parent_indices", otherSet.parentIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(3, "nodes_of_render_indices", otherSet.nodesOfRenderIndicesBuff);

            m_CSHierarchicalCut.SetVector("viewpoint", cam_pos);
            m_CSHierarchicalCut.SetFloat("target_size", sizeLimit);

            int numNodeBlocks = (numActiveNodesGpu + 255) / 256;

            DispatchChangeNodesShader(numNodeBlocks);
            DispatchFlaggedShader(1024, numActiveNodesGpu);
            DispatchInclusiveSumShader(1024, numActiveNodesGpu);

            int prevNumActiveNodesGpu = numActiveNodesGpu;

            int [] buffer = new int[1];
            numIBuff.GetData(buffer);
            numNeedChildren = buffer[0];

            NdstIBuff.GetData(buffer, 0, numActiveNodesGpu - 1, 1);
            numActiveNodesGpu = buffer[0];

            if (numActiveNodesGpu > GAUSS_MEMLIMIT)
                return false;

            DispatchPutNodesShader(numNodeBlocks, prevNumActiveNodesGpu);

            int numRenderBlocks = (numActiveNodesGpu + 255) / 256;

            DispatchRenderIndicesIndexed(numRenderBlocks, numActiveNodesGpu);
            DispatchInclusiveSumShader(1024, numActiveNodesGpu);
            DispatchPutRenderIndicesIndexed(numRenderBlocks);

            NdstIBuff.GetData(buffer, 0, numActiveNodesGpu - 1, 1);
            otherSet.toRender = buffer[0];

            return true;
        }

        public void DispatchChangeNodesShader(int threads) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("changeNodesOnce");
            m_CSHierarchicalCut.SetInt("N", numActiveNodesGpu);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);
        }
        public void DispatchPutNodesShader(int threads, int N) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("putNodes");
            m_CSHierarchicalCut.SetInt("N", N);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);
        }
        public void DispatchRenderIndicesIndexed(int threads, int N) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("countRenderIndicesIndexed");
            m_CSHierarchicalCut.SetInt("N", N);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);
        }
        public void DispatchPutRenderIndicesIndexed(int threads) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("putRenderIndicesIndexed");
            m_CSHierarchicalCut.SetInt("N", numActiveNodesGpu);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);
        }

        public void DispatchFlaggedShader(int threads, int N) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("Flagged");
            m_CSHierarchicalCut.SetInt("N", N);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);
        }

        public void DispatchInclusiveSumShader(int threads, int N) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("InclusiveSum");
            m_CSHierarchicalCut.SetInt("N", N);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);
        }

        public void DispatchSetStarts(int threads, int numGetChildren) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("setStarts");
            m_CSHierarchicalCut.SetBuffer(kernel, "nodes", currMem.nodesBuff);
            m_CSHierarchicalCut.SetBuffer(kernel, "nodes_to_expand", nodesToExpandBuff);
            m_CSHierarchicalCut.SetInt("N", numGetChildren);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);
        }

        public void DispatchComputeTsIndexed(int threads) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("computeTsIndexed");
            m_CSHierarchicalCut.SetInt("to_render_num", currSet.toRender);
            m_CSHierarchicalCut.SetFloat("target_size", sizeLimit);
            m_CSHierarchicalCut.SetBuffer(kernel, "nodes_of_render_indices", currSet.nodesOfRenderIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(kernel, "nodes", currMem.nodesBuff);
            m_CSHierarchicalCut.SetBuffer(kernel, "boxes", currMem.boxesBuff);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);

            int[] integerBuff = new int[GAUSS_MEMLIMIT];
            kidsBuff.GetData(kids);
            string ans = "";
            for (int i = 0; i<30; i++) 
                ans += kids[i].ToString() + " ";
            Debug.Log("kids: " +  ans);
            interpTausBuff.GetData(interpTaus);
            ans = "";
            for (int i = 0; i<30; i++) 
                ans += interpTaus[i].ToString() + " ";
            Debug.Log("interpTaus: " +  ans);
            currSet.renderIndicesBuff.GetData(integerBuff);
            ans = "";
            for (int i = 0; i<30; i++) 
                ans += integerBuff[i].ToString() + " ";
            Debug.Log("render_indices: " + ans);
            currSet.parentIndicesBuff.GetData(integerBuff);
            ans = "";
            for (int i = 0; i<30; i++) 
                ans += integerBuff[i].ToString() + " ";
            Debug.Log("parent_indices: " + ans);
            currSet.nodesOfRenderIndicesBuff.GetData(integerBuff);
            ans = "";
            for (int i = 0; i<30; i++) 
                ans += integerBuff[i].ToString() + " ";
            Debug.Log("nodes_of_render_indices: " + ans);
        }

        void InitSortBuffers(int count)
        {
            m_GpuSortDistances?.Dispose();
            m_GpuSortKeys?.Dispose();
            m_SorterArgs.resources.Dispose();

            EnsureSorterAndRegister();

            m_GpuSortDistances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4) { name = "HierarchicalSplatSortDistances" };
            m_GpuSortKeys = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4) { name = "HierarchicalSplatSortIndices" };

            // init keys buffer to splat indices
            m_CSSplatUtilities.SetBuffer((int)KernelIndices.SetIndices, Props.SplatSortKeys, m_GpuSortKeys);
            m_CSSplatUtilities.SetInt(Props.SplatCount, m_GpuSortDistances.count);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.SetIndices, out uint gsX, out _, out _);
            m_CSSplatUtilities.Dispatch((int)KernelIndices.SetIndices, (m_GpuSortDistances.count + (int)gsX - 1)/(int)gsX, 1, 1);

            m_SorterArgs.inputKeys = m_GpuSortDistances;
            m_SorterArgs.inputValues = m_GpuSortKeys;
            m_SorterArgs.count = (uint)count;
            if (m_Sorter.Valid)
                m_SorterArgs.resources = GpuSorting.SupportResources.Load((uint)count);
        }

        public bool resourcesAreSetUp => m_ShaderSplats != null 
            && m_ShaderComposite != null 
            && m_CSSplatUtilities != null 
            && m_CSHierarchicalCut != null
            && SystemInfo.supportsComputeShaders;

        public void EnsureMaterials()
        {
            if (m_MatSplats == null && resourcesAreSetUp)
            {
                m_MatSplats = new Material(m_ShaderSplats) {name = "HierarchicalSplats"};
                m_MatComposite = new Material(m_ShaderComposite) {name = "HierarchicalClearDstAlpha"};
            }
        }

        public void EnsureSorterAndRegister()
        {
            if (m_Sorter == null && resourcesAreSetUp)
            {
                m_Sorter = new GpuSorting(m_CSSplatUtilities);
            }

            if (!m_Registered && resourcesAreSetUp)
            {
                HierarchicalSplatRenderSystem.instance.RegisterSplat(this);
                m_Registered = true;
            }
        }

        public void OnEnable()
        {
            Debug.Log("OnEnable()");
            m_FrameCounter = 0;
            if (!resourcesAreSetUp)
                return;

            EnsureMaterials();
            EnsureSorterAndRegister();
        }

        void SetAssetDataOnCS(CommandBuffer cmb, KernelIndices kernel)
        {
            ComputeShader cs = m_CSSplatUtilities;
            int kernelIndex = (int) kernel;
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatPos, m_GpuPosData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatChunks, m_GpuChunks);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatOther, m_GpuOtherData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatSH, m_GpuSHData);
            cmb.SetComputeTextureParam(cs, kernelIndex, Props.SplatColor, m_GpuColorData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatSelectedBits, m_GpuPosData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatDeletedBits, m_GpuPosData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatViewData, m_GpuView);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.OrderBuffer, m_GpuSortKeys);

            cmb.SetComputeIntParam(cs, Props.SplatBitsValid, 0);
            cmb.SetComputeIntParam(cs, Props.SplatFormat, 0);
            cmb.SetComputeIntParam(cs, Props.SplatCount, currSet.toRender);
            cmb.SetComputeIntParam(cs, Props.SplatChunkCount, 0);

            NativeArray<GaussianCutout.ShaderData> data = new(1, Allocator.Temp);
            m_GpuEditCutouts.SetData(data);
            cmb.SetComputeIntParam(cs, Props.SplatCutoutsCount, 0);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatCutouts, m_GpuEditCutouts);
        }

        internal void SetAssetDataOnMaterial(MaterialPropertyBlock mat)
        {
            mat.SetBuffer(Props.SplatPos, m_GpuPosData);
            mat.SetBuffer(Props.SplatOther, m_GpuOtherData);
            mat.SetBuffer(Props.SplatSH, m_GpuSHData);
            mat.SetTexture(Props.SplatColor, m_GpuColorData);
            mat.SetBuffer(Props.SplatSelectedBits, m_GpuPosData);
            mat.SetBuffer(Props.SplatDeletedBits, m_GpuPosData);
            mat.SetInt(Props.SplatBitsValid, 0);
            mat.SetInteger(Props.SplatFormat, 0);
            mat.SetInteger(Props.SplatCount, currSet.toRender);
            mat.SetInteger(Props.SplatChunkCount, 0);
            
        }

        static void DisposeBuffer(ref GraphicsBuffer buf)
        {
            buf?.Dispose();
            buf = null;
        }

        void DisposeResourcesForAsset()
        {
            DestroyImmediate(m_GpuColorData);

            DisposeBuffer(ref m_GpuPosData);
            DisposeBuffer(ref m_GpuOtherData);
            DisposeBuffer(ref m_GpuSHData);

            DisposeBuffer(ref m_GpuView);
            DisposeBuffer(ref m_GpuIndexBuffer);
            DisposeBuffer(ref m_GpuSortDistances);
            DisposeBuffer(ref m_GpuSortKeys);

            currMem?.Release();
            otherMem?.Release();
            currSet?.Release();
            otherSet?.Release();
            splitsBuff?.Release();
            nodeIndices1Buff?.Release();
            nodesToExpandBuff?.Release();
            interpTausBuff?.Release();
            kidsBuff?.Release();
            NdstIBuff?.Release();
            NsrcIBuff?.Release();
            NsrcCBuff?.Release();
            nodeIndices2Buff?.Release();
            numIBuff?.Release();
            outNBuff?.Release();

            currMem = null;
            otherMem = null;
            currSet = null;
            otherSet = null;
            splitsBuff = null;
            nodeIndices1Buff = null;
            nodesToExpandBuff = null;
            interpTausBuff = null;
            kidsBuff = null;
            NdstIBuff = null;
            NsrcIBuff = null;
            NsrcCBuff = null;
            nodeIndices2Buff = null;
            numIBuff = null;
            outNBuff = null;

            m_SorterArgs.resources.Dispose();

            m_SplatCount = 0;

        }

        public void OnDisable()
        {
            DisposeResourcesForAsset();
            HierarchicalSplatRenderSystem.instance.UnregisterSplat(this);
            m_Registered = false;

            DestroyImmediate(m_MatSplats);
            DestroyImmediate(m_MatComposite);
        }

        internal void CalcViewData(CommandBuffer cmb, Camera cam)
        {
            if (cam.cameraType == CameraType.Preview)
                return;

            // calculate view dependent data for each splat
            SetAssetDataOnCS(cmb, KernelIndices.CalcViewData);

            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixMV, matView * matO2W);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, matO2W);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixWorldToObject, matW2O);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.VecScreenParams, screenPar);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.VecWorldSpaceCameraPos, camPos);
            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.SplatScale, m_SplatScale);
            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.SplatOpacityScale, m_OpacityScale);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.SHOrder, m_SHOrder);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.SHOnly, m_SHOnly ? 1 : 0);

            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.CalcViewData, out uint gsX, out _, out _);
            cmb.DispatchCompute(m_CSSplatUtilities, (int)KernelIndices.CalcViewData, (m_GpuView.count + (int)gsX - 1)/(int)gsX, 1, 1);
        }
        public void SetViewpoint(Camera cam)
        {
            if (cam.cameraType == CameraType.Preview)
                return;
                
            m_FrameCounter++;
            var tr = transform;

            matView = cam.worldToCameraMatrix;
            matO2W = tr.localToWorldMatrix;
            matW2O = tr.worldToLocalMatrix;
            int screenW = cam.pixelWidth, screenH = cam.pixelHeight;
            int eyeW = XRSettings.eyeTextureWidth, eyeH = XRSettings.eyeTextureHeight;
            Vector4 screenPar = new Vector4(eyeW != 0 ? eyeW : screenW, eyeH != 0 ? eyeH : screenH, 0, 0);
            
            
            camPos = cam.transform.position;

            /*camPos = new Vector4(3.27458f, -48.7878f, 3.3452f);
            //Hard Code for Debugging
            m_ZDirection = new Vector3(0.16184f, 0.818862f, -0.550702f);

            if (m_FrameCounter <50) {
                Debug.Log("zdir: " + m_ZDirection);
                Debug.Log("cam_pos: " + cam_pos);
            }*/
        }

        internal void SortPoints(CommandBuffer cmd, Camera cam, Matrix4x4 matrix)
        {
            if (cam.cameraType == CameraType.Preview)
                return;

            Matrix4x4 worldToCamMatrix = cam.worldToCameraMatrix;
            worldToCamMatrix.m20 *= -1;
            worldToCamMatrix.m21 *= -1;
            worldToCamMatrix.m22 *= -1;

            // calculate distance to the camera for each splat
            cmd.BeginSample(s_ProfSort);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatSortDistances, m_GpuSortDistances);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatSortKeys, m_GpuSortKeys);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatChunks, m_GpuChunks);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatPos, m_GpuPosData);
            cmd.SetComputeIntParam(m_CSSplatUtilities, Props.SplatFormat, (int)m_Asset.posFormat);
            cmd.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixMV, worldToCamMatrix * matrix);
            cmd.SetComputeIntParam(m_CSSplatUtilities, Props.SplatCount, m_SplatCount);
            cmd.SetComputeIntParam(m_CSSplatUtilities, Props.SplatChunkCount, m_GpuChunksValid ? m_GpuChunks.count : 0);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.CalcDistances, out uint gsX, out _, out _);
            cmd.DispatchCompute(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, (m_GpuSortDistances.count + (int)gsX - 1)/(int)gsX, 1, 1);

            // sort the splats
            EnsureSorterAndRegister();
            m_Sorter.Dispatch(cmd, m_SorterArgs);
            cmd.EndSample(s_ProfSort);
        }

        public void tau2Limit(Camera cam) 
        {
            float fovy = cam.fieldOfView;  
            float aspect = cam.aspect;

            float fovx = 2.0f * Mathf.Atan(Mathf.Tan(fovy * 0.5f) * aspect);
            float tan_fovx = Mathf.Tan(fovx * 0.5f);
        	if (tau == 0)
		        sizeLimit = 0;
            else
	            sizeLimit = (2.0f * (tau + 0.5f)) * fovx / (0.5f * cam.pixelWidth); //Screen.Width?
            
            // HARD CODING FOR DEBUGGING
            //sizeLimit = 0.0211829f;
        }
        public void Update()
        {
            var curHash = m_Asset ? m_Asset.dataHash : new Hash128();
            if (m_PrevAsset != m_Asset || m_PrevHash != curHash) //Done only the first time.
            {
                m_PrevAsset = m_Asset;
                m_PrevHash = curHash;
                if (resourcesAreSetUp)
                {
                    Debug.Log("Inside Update()");
                    DisposeResourcesForAsset();
                    CreateResourcesForAsset();
                }
                else
                {
                    Debug.LogError($"{nameof(HierarchicalSplatRenderer)} component is not set up correctly (Resource references are missing), or platform does not support compute shaders");
                }
            }
        }

        public void ActivateCamera(int index)
        {
            Camera mainCam = Camera.main;
            if (!mainCam)
                return;
            if (!m_Asset)
                return;

        }

        void ClearGraphicsBuffer(GraphicsBuffer buf)
        {
            m_CSSplatUtilities.SetBuffer((int)KernelIndices.ClearBuffer, Props.DstBuffer, buf);
            m_CSSplatUtilities.SetInt(Props.BufferSize, buf.count);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.ClearBuffer, out uint gsX, out _, out _);
            m_CSSplatUtilities.Dispatch((int)KernelIndices.ClearBuffer, (int)((buf.count+gsX-1)/gsX), 1, 1);
        }

        void UnionGraphicsBuffers(GraphicsBuffer dst, GraphicsBuffer src)
        {
            m_CSSplatUtilities.SetBuffer((int)KernelIndices.OrBuffers, Props.SrcBuffer, src);
            m_CSSplatUtilities.SetBuffer((int)KernelIndices.OrBuffers, Props.DstBuffer, dst);
            m_CSSplatUtilities.SetInt(Props.BufferSize, dst.count);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.OrBuffers, out uint gsX, out _, out _);
            m_CSSplatUtilities.Dispatch((int)KernelIndices.OrBuffers, (int)((dst.count+gsX-1)/gsX), 1, 1);
        }

        static float SortableUintToFloat(uint v)
        {
            uint mask = ((v >> 31) - 1) | 0x80000000u;
            return math.asfloat(v ^ mask);
        }

        void DispatchUtilsAndExecute(CommandBuffer cmb, KernelIndices kernel, int count)
        {
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)kernel, out uint gsX, out _, out _);
            cmb.DispatchCompute(m_CSSplatUtilities, (int)kernel, (int)((count + gsX - 1)/gsX), 1, 1);
            Graphics.ExecuteCommandBuffer(cmb);
        }
    }
}