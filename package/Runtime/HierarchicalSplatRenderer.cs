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
            if (m_CamerCommandBuffersDone != null)
            {
                if (m_CommandBuffer != null)
                {
                    for each (var cam in m_CameraCommandBuffersDone)
                    {
                        if (cam)
                            cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, m_CommandBuffer);
                    }
                }
            }
            m_CommandBuffer?.Dispose();
            m_CommandBUffer = null;
            Camera.onPreCull -= OnPreCullCamera;
        }

        public Material SortAndRenderSplats(Camera cam, CommandBuffer cmb)
        {
            Material matComposite = null;
            hs.EnsureMaterials();
            matComposite = hs.m_MatComposite;
            var mpb = mat;

            // sort
            var matrix = hs.transform.localToWorldMatrix;
            if (frameCounter == 1 || frameCounter %2 == 0)
                hs.SortPoints(cmb, cam, matrix); 

            // cache view
            mat.Clear();
            Material displayMat = hs.m_MatSplats;

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

                int num_get_children = curr_res.Item1;
                if (curr_res.Item3 == hs.otherMem)
                    (hs.otherMem, hs.currMem) = (hs.currMem, hs.otherMem);

                if (num_get_children != 0)
                    hs.DispatchSetStarts(1024, num_get_children);
                
                hs.tau2Limit(cam);
                curr_res = hs.CreateHierarchicalCut(cleanup);
                cleanup = false;
            }

            hs.tau2Limit(cam);
            hs.DispatchComputeTsIndexed(1024);

            hs.CreateRenderBuffers();

            // add sorting, view calc and drawing commands for each splat object
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

        public int m_RenderMode = 0;
        [Range(1.0f,15.0f)] public float m_PointDisplaySize = 3.0f;
        [SerializeField] public float tau = 9.0f;
        public float sizeLimit = 0.03f;
        public Shader m_ShaderSplats;
        public Shader m_ShaderComposite;
        [Tooltip("Gaussian splatting compute shader")]
        public ComputeShader m_CSSplatUtilities;
        [Tooltip("Hierarchy Cut selection compute shader")]
        public ComputeShader m_CSHierarchicalCut;
        
        public uint[] CopyPos;
        public uint[] CopyOther;
        public Vector4[] CopyColor;
        public uint[] CopySHs;
        public Box[] CopyBoxes;
        public Node[] CopyNodes;

        int m_SplatCount;
        int GAUSS_MEMLIMIT;
        int ALLGAUSS;
        int num_need_children;
        public int num_active_nodes_gpu;
        int nodes_offset = 0;
        int global_node_count = 0;
        int gaussians_offset = 0;
        int skyboxoffset;
        Vector4 camPos;
        Vector4 camPosOld;

        Matrix4x4 matView;
        Matrix4x4 matO2W;
        Matrix4x4 matW2O;
        Vector4 screenPar

        public LightSet currSet;
        public LightSet otherSet;
        public MemSet currMem;
        public MemSet otherMem;

        bool ran_out = false;

        int[] cuda2cpu;
        int[] package_parent_starts; //package_parent_starts
        int[] need_children;
        int[] splits1;
        int[] splits2;
        int[] node_indices1; //active_nodes1
        int[] node_indices2;
        float[] interp_taus;
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
        Texture m_GpuColorData;
        internal GraphicsBuffer m_GpuView;
        internal GraphicsBuffer m_GpuIndexBuffer;

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

        //public bool HasValidRenderSetup => m_GpuPosData != null && m_GpuOtherData != null;
        const int kGpuViewDataSize = 40;

        void CreateResourcesForAsset()
        {
            if (!HasValidAsset)
                return;

            
            m_SplatCount = asset.splatCount;

            long budget = 16000L;
            GAUSS_MEMLIMIT = (int)((budget * 1000000L - (484L * asset.scaffoldCount + 168L)) / 681L);
            if (GAUSS_MEMLIMIT < 0)
            {
                Debug.LogError("Memory budget insufficient");
            }
            GAUSS_MEMLIMIT = asset.splatCount < GAUSS_MEMLIMIT ? asset.splatCount : GAUSS_MEMLIMIT;
            Debug.Log("GAUSS_MEMLIMIT" + GAUSS_MEMLIMIT.ToString());

            skyboxoffset = asset.scaffoldCount;

            ALLGAUSS = (GAUSS_MEMLIMIT + asset.scaffoldCount);
            Debug.Log("ALLGAUSS" + ALLGAUSS.ToString());

            currSet = new LightSet(GAUSS_MEMLIMIT);
            otherSet = new LightSet(GAUSS_MEMLIMIT);

            currMem = new MemSet(ALLGAUSS, GAUSS_MEMLIMIT, asset.padded);
            currMem.posBuff.SetData(asset.AllPos, 0, 0, asset.AllPos.Length);
            currMem.otherBuff.SetData(asset.AllOther, 0, 0, asset.AllOther.Length);
            currMem.colorBuff.SetData(asset.AllColor, 0, 0, asset.AllColor.Length);
            currMem.shsBuff.SetData(asset.AllSHs, 0, 0, asset.AllSHs.Length);
            otherMem = new MemSet(ALLGAUSS, GAUSS_MEMLIMIT, asset,padded);
            otherMem.posBuff.SetData(asset.AllPos, 0, 0, asset.AllPos.Length);
            otherMem.otherBuff.SetData(asset.AllOther, 0, 0, asset.AllOther.Length);
            otherMem.colorBuff.SetData(asset.AllColor, 0, 0, asset.AllColor.Length);
            otherMem.shsBuff.SetData(asset.AllSHs, 0, 0, asset.AllSHs.Length);

            CopyPos = new uint[GAUSS_MEMLIMIT * 3];
            CopyOther = new uint[GAUSS_MEMLIMIT * 3];
            CopyColor = new Vector4[GAUSS_MEMLIMIT];
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
            package_parent_starts = new int[GAUSS_MEMLIMIT];
            need_children = new int[GAUSS_MEMLIMIT];
            splits1 = new int [GAUSS_MEMLIMIT];
            splits2 = new int [GAUSS_MEMLIMIT];
            node_indices1 = new int [GAUSS_MEMLIMIT];
            node_indices2 = new int [GAUSS_MEMLIMIT];
            interp_taus = new float[GAUSS_MEMLIMIT];
            kids = new int[GAUSS_MEMLIMIT];

            node_indices1[0] = 0;
            num_active_nodes_gpu = 1;

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
            
            m_GpuPosData = currMem.posBuff; // I might need to make copies of these buffers instead.
            m_GpuOtherData = currMem.otherBuff;
            m_GpuSHData = currMem.shsBuff;
            
            int toRender = currSet.toRender + asset.scaffoldCount;
            var (texWidth, texHeight) = HierarchicalSplatAsset.CalcTextureSize(toRender);
            var texFormat = GraphicsFormat.R32G32B32A32_SFloat;
            var tex = new Texture2D(texWidth, texHeight, texFormat, TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate)   { name = "HierarchicalColorData" };

            float4[] colorArr = new float4[toRender];
            currMem.colorBuff.GetData(colorArr, 0);
            tex.SetPixelData(colorArr, 0);
            tex.Apply(false, true);
            m_GpuColorData = tex;

            m_GpuView = new GraphicsBuffer(GraphicsBuffer.Target.Structured, toRender, kGpuViewDataSize);

            InitSortBuffers(toRender);
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

        void InitGraphicsBuffers()
        {
            Debug.Log("Init Graphics Buffers");
            Print(m_Asset.Pos, m_Asset.Rots, m_Asset.Scales, m_Asset.SHs, m_Asset.Alphas, m_Asset.Nodes, m_Asset.Boxes);

            splitsBuff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            splitsBuff.SetData(splits1);
            nodeIndices1Buff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            nodeIndices1Buff.SetData(node_indices1);
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
            nodeIndices2Buff.SetData(node_indices2);
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

            m_CSHierarchicalCut.SetBuffer(5, "interp_taus", interpTausBuff);
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

            m_CSHierarchicalCut.SetInt("N", num_active_nodes_gpu);
        }

        bool AddNodePackage(int[] n_indices, int[] p_indices, MemSet useMem)
        {
            int node_copy_count = n_indices.Length;
            int gaussian_copy_count = 0;
            foreach (int id in n_indices) 
            {
                Node node = asset.Nodes[id];
                gaussian_copy_count  += node.count_leafs + node.count_merged;
            }

            if (node_copy_count + nodes_offset > GAUSS_MEMLIMIT ||
                gaussian_copy_count + gaussians_offset > GAUSS_MEMLIMIT)
            {
                if (tau == 0)
                    tau = 1.0f;
                tau *= 1.05f;
                return false;
            }
            
            int copied_gaussians = 0;
            for (int i = 0; i < n_indices.Length; i++)
            {
                int id = n_indices[i];
                int parent = p_indices[i];
                Node node = asset.Nodes[id];

                int count = node.count_leafs + node.count_merged;
                for (int j = 0; j < count; j++)
                {
                    int src = node.start + j;
                    int dst = copied_gaussians + j;



                    int src = node.start + j;
                    int dst = copied_gaussians + j;

                    // Pos: 3 uints per splat
                    CopyPos[dst * 3 + 0] = asset.posData[src * 3 + 0];
                    CopyPos[dst * 3 + 1] = asset.posData[src * 3 + 1];
                    CopyPos[dst * 3 + 2] = asset.posData[src * 3 + 2];

                    // Other: 4 uints per splat
                    CopyOther[dst * 4 + 0] = asset.otherData[src * 4 + 0];
                    CopyOther[dst * 4 + 1] = asset.otherData[src * 4 + 1];
                    CopyOther[dst * 4 + 2] = asset.otherData[src * 4 + 2];
                    CopyOther[dst * 4 + 3] = asset.otherData[src * 4 + 3];

                    // Color: 1 Vector4 per splat
                    CopyColor[dst] = asset.colorData[src];

                    // SH: 48 uints per splat
                    for (int k = 0; k < 48; k++)
                        CopySHs[dst * 48 + k] = asset.shData[src * 48 + k];
                }
                node.start_children = -1;
                node.start = gaussians_offset + copied_gaussians;
                node.parent = parent;

                CopyNodes[i] = node;
                CopyBoxes[i] = asset.Boxes[id];

                cuda2cpu[nodes_offset + i] = id;

                copied_gaussians += count;
            }

            int totalOffset = gaussians_offset + asset.scaffoldCount;
            //Insert Set Data Statements Here
            useMem.posBuff.SetData(CopyPos, 0, totalOffset * 3, gaussian_copy_count * 3);
            useMem.otherBuff.SetData(CopyOther, 0, totalOffset * 4, gaussian_copy_count * 4);
            useMem.colorBuff.SetData(CopyColor, 0, totalOffset, gaussian_copy_count);
            useMem.shBuff.SetData(CopySHs, 0, totalOffset * 48, gaussian_copy_count * 48);
            useMem.nodesBuff.SetData(CopyNodes, 0, nodes_offset, node_copy_count);
            useMem.boxesBuff.SetData(CopyBoxes, 0, nodes_offset, node_copy_count);

            gaussians_offset += gaussian_copy_count;
            nodes_offset += node_copy_count;

            global_node_count = node_copy_count;
            Debug.Log("gaussian_copy_count: " + gaussian_copy_count);
            Debug.Log("node_copy_count: " + node_copy_count);
            Debug.Log("gaussians_offset (cuda_gaussians_offset): " + gaussians_offset);
            Debug.Log("nodes_offset (cuda_nodes_offset): " + nodes_offset);
            string ans = "";
            for (int i = 0; i< 30; i++)
            {
                ans += cuda2cpu[i].ToString() + " ";
            }
            Debug.Log("Cuda2Cpu: " + ans);
            return true;

        }

        int createNodePackage(out int[] n_indices, out int[] p_indices)   //this is using the direct asset.Nodes --> no memSet at all
        {

            nodesToExpandBuff.GetData(need_children, 0, 0, num_need_children);
            
            string ans = "";
            for (int i = 0; i< 10; i++)
            {
                ans += need_children[i].ToString();
            }
            Debug.Log("need_children (nodes_to_expand_cuda, need_children): " + ans);
            int num_get_children = 0;
            int node_package_count = 0;
            for (int i = 0; i< num_need_children; i++)
            {
                int id = need_children[i];
                int node_id = cuda2cpu[id];
                node_package_count += asset.Nodes[node_id].count_children;

                num_get_children++;
            }

            n_indices = new int[node_package_count];
            p_indices = new int[node_package_count];

            int nodes_expanded = 0;
            ans = "";
            ans += "num_get_children: ";
            ans += num_get_children.ToString() + "\n";
            Debug.Log(ans);
            for (int k = 0; k < num_get_children; k++)
            {
                int id = need_children[k];
                int node_id = cuda2cpu[id];
                Node node = asset.Nodes[node_id];
                for (int i = 0; i < node.count_children; i++)
                {
                    n_indices[nodes_expanded + i] = node.start_children + i;
                    p_indices[nodes_expanded + i] = id;
                }
                package_parent_starts[k] = nodes_offset + nodes_expanded;
                nodes_expanded += node.count_children;
            }

            return num_get_children;
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
            Node[] h_nodes = new Node[global_node_count];

            // Using GraphicsBuffer.GetData to retrieve data from GPU buffers
            nodeIndices1Buff.GetData(h_active1, 0, 0, num_active_nodes_gpu);
            nodeIndices2Buff.GetData(h_active2, 0, 0, num_active_nodes_gpu);
            currMem.nodesBuff.GetData(h_nodes, 0, nodes_offset, global_node_count);
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
            Debug.Log("num_active_nodes_gpu: " + num_active_nodes_gpu);

            for (int i = 0; i < Mathf.Min(20, num_active_nodes_gpu); i++)
            {
                ans += h_active1[i] + " ";
            }
            Debug.Log("activenodes1_cuda: " + ans);

            ans = "";
            for (int i = 0; i < Mathf.Min(20, num_active_nodes_gpu); i++)
            {
                ans += h_active2[i] + " ";
            }
            Debug.Log("activenodes2_cuda: " + ans);

            ans = "";
            for (int i = 0; i < Mathf.Min(5, global_node_count); i++)
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

            Debug.Log("num_need_children: " + num_need_children);
        }

        // AsyncTask
        public (int, int, MemSet) CreateHierarchicalCut(bool cleanup)
        {

            MemSet useMem = currMem;

            printSizeStep();

            if (!changeToSizeStep(useMem))
                Debug.LogError("Doing a step didn't work");

            (nodeIndices1Buff, nodeIndices2Buff) = (nodeIndices2Buff, nodeIndices1Buff);

            int num_get_children = 0;
            int num_transferred = 0;
            Debug.Log("num_need_children: " + num_need_children);
            if (!cleanup && num_need_children > 0)
            {
                if (!ran_out)
                {
                    int[] package_indices;
                    int[] package_parent_indices;

                    int num_new_parents = createNodePackage(out package_indices, out package_parent_indices);

                    string ans = "";
                    for (int i = 0; i < 10; i++)
                    {
                        ans += package_parent_starts[i].ToString();
                    }
                    Debug.Log("package_parent_starts (package_parent_cuda_starts): " + ans);

                    Debug.Log("num_new_parents: " + num_new_parents.ToString());
                    ans = "";
                    for (int i = 0; i < Mathf.Min(10, package_indices.Length); i++)
                    {
                        ans += package_indices[i].ToString();
                    }
                    Debug.Log("package_indices: " + ans);

                    ans = "";
                    for (int i = 0; i < Mathf.Min(10, package_parent_indices.Length); i++)
                    {
                        ans += package_parent_indices[i].ToString();
                    }
                    Debug.Log("package_parent_indices: " + ans);

                    if (AddNodePackage(package_indices, package_parent_indices, useMem))
                    {
                        NsrcIBuff.SetData(package_parent_starts);
                        num_transferred = package_indices.Length;

                        num_get_children = num_new_parents;
                    }
                    else
                    {
                        ran_out = true;
                    }
                }
            }

            /*
            
            Cleanup Commands

            */


            Debug.Log("num_transferred: " + num_transferred.ToString());
            return (num_get_children, num_transferred, useMem);
        }

        bool changeToSizeStep(MemSet useMem)
        {
            /*Node[] node_0 = new Node[1]; 
            useMem.nodesBuff.GetData(node_0, 0, 0, 1);
            Debug.Log("node_0 depth: " + node_0[0].depth.ToString());
            Debug.Log("node_0 parent: " + node_0[0].parent.ToString());
            Debug.Log("node_0 start: " + node_0[0].start.ToString());
            Debug.Log("node_0 count_leafs: " + node_0[0].count_leafs.ToString());
            Debug.Log("node_0 count_merged: " + node_0[0].count_merged.ToString());
            Debug.Log("node_0 start children: " + node_0[0].start_children.ToString());
            Debug.Log("node_0 count children: " + node_0[0].count_children.ToString());*/

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

            m_CSHierarchicalCut.SetVector("viewpoint", camPos);
            m_CSHierarchicalCut.SetFloat("target_size", sizeLimit);

            int num_node_blocks = (num_active_nodes_gpu + 255) / 256;

            DispatchChangeNodesShader(num_node_blocks);
            DispatchFlaggedShader(1024, num_active_nodes_gpu);
            DispatchInclusiveSumShader(1024, num_active_nodes_gpu);

            int og_num_active_nodes = num_active_nodes_gpu;

            int [] buffer = new int[1];
            numIBuff.GetData(buffer);
            num_need_children = buffer[0];
            //Debug.Log("num_need_children (need_expansion): " + num_need_children.ToString());
            NdstIBuff.GetData(buffer, 0, num_active_nodes_gpu - 1, 1);
            num_active_nodes_gpu = buffer[0];
            //Debug.Log("new_node_count (new_N): " + new_node_count.ToString());

            if (num_active_nodes_gpu > GAUSS_MEMLIMIT)
                return false;

            DispatchPutNodesShader(num_node_blocks, og_num_active_nodes);

            int num_render_blocks = (num_active_nodes_gpu + 255) / 256;

            DispatchRenderIndicesIndexed(num_render_blocks, num_active_nodes_gpu);
            DispatchInclusiveSumShader(1024, num_active_nodes_gpu);
            DispatchPutRenderIndicesIndexed(num_render_blocks);

            NdstIBuff.GetData(buffer, 0, num_active_nodes_gpu - 1, 1);
            otherSet.toRender = buffer[0];
            //Debug.Log("to_render_num: (new_R)" + to_render_num.ToString());
            return true;
        }

        public void DispatchChangeNodesShader(int threads) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("changeNodesOnce");
            m_CSHierarchicalCut.SetInt("N", num_active_nodes_gpu);
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
            m_CSHierarchicalCut.SetInt("N", num_active_nodes_gpu);
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

        public void DispatchSetStarts(int threads, int num_get_children) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("setStarts");
            m_CSHierarchicalCut.SetBuffer(kernel, "nodes", currMem.nodesBuff);
            m_CSHierarchicalCut.SetBuffer(kernel, "nodes_to_expand", nodesToExpandBuff);
            m_CSHierarchicalCut.SetInt("N", num_get_children);
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

            /*int[] integerBuff = new int[GAUSS_MEMLIMIT];
            kidsBuff.GetData(kids);
            string ans = "";
            for (int i = 0; i<30; i++) 
                ans += kids[i].ToString() + " ";
            Debug.Log("kids: " +  ans);
            interpTausBuff.GetData(interp_taus);
            ans = "";
            for (int i = 0; i<30; i++) 
                ans += interp_taus[i].ToString() + " ";
            Debug.Log("interp_taus: " +  ans);
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
            Debug.Log("nodes_of_render_indices: " + ans);*/
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
                Debug.Log("Registering");
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
            //CreateResourcesForAsset();
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
            screenPar = new Vector4(eyeW != 0 ? eyeW : screenW, eyeH != 0 ? eyeH : screenH, 0, 0);
            
            camPos = cam.transform.position;
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
            cmd.SetComputeIntParam(m_CSSplatUtilities, Props.SplatFormat, 0);
            cmd.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixMV, worldToCamMatrix * matrix);
            cmd.SetComputeIntParam(m_CSSplatUtilities, Props.SplatCount, currSet.toRender);
            cmd.SetComputeIntParam(m_CSSplatUtilities, Props.SplatChunkCount, 0);
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
            sizeLimit = 0.0211829f;
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

        public void UpdateEditCountsAndBounds()
        {
            if (m_GpuEditSelected == null)
            {
                editSelectedSplats = 0;
                editDeletedSplats = 0;
                editCutSplats = 0;
                editModified = false;
                editSelectedBounds = default;
                return;
            }

            m_CSSplatUtilities.SetBuffer((int)KernelIndices.InitEditData, Props.DstBuffer, m_GpuEditCountsBounds);
            m_CSSplatUtilities.Dispatch((int)KernelIndices.InitEditData, 1, 1, 1);

            using CommandBuffer cmb = new CommandBuffer();
            SetAssetDataOnCS(cmb, KernelIndices.UpdateEditData);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.UpdateEditData, Props.DstBuffer, m_GpuEditCountsBounds);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.BufferSize, m_GpuEditSelected.count);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.UpdateEditData, out uint gsX, out _, out _);
            cmb.DispatchCompute(m_CSSplatUtilities, (int)KernelIndices.UpdateEditData, (int)((m_GpuEditSelected.count+gsX-1)/gsX), 1, 1);
            Graphics.ExecuteCommandBuffer(cmb);

            uint[] res = new uint[m_GpuEditCountsBounds.count];
            m_GpuEditCountsBounds.GetData(res);
            editSelectedSplats = res[0];
            editDeletedSplats = res[1];
            editCutSplats = res[2];
            Vector3 min = new Vector3(SortableUintToFloat(res[3]), SortableUintToFloat(res[4]), SortableUintToFloat(res[5]));
            Vector3 max = new Vector3(SortableUintToFloat(res[6]), SortableUintToFloat(res[7]), SortableUintToFloat(res[8]));
            Bounds bounds = default;
            bounds.SetMinMax(min, max);
            if (bounds.extents.sqrMagnitude < 0.01)
                bounds.extents = new Vector3(0.1f,0.1f,0.1f);
            editSelectedBounds = bounds;
        }

        void UpdateCutoutsBuffer()
        {
            m_GpuEditCutouts
            int bufferSize = m_Cutouts?.Length ?? 0;
            if (bufferSize == 0)
                bufferSize = 1;
            if (m_GpuEditCutouts == null || m_GpuEditCutouts.count != bufferSize)
            {
                m_GpuEditCutouts?.Dispose();
                m_GpuEditCutouts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bufferSize, UnsafeUtility.SizeOf<GaussianCutout.ShaderData>()) { name = "GaussianCutouts" };
            }

            NativeArray<GaussianCutout.ShaderData> data = new(1, Allocator.Temp);
            if (m_Cutouts != null)
            {
                var matrix = transform.localToWorldMatrix;
                for (var i = 0; i < m_Cutouts.Length; ++i)
                {
                    data[i] = GaussianCutout.GetShaderData(m_Cutouts[i], matrix);
                }
            }

            m_GpuEditCutouts.SetData(data);
            data.Dispose();
        }

        bool EnsureEditingBuffers()
        {
            if (!HasValidAsset || !HasValidRenderSetup)
                return false;

            if (m_GpuEditSelected == null)
            {
                var target = GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource |
                             GraphicsBuffer.Target.CopyDestination;
                var size = (m_SplatCount + 31) / 32;
                m_GpuEditSelected = new GraphicsBuffer(target, size, 4) {name = "HierarchicalSplatSelected"};
                m_GpuEditSelectedMouseDown = new GraphicsBuffer(target, size, 4) {name = "HierarchicalSplatSelectedInit"};
                m_GpuEditDeleted = new GraphicsBuffer(target, size, 4) {name = "HierarchicalSplatDeleted"};
                m_GpuEditCountsBounds = new GraphicsBuffer(target, 3 + 6, 4) {name = "HierarchicalSplatEditData"}; // selected count, deleted bound, cut count, float3 min, float3 max
                ClearGraphicsBuffer(m_GpuEditSelected);
                ClearGraphicsBuffer(m_GpuEditSelectedMouseDown);
                ClearGraphicsBuffer(m_GpuEditDeleted);
            }
            return m_GpuEditSelected != null;
        }

        public void EditStoreSelectionMouseDown()
        {
            if (!EnsureEditingBuffers()) return;
            Graphics.CopyBuffer(m_GpuEditSelected, m_GpuEditSelectedMouseDown);
        }

        public void EditStorePosMouseDown()
        {
            if (m_GpuEditPosMouseDown == null)
            {
                m_GpuEditPosMouseDown = new GraphicsBuffer(m_GpuPosData.target | GraphicsBuffer.Target.CopyDestination, m_GpuPosData.count, m_GpuPosData.stride) {name = "HierarchicalSplatEditPosMouseDown"};
            }
            Graphics.CopyBuffer(m_GpuPosData, m_GpuEditPosMouseDown);
        }
        public void EditStoreOtherMouseDown()
        {
            if (m_GpuEditOtherMouseDown == null)
            {
                m_GpuEditOtherMouseDown = new GraphicsBuffer(m_GpuOtherData.target | GraphicsBuffer.Target.CopyDestination, m_GpuOtherData.count, m_GpuOtherData.stride) {name = "HierarchicalSplatEditOtherMouseDown"};
            }
            Graphics.CopyBuffer(m_GpuOtherData, m_GpuEditOtherMouseDown);
        }

        public void EditTranslateSelection(Vector3 localSpacePosDelta)
        {
            if (!EnsureEditingBuffers()) return;

            using var cmb = new CommandBuffer { name = "SplatTranslateSelection" };
            SetAssetDataOnCS(cmb, KernelIndices.TranslateSelection);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionDelta, localSpacePosDelta);

            DispatchUtilsAndExecute(cmb, KernelIndices.TranslateSelection, m_SplatCount);
            UpdateEditCountsAndBounds();
            editModified = true;
        }

        public void EditRotateSelection(Vector3 localSpaceCenter, Matrix4x4 localToWorld, Matrix4x4 worldToLocal, Quaternion rotation)
        {
            if (!EnsureEditingBuffers()) return;
            if (m_GpuEditPosMouseDown == null || m_GpuEditOtherMouseDown == null) return; // should have captured initial state

            using var cmb = new CommandBuffer { name = "SplatRotateSelection" };
            SetAssetDataOnCS(cmb, KernelIndices.RotateSelection);

            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.RotateSelection, Props.SplatPosMouseDown, m_GpuEditPosMouseDown);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.RotateSelection, Props.SplatOtherMouseDown, m_GpuEditOtherMouseDown);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionCenter, localSpaceCenter);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, localToWorld);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixWorldToObject, worldToLocal);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionDeltaRot, new Vector4(rotation.x, rotation.y, rotation.z, rotation.w));

            DispatchUtilsAndExecute(cmb, KernelIndices.RotateSelection, m_SplatCount);
            UpdateEditCountsAndBounds();
            editModified = true;
        }


        public void EditScaleSelection(Vector3 localSpaceCenter, Matrix4x4 localToWorld, Matrix4x4 worldToLocal, Vector3 scale)
        {
            if (!EnsureEditingBuffers()) return;
            if (m_GpuEditPosMouseDown == null) return; // should have captured initial state

            using var cmb = new CommandBuffer { name = "SplatScaleSelection" };
            SetAssetDataOnCS(cmb, KernelIndices.ScaleSelection);

            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.ScaleSelection, Props.SplatPosMouseDown, m_GpuEditPosMouseDown);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionCenter, localSpaceCenter);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, localToWorld);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixWorldToObject, worldToLocal);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionDelta, scale);

            DispatchUtilsAndExecute(cmb, KernelIndices.ScaleSelection, m_SplatCount);
            UpdateEditCountsAndBounds();
            editModified = true;
        }

        void DispatchUtilsAndExecute(CommandBuffer cmb, KernelIndices kernel, int count)
        {
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)kernel, out uint gsX, out _, out _);
            cmb.DispatchCompute(m_CSSplatUtilities, (int)kernel, (int)((count + gsX - 1)/gsX), 1, 1);
            Graphics.ExecuteCommandBuffer(cmb);
        }

        public GraphicsBuffer GpuEditDeleted => m_GpuEditDeleted;
    }
}