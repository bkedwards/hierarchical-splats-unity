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

        public static HierarchicalSplatRenderSystem instance => ms_Instance ??= new HierarchicalSplatRenderSystem();
        static HierarchicalSplatRenderSystem ms_Instance;

        HierarchicalSplatRenderer hs;
        MaterialPropertyBlock mat;
        int frameCounter;
        bool cleanup;
        (int, int, MemSet*) curr_res;

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
            Camera.onPreCull -= OnPreCullCamera;
        }

        // ReSharper disable once MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        /*public bool GatherSplatsForCamera(Camera cam)
        {
            if (cam.cameraType == CameraType.Preview)
                return false;
            // gather all active & valid splat objects
            m_ActiveSplats.Clear();
            foreach (var kvp in m_Splats)
            {
                var hs = kvp.Key;
                if (hs == null || !hs.isActiveAndEnabled || !hs.HasValidAsset)// || !hs.HasValidRenderSetup)
                    continue;
                m_ActiveSplats.Add((kvp.Key, kvp.Value));
            }
            if (m_ActiveSplats.Count == 0)
                return false;

            // sort them by order and depth from camera
            var camTr = cam.transform;
            m_ActiveSplats.Sort((a, b) =>
            {
                var orderA = a.Item1.m_RenderOrder;
                var orderB = b.Item1.m_RenderOrder;
                if (orderA != orderB)
                    return orderB.CompareTo(orderA);
                var trA = a.Item1.transform;
                var trB = b.Item1.transform;
                var posA = camTr.InverseTransformPoint(trA.position);
                var posB = camTr.InverseTransformPoint(trB.position);
                return posA.z.CompareTo(posB.z);
            });
            return true;
        }*/

        // ReSharper disable once MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        /*public Material SortAndRenderSplats(Camera cam, CommandBuffer cmb)
        {
            Material matComposite = null;
            foreach (var kvp in m_ActiveSplats)
            {
                var hs = kvp.Item1;
                hs.EnsureMaterials();
                matComposite = hs.m_MatComposite;
                var mpb = kvp.Item2;

                // sort
                var matrix = hs.transform.localToWorldMatrix;
                if (hs.m_FrameCounter % hs.m_SortNthFrame == 0)
                    hs.SortPoints(cmb, cam, matrix);
                ++hs.m_FrameCounter;

                // cache view
                kvp.Item2.Clear();
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
            }
            return matComposite;
        }*/

        // ReSharper disable once MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        // ReSharper disable once UnusedMethodReturnValue.Global - used by HDRP/URP features that are not always compiled
        /*public CommandBuffer InitialClearCmdBuffer(Camera cam)
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
        }*/

        /*void OnPreCullCamera(Camera cam)
        {
            if (!GatherSplatsForCamera(cam))
                return;

            InitialClearCmdBuffer(cam);

            m_CommandBuffer.GetTemporaryRT(HierarchicalSplatRenderer.Props.GaussianSplatRT, -1, -1, 0, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat);
            m_CommandBuffer.SetRenderTarget(HierarchicalSplatRenderer.Props.GaussianSplatRT, BuiltinRenderTextureType.CurrentActive);
            m_CommandBuffer.ClearRenderTarget(RTClearFlags.Color, new Color(0, 0, 0, 0), 0, 0);

            // We only need this to determine whether we're rendering into backbuffer or not. However, detection this
            // way only works in BiRP so only do it here.
            m_CommandBuffer.SetGlobalTexture(HierarchicalSplatRenderer.Props.CameraTargetTexture, BuiltinRenderTextureType.CameraTarget);

            // add sorting, view calc and drawing commands for each splat object
            Material matComposite = SortAndRenderSplats(cam, m_CommandBuffer);

            // compose
            m_CommandBuffer.BeginSample(s_ProfCompose);
            m_CommandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            m_CommandBuffer.DrawProcedural(Matrix4x4.identity, matComposite, 0, MeshTopology.Triangles, 3, 1);
            m_CommandBuffer.EndSample(s_ProfCompose);
            m_CommandBuffer.ReleaseTemporaryRT(HierarchicalSplatRenderer.Props.GaussianSplatRT);
        }*/
        void OnPreCullCamera(Camera cam)
        {
            if (!hs.resourcesAreSetUp || !hs.HasValidAsset)
                return;

            frameCounter++;   
            Debug.Log("frame " + frameCounter);

            hs.CalcViewData(cam);

            cleanup |= frameCounter % 10 == 0;
            if (frameCounter == 1 || frameCounter % 2 == 0)
            {

                if (frameCounter == 1)
                   curr_res = hs.CreateHierarchicalCut(false);

                if (frameCounter < 10)
                    Debug.Log("num_get_children: " + curr_res.Item1.ToString());

                (hs.currSet, hs.otherSet) = (hs.otherSet, hs.currSet);
                if (res.Item1 == hs.otherMem)
                    (hs.otherMem, hs.currMem) = (hs.currMem, hs.otherMem);

                if (curr_res.Item1 != 0)
                    hs.DispatchSetStarts(1024, res.Item1);
                curr_res = hs.CreateHierarchicalCut(cleanup);
                cleanup = false;
            }
            hs.tau2Limit(cam);

            if (frameCounter < 50)
            {
                Debug.Log("tau" + hs.tau);
                Debug.Log("num_active_nodes: " + hs.num_active_nodes);
                Debug.Log("sizeLimit: " + hs.sizeLimit);
                Debug.Log("to_render: " + hs.to_render_num);
                Debug.Log("skyboxnum: " + 100000);

            }
            hs.DispatchComputeTsIndexed(1024);
            //hs.forward();*/
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
        public float sizeLimit = 0.03f;
        public Shader m_ShaderSplats;
        public Shader m_ShaderComposite;
        [Tooltip("Gaussian splatting compute shader")]
        public ComputeShader m_CSSplatUtilities;
        [Tooltip("Cut selection compute shader")]
        public ComputeShader m_CSHierarchicalCut;

        int m_SplatCount;
        int GAUSS_MEMLIMIT;
        int ALLGAUSS;
        int new_node_count;
        public int to_render_num;
        int num_need_children;
        public int num_active_nodes; //num_active_nodes_cpu
        int num_active_nodes_gpu;
        int nodes_offset = 0;
        int gaussians_offset = 0;
        int skyboxoffset;
        Vector4 cam_pos;
        Vector4 cam_pos_old;
        Vector3 m_ZDirection;

        public MemSet [] mems = new MemSet[2];

        public LightSet* currSet;
        public LightSet* otherSet;
        public MemSet* currMem;
        public MemSet* otherMem;

        bool ran_out = false;
        LightSet [] lights = new LightSet[2];

        int[] cuda2cpu;
        int[] package_parent_starts; //package_parent_starts
        int[] need_children;
        int[] render_indices;
        int[] parent_indices;
        int[] nodes_of_render_indices;
        int[] splits1;
        int[] splits2;
        int[] node_indices1; //active_nodes1
        int[] node_indices2;
        float[] interp_taus;
        int[] kids;

        GraphicsBuffer nodesBuff;
        GraphicsBuffer boxesBuff;
        GraphicsBuffer renderIndicesBuff;
        GraphicsBuffer parentIndicesBuff;
        GraphicsBuffer nodesOfRenderIndicesBuff;
        GraphicsBuffer splitsBuff;
        GraphicsBuffer nodeIndicesBuff;
        GraphicsBuffer nodesToExpandBuff;
        GraphicsBuffer interpTausBuff;
        GraphicsBuffer kidsBuff;
        GraphicsBuffer NdstIBuff;
        GraphicsBuffer NsrcIBuff;
        GraphicsBuffer NsrcCBuff;
        GraphicsBuffer newNodeIndicesBuff;
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
            m_Asset.Pos != null &&
            m_Asset.Scales != null &&
            m_Asset.Rots != null &&
            m_Asset.Alphas != null &&
            m_Asset.SHs != null &&
            m_Asset.Nodes != null &&
            m_Asset.Boxes != null;

        //public bool HasValidRenderSetup => m_GpuPosData != null && m_GpuOtherData != null;
        const int kGpuViewDataSize = 40;

        void CreateResourcesForAsset()
        {

            //Print(m_Asset.SkyPos, m_Asset.SkyRot, m_Asset.SkyScale, m_Asset.SkySH, m_Asset.SkyAlpha, default, default);

            
            Debug.Log("CreateResourcesForAsset");
            if (!HasValidAsset)
                Debug.Log("Something wrong: " + HasValidAsset);
                Debug.Log("asset: " + (m_Asset != null));
                Debug.Log("Pos: " + (m_Asset.Pos != null));
                Debug.Log("scales: " + (m_Asset.Scales != null));
                Debug.Log("rots: " + (m_Asset.Rots != null));
                Debug.Log("alphas: " + (m_Asset.Alphas != null));
                Debug.Log("shs: " + (m_Asset.SHs != null));
                Debug.Log("nodes: " + (m_Asset.Nodes != null));
                Debug.Log("boxes: " + (m_Asset.Boxes != null));

            //Calculate GAUSS_MEMLIMIT and ALLGAUSS
            long budget = 16000L;
            GAUSS_MEMLIMIT = (int)((budget * 1000000L - (484L * asset.scaffoldCount + 168L)) / 681L);
            if (GAUSS_MEMLIMIT < 0)
            {
                Debug.LogError("Memory budget insufficient");
            }
            GAUSS_MEMLIMIT = asset.splatCount < GAUSS_MEMLIMIT ? asset.splatCount : GAUSS_MEMLIMIT;
            Debug.Log("GAUSS_MEMLIMIT" + GAUSS_MEMLIMIT.ToString());

            //GAUSS_MEMLIMIT = 10,387,668
            //skyboxoffset = 100,000
            skyboxoffset = asset.scaffoldCount;

            ALLGAUSS = (GAUSS_MEMLIMIT + asset.scaffoldCount);
            Debug.Log("ALLGAUSS" + ALLGAUSS.ToString());

            
            for (int i = 0; i < 2; i++)
            {
                mems[i].pos_buff = new Vector3 [ALLGAUSS];
                mems[i].scales_buff = new Vector3 [ALLGAUSS];
                mems[i].rots_buff = new Vector4 [ALLGAUSS];
                mems[i].alphas_buff = new float [ALLGAUSS];
                mems[i].shs_buff = new SHs [ALLGAUSS];

                mems[i].boxes_buff = new Box [GAUSS_MEMLIMIT];
                mems[i].nodes_buff = new Node [GAUSS_MEMLIMIT];

                lights[i].to_render = 0;
                lights[i].render_indices = new int [GAUSS_MEMLIMIT];
                lights[i].parent_indices = new int [GAUSS_MEMLIMIT];
                lights[i].nodes_of_render_indices = new int [GAUSS_MEMLIMIT];
            }

            currSet = &lights[0];
            otherSet = &lights[1];

            currMem = &mems[0];
            otherMem = &mems[1];
            //nodes_to_copy...I don't think I need these ones.

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
            render_indices = new int[GAUSS_MEMLIMIT];
            parent_indices = new int[GAUSS_MEMLIMIT];
            nodes_of_render_indices = new int[GAUSS_MEMLIMIT];
            splits1 = new int [GAUSS_MEMLIMIT];
            splits2 = new int [GAUSS_MEMLIMIT];
            node_indices1 = new int [GAUSS_MEMLIMIT];
            node_indices2 = new int [GAUSS_MEMLIMIT];
            interp_taus = new float[GAUSS_MEMLIMIT];
            kids = new int[GAUSS_MEMLIMIT];

            node_indices1[0] = 0;
            num_active_nodes = 1;
            num_active_nodes_gpu = 1;


            AddNodePackage(new int[] {0}, new int[] {-1}, currMem);

            InitGraphicsBuffers();
            SetGraphicsBuffers();


            /*m_GpuPosData = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, (int) (asset.posData.dataSize / 4), 4) { name = "HierarchicalPosData" };
            m_GpuPosData.SetData(asset.posData.GetData<uint>());
            m_GpuOtherData = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, (int) (asset.otherData.dataSize / 4), 4) { name = "HierarchicalOtherData" };
            m_GpuOtherData.SetData(asset.otherData.GetData<uint>());
            m_GpuSHData = new GraphicsBuffer(GraphicsBuffer.Target.Raw, (int) (asset.shData.dataSize / 4), 4) { name = "HierarchicalSHData" };
            m_GpuSHData.SetData(asset.shData.GetData<uint>());
            var (texWidth, texHeight) = HierarchicalSplatAsset.CalcTextureSize(asset.splatCount);
            var texFormat = HierarchicalSplatAsset.ColorFormatToGraphics(asset.colorFormat);
            var tex = new Texture2D(texWidth, texHeight, texFormat, TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate)   { name = "HierarchicalColorData" };

            tex.SetPixelData(asset.colorData.GetData<byte>(), 0);
            tex.Apply(false, true);
            m_GpuColorData = tex;
            if (asset.chunkData != null && asset.chunkData.dataSize != 0)
            {
                m_GpuChunks = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    (int) (asset.chunkData.dataSize / UnsafeUtility.SizeOf<HierarchicalSplatAsset.ChunkInfo>()),
                    UnsafeUtility.SizeOf<HierarchicalSplatAsset.ChunkInfo>()) {name = "HierarchicalChunkData"};
                m_GpuChunks.SetData(asset.chunkData.GetData<HierarchicalSplatAsset.ChunkInfo>());
                m_GpuChunksValid = true;
            }
            else
            {
                // just a dummy chunk buffer
                m_GpuChunks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1,
                    UnsafeUtility.SizeOf<HierarchicalSplatAsset.ChunkInfo>()) {name = "HierarchicalChunkData"};
                m_GpuChunksValid = false;
            }

            m_GpuView = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_Asset.splatCount, kGpuViewDataSize);
            m_GpuIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, 36, 2);
            // cube indices, most often we use only the first quad
            m_GpuIndexBuffer.SetData(new ushort[]
            {
                0, 1, 2, 1, 3, 2,
                4, 6, 5, 5, 6, 7,
                0, 2, 4, 4, 2, 6,
                1, 5, 3, 5, 7, 3,
                0, 4, 1, 4, 5, 1,
                2, 3, 6, 3, 7, 6
            });
            */

            //InitSortBuffers(splatCount);
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
            nodesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GAUSS_MEMLIMIT, sizeof(int) * 7);
            nodesBuff.SetData(nodes_to_render);
            boxesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GAUSS_MEMLIMIT, sizeof(float) * 8);
            boxesBuff.SetData(boxes_to_render);

            renderIndicesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            parentIndicesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            nodesOfRenderIndicesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            splitsBuff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            splitsBuff.SetData(splits1);
            nodeIndicesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            nodeIndicesBuff.SetData(node_indices1);
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
            newNodeIndicesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GAUSS_MEMLIMIT, sizeof(int));
            newNodeIndicesBuff.SetData(node_indices2);
            numIBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int));
            numIBuff.SetData(new int[] { 0 });
            outNBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int));
            outNBuff.SetData(new int[] { 0 });
        }

        void SetGraphicsBuffers()
        {
            m_CSHierarchicalCut.SetBuffer(0, "nodes", nodesBuff);
            m_CSHierarchicalCut.SetBuffer(1, "nodes", nodesBuff);
            m_CSHierarchicalCut.SetBuffer(2, "nodes", nodesBuff);
            m_CSHierarchicalCut.SetBuffer(3, "nodes", nodesBuff);
            m_CSHierarchicalCut.SetBuffer(4, "nodes", nodesBuff);
            m_CSHierarchicalCut.SetBuffer(5, "nodes", nodesBuff);

            m_CSHierarchicalCut.SetBuffer(0, "boxes", boxesBuff);
            m_CSHierarchicalCut.SetBuffer(5, "boxes", boxesBuff);

            m_CSHierarchicalCut.SetBuffer(3, "render_indices", renderIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(3, "parent_indices", parentIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(3, "nodes_of_render_indices", nodesOfRenderIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(5, "nodes_of_render_indices", nodesOfRenderIndicesBuff);

            m_CSHierarchicalCut.SetBuffer(0, "splits", splitsBuff);
            m_CSHierarchicalCut.SetBuffer(2, "splits", splitsBuff);

            m_CSHierarchicalCut.SetBuffer(0, "node_indices", nodeIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(1, "node_indices", nodeIndicesBuff);

            m_CSHierarchicalCut.SetBuffer(1, "new_node_indices", newNodeIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(2, "new_node_indices", newNodeIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(3, "new_node_indices", newNodeIndicesBuff);

            m_CSHierarchicalCut.SetBuffer(4, "nodes_to_expand", nodesToExpandBuff);
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

            m_CSHierarchicalCut.SetInt("N", num_active_nodes);
        }

        bool AddNodePackage(int[] n_indices, int[] p_indices, MemSet* useMem)
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
                    int dst = skyboxoffset + gaussians_offset + copied_gaussians + j;

                    useMem->pos_buff[dst] = asset.Pos[src];
                    useMem->rots_buff[dst] = asset.Rots[src];
                    useMem->shs_buff[dst] = asset.SHs[src];
                    useMem->alphas_buff[dst] = asset.Alphas[src];
                    useMem->scales_buff[dst] = asset.Scales[src];
                }
                node.start_children = -1;
                node.start = gaussians_offset + copied_gaussians;
                node.parent = parent;

                useMem->nodes_buff[nodes_offset + i] = node;
                useMem->boxes_buff[nodes_offset + i] = asset.Boxes[id];

                cuda2cpu[nodes_offset + i] = id;

                copied_gaussians += count;
            }

            gaussians_offset += gaussian_copy_count;
            nodes_offset += node_copy_count;
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

            nodesToExpandBuff.GetData(need_children);
            
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
            for (int k = 0; k < num_get_children; k++)
            {
                int id = need_children[k];
                int node_id = cuda2cpu[id];
                Node node = asset.Nodes[node_id];
                ans += "\tnode id: " + node_id.ToString() + "\n";
                for (int i = 0; i < node.count_children; i++)
                {
                    ans += "\t" + (asset.Nodes[node_id].start_children).ToString() + "\n";
                    ans += "\t" + (id).ToString() + "\n";
                    n_indices[nodes_expanded + i] = node.start_children + i;
                    p_indices[nodes_expanded + i] = id;
                }
                package_parent_starts[k] = nodes_offset + nodes_expanded;
                nodes_expanded += node.count_children;
            }
            Debug.Log(ans);
            return num_get_children;
        }
        // AsyncTask
        public (int, int, MemSet*) CreateHierarchicalCut(bool cleanup)
        {
            nodeIndicesBuff.GetData(node_indices1);
            
            /*string ans = "";
            for (int i = 0; i< 30; i++)
            {
                ans += node_indices1[i].ToString();
            }
            Debug.Log("node_indices1 (activenodes1_cuda): " + ans);

            splitsBuff.GetData(splits1);
            
            ans = "";
            for (int i = 0; i< 30; i++)
            {
                ans += splits1[i].ToString();
            }
            Debug.Log("splits1 (splits1_cuda): " + ans);*/

            MemSet* useMem = currMem;

            if (!changeToSizeStep(useMem))
                Debug.LogError("Doing a step didn't work");

            (activenodes1, activenodes2) = (activenodes2, activenodes1);
            
            int num_get_children = 0;
            int num_transferred = 0;
            if (!cleanup && num_need_children > 0)
            {
                if (!ran_out)
                {
                    int[] package_indices;
                    int[] package_parent_indices;

                    int num_new_parents = createNodePackage(out package_indices, out package_parent_indices);

                    ans = "";
                    for (int i = 0; i< 30; i++)
                    {
                        ans += package_parent_starts[i].ToString();
                    }
                    Debug.Log("package_parent_starts (package_parent_cuda_starts): " + ans);
                    Debug.Log("num_new_parents: " + num_new_parents.ToString());
                    if (AddNodePackage(package_indices, package_parent_indices, useMem))
                    {
                        NsrcIBuff.GetData(package_parent_starts);
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

        bool changeToSizeStep(MemSet* useMem)
        {
            nodeIndicesBuff.SetData(node_indices1);
            newNodeIndicesBuff.SetData(node_indices2);
            nodesBuff.SetData(useMem->nodes_buff);
            boxesBuff.SetData(useMem->boxes_buff);
            splitsBuff.SetData(splits1);
            renderIndicesBuff.SetData(otherSet->render_indices);
            parentIndicesBuff.SetData(otherSet->parent_indices);
            nodesOfRenderIndicesBuff.SetData(otherSet->nodes_of_render_indices);

            m_CSHierarchicalCut.SetBuffer(0, "node_indices", nodeIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(1, "node_indices", nodeIndicesBuff);

            m_CSHierarchicalCut.SetBuffer(1, "new_node_indices", newNodeIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(2, "new_node_indices", newNodeIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(3, "new_node_indices", newNodeIndicesBuff);

            m_CSHierarchicalCut.SetBuffer(0, "nodes", nodesBuff);
            m_CSHierarchicalCut.SetBuffer(1, "nodes", nodesBuff);
            m_CSHierarchicalCut.SetBuffer(2, "nodes", nodesBuff);
            m_CSHierarchicalCut.SetBuffer(3, "nodes", nodesBuff);
            m_CSHierarchicalCut.SetBuffer(4, "nodes", nodesBuff);

            m_CSHierarchicalCut.SetBuffer(0, "boxes", boxesBuff);
            
            m_CSHierarchicalCut.SetBuffer(0, "splits", splitsBuff);
            m_CSHierarchicalCut.SetBuffer(2, "splits", splitsBuff);

            m_CSHierarchicalCut.SetBuffer(3, "render_indices", renderIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(3, "parent_indices", parentIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(3, "nodes_of_render_indices", nodesOfRenderIndicesBuff);

            m_CSHierarchicalCut.SetVector("viewpoint", cam_pos);
            m_CSHierarchicalCut.SetFloat("target_size", sizeLimit);

            int num_node_blocks = (num_active_nodes + 255) / 256;

            DispatchChangeNodesShader(num_node_blocks);
            DispatchFlaggedShader(1024, num_active_nodes);
            DispatchInclusiveSumShader(1024, num_active_nodes);

            int [] buffer = new int[1];
            numIBuff.GetData(buffer);
            num_need_children = buffer[0];
            Debug.Log("num_need_children (need_expansion): " + num_need_children.ToString());
            outNBuff.GetData(buffer);
            new_node_count = buffer[0];
            Debug.Log("new_node_count (new_N): " + new_node_count.ToString());

            if (new_node_count > GAUSSIAN_MEMLIMIT)
                return false;

            DispatchPutNodesShader(num_node_blocks);

            int num_render_blocks = (new_node_count + 255) / 256;

            DispatchRenderIndicesIndexed(num_render_blocks);
            DispatchInclusiveSumShader(1024, new_node_count);
            DispatchPutRenderIndicesIndexed(num_render_blocks);

            outNBuff.GetData(buffer);
            to_render_num = buffer[0];
            Debug.Log("to_render_num: (new_R)" + to_render_num.ToString());
            return true;
        }

        public void DispatchChangeNodesShader(int threads) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("changeNodesOnce");
            m_CSHierarchicalCut.SetInt("N", num_active_nodes_gpu);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);
        }
        public void DispatchPutNodesShader(int threads) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("putNodes");
            m_CSHierarchicalCut.SetInt("N", num_active_nodes_gpu);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);
        }
        public void DispatchRenderIndicesIndexed(int threads) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("countRenderIndicesIndexed");
            m_CSHierarchicalCut.SetInt("N", new_node_count);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);
        }
        public void DispatchPutRenderIndicesIndexed(int threads) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("putRenderIndicesIndexed");
            m_CSHierarchicalCut.SetInt("N", new_node_count);
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
            nodesBuff.SetData(currMem->nodes_buff);
            m_CSHierarchicalCut.SetInt("N", num_get_children);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);
        }

        public void DispatchComputeTsIndexed(int threads) 
        {
            int kernel = m_CSHierarchicalCut.FindKernel("computeTsIndexed");
            m_CSHierarchicalCut.SetInt("to_render_num", currSet->to_render);
            m_CSHierarchicalCut.SetInt("target_size", sizeLimit);
            nodesOfRenderIndicesBuff.SetData(currSet->nodes_of_render_indices);
            nodesBuff.SetData(currMem->nodes_buff);
            boxesBuff.SetData(currMem->boxes_buff);
            m_CSHierarchicalCut.SetBuffer(kernel, "nodes_of_render_indices", nodesOfRenderIndicesBuff);
            m_CSHierarchicalCut.SetBuffer(kernel, "nodes", nodesBuff);
            m_CSHierarchicalCut.SetBuffer(kernel, "boxes", boxesBuff);
            m_CSHierarchicalCut.Dispatch(kernel, threads, 1, 1);
            
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
            renderIndicesBuff.GetData(render_indices);
            ans = "";
            for (int i = 0; i<30; i++) 
                ans += render_indices[i].ToString() + " ";
            Debug.Log("render_indices: " + ans);
            parentIndicesBuff.GetData(parent_indices);
            ans = "";
            for (int i = 0; i<30; i++) 
                ans += parent_indices[i].ToString() + " ";
            Debug.Log("parent_indices: " + ans);
            nodesOfRenderIndicesBuff.GetData(nodes_of_render_indices);
            ans = "";
            for (int i = 0; i<30; i++) 
                ans += nodes_of_render_indices[i].ToString() + " ";
            Debug.Log("nodes_of_render_indices: " + ans);
        }

        /*void InitSortBuffers(int count)
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
        }*/

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

        /*void SetAssetDataOnCS(CommandBuffer cmb, KernelIndices kernel)
        {
            ComputeShader cs = m_CSSplatUtilities;
            int kernelIndex = (int) kernel;
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatPos, m_GpuPosData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatChunks, m_GpuChunks);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatOther, m_GpuOtherData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatSH, m_GpuSHData);
            cmb.SetComputeTextureParam(cs, kernelIndex, Props.SplatColor, m_GpuColorData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatSelectedBits, m_GpuEditSelected ?? m_GpuPosData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatDeletedBits, m_GpuEditDeleted ?? m_GpuPosData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatViewData, m_GpuView);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.OrderBuffer, m_GpuSortKeys);

            cmb.SetComputeIntParam(cs, Props.SplatBitsValid, m_GpuEditSelected != null && m_GpuEditDeleted != null ? 1 : 0);
            uint format = (uint)m_Asset.posFormat | ((uint)m_Asset.scaleFormat << 8) | ((uint)m_Asset.shFormat << 16);
            cmb.SetComputeIntParam(cs, Props.SplatFormat, (int)format);
            cmb.SetComputeIntParam(cs, Props.SplatCount, m_SplatCount);
            cmb.SetComputeIntParam(cs, Props.SplatChunkCount, m_GpuChunksValid ? m_GpuChunks.count : 0);

            UpdateCutoutsBuffer();
            cmb.SetComputeIntParam(cs, Props.SplatCutoutsCount, m_Cutouts?.Length ?? 0);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatCutouts, m_GpuEditCutouts);
            
        }*/

        /*internal void SetAssetDataOnMaterial(MaterialPropertyBlock mat)
        {
            mat.SetBuffer(Props.SplatPos, m_GpuPosData);
            mat.SetBuffer(Props.SplatOther, m_GpuOtherData);
            mat.SetBuffer(Props.SplatSH, m_GpuSHData);
            mat.SetTexture(Props.SplatColor, m_GpuColorData);
            mat.SetBuffer(Props.SplatSelectedBits, m_GpuEditSelected ?? m_GpuPosData);
            mat.SetBuffer(Props.SplatDeletedBits, m_GpuEditDeleted ?? m_GpuPosData);
            mat.SetInt(Props.SplatBitsValid, m_GpuEditSelected != null && m_GpuEditDeleted != null ? 1 : 0);
            uint format = (uint)m_Asset.posFormat | ((uint)m_Asset.scaleFormat << 8) | ((uint)m_Asset.shFormat << 16);
            mat.SetInteger(Props.SplatFormat, (int)format);
            mat.SetInteger(Props.SplatCount, m_SplatCount);
            mat.SetInteger(Props.SplatChunkCount, m_GpuChunksValid ? m_GpuChunks.count : 0);
            
        }*/

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

            nodesBuff?.Release();
            boxesBuff?.Release();
            renderIndicesBuff?.Release();
            parentIndicesBuff?.Release();
            nodesOfRenderIndicesBuff?.Release();
            splitsBuff?.Release();
            nodeIndicesBuff?.Release();
            nodesToExpandBuff?.Release();
            interpTausBuff?.Release();
            kidsBuff?.Release();
            NdstIBuff?.Release();
            NsrcIBuff?.Release();
            NsrcCBuff?.Release();
            newNodeIndicesBuff?.Release();
            numIBuff?.Release();
            outNBuff?.Release();

            nodesBuff = null;
            boxesBuff = null;
            renderIndicesBuff = null;
            parentIndicesBuff = null;
            nodesOfRenderIndicesBuff = null;
            splitsBuff = null;
            nodeIndicesBuff = null;
            nodesToExpandBuff = null;
            interpTausBuff = null;
            kidsBuff = null;
            NdstIBuff = null;
            NsrcIBuff = null;
            NsrcCBuff = null;
            newNodeIndicesBuff = null;
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

        /*internal void CalcViewData(CommandBuffer cmb, Camera cam)
        {
            if (cam.cameraType == CameraType.Preview)
                return;

            var tr = transform;

            Matrix4x4 matView = cam.worldToCameraMatrix;
            Matrix4x4 matO2W = tr.localToWorldMatrix;
            Matrix4x4 matW2O = tr.worldToLocalMatrix;
            int screenW = cam.pixelWidth, screenH = cam.pixelHeight;
            int eyeW = XRSettings.eyeTextureWidth, eyeH = XRSettings.eyeTextureHeight;
            Vector4 screenPar = new Vector4(eyeW != 0 ? eyeW : screenW, eyeH != 0 ? eyeH : screenH, 0, 0);
            Vector4 camPos = cam.transform.position;

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
        }*/
        public void CalcViewData(Camera cam)
        {
            if (cam.cameraType == CameraType.Preview)
                return;
                
            m_FrameCounter++;
            var tr = transform;

            Matrix4x4 matView = cam.worldToCameraMatrix;
            Matrix4x4 matO2W = tr.localToWorldMatrix;
            Matrix4x4 matW2O = tr.worldToLocalMatrix;
            int screenW = cam.pixelWidth, screenH = cam.pixelHeight;
            int eyeW = XRSettings.eyeTextureWidth, eyeH = XRSettings.eyeTextureHeight;
            Vector4 screenPar = new Vector4(eyeW != 0 ? eyeW : screenW, eyeH != 0 ? eyeH : screenH, 0, 0);
            Vector4 camPos = cam.transform.position;

            cam_pos = new Vector4(3.27458f, -48.7878f, 3.3452f);

            m_ZDirection = new Vector3(0.16184f, 0.818862f, -0.550702f);

            if (m_FrameCounter <50) {
                Debug.Log("zdir: " + m_ZDirection);
                Debug.Log("cam_pos: " + cam_pos);
            }
        }

        /*internal void SortPoints(CommandBuffer cmd, Camera cam, Matrix4x4 matrix)
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
        }*/

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
            sizeLimit = 0.0211829;
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

        /*void ClearGraphicsBuffer(GraphicsBuffer buf)
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
        }*/

        static float SortableUintToFloat(uint v)
        {
            uint mask = ((v >> 31) - 1) | 0x80000000u;
            return math.asfloat(v ^ mask);
        }

        /*public void UpdateEditCountsAndBounds()
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
            int bufferSize = m_Cutouts?.Length ?? 0;
            if (bufferSize == 0)
                bufferSize = 1;
            if (m_GpuEditCutouts == null || m_GpuEditCutouts.count != bufferSize)
            {
                m_GpuEditCutouts?.Dispose();
                m_GpuEditCutouts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bufferSize, UnsafeUtility.SizeOf<GaussianCutout.ShaderData>()) { name = "GaussianCutouts" };
            }

            NativeArray<GaussianCutout.ShaderData> data = new(bufferSize, Allocator.Temp);
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

        public void EditUpdateSelection(Vector2 rectMin, Vector2 rectMax, Camera cam, bool subtract)
        {
            if (!EnsureEditingBuffers()) return;

            Graphics.CopyBuffer(m_GpuEditSelectedMouseDown, m_GpuEditSelected);

            var tr = transform;
            Matrix4x4 matView = cam.worldToCameraMatrix;
            Matrix4x4 matO2W = tr.localToWorldMatrix;
            Matrix4x4 matW2O = tr.worldToLocalMatrix;
            int screenW = cam.pixelWidth, screenH = cam.pixelHeight;
            Vector4 screenPar = new Vector4(screenW, screenH, 0, 0);
            Vector4 camPos = cam.transform.position;

            using var cmb = new CommandBuffer { name = "SplatSelectionUpdate" };
            SetAssetDataOnCS(cmb, KernelIndices.SelectionUpdate);

            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixMV, matView * matO2W);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, matO2W);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixWorldToObject, matW2O);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.VecScreenParams, screenPar);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.VecWorldSpaceCameraPos, camPos);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_SelectionRect", new Vector4(rectMin.x, rectMax.y, rectMax.x, rectMin.y));
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.SelectionMode, subtract ? 0 : 1);

            DispatchUtilsAndExecute(cmb, KernelIndices.SelectionUpdate, m_SplatCount);
            UpdateEditCountsAndBounds();
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

        public void EditDeleteSelected()
        {
            if (!EnsureEditingBuffers()) return;
            UnionGraphicsBuffers(m_GpuEditDeleted, m_GpuEditSelected);
            EditDeselectAll();
            UpdateEditCountsAndBounds();
            if (editDeletedSplats != 0)
                editModified = true;
        }

        public void EditSelectAll()
        {
            if (!EnsureEditingBuffers()) return;
            using var cmb = new CommandBuffer { name = "SplatSelectAll" };
            SetAssetDataOnCS(cmb, KernelIndices.SelectAll);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.SelectAll, Props.DstBuffer, m_GpuEditSelected);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.BufferSize, m_GpuEditSelected.count);
            DispatchUtilsAndExecute(cmb, KernelIndices.SelectAll, m_GpuEditSelected.count);
            UpdateEditCountsAndBounds();
        }

        public void EditDeselectAll()
        {
            if (!EnsureEditingBuffers()) return;
            ClearGraphicsBuffer(m_GpuEditSelected);
            UpdateEditCountsAndBounds();
        }

        public void EditInvertSelection()
        {
            if (!EnsureEditingBuffers()) return;

            using var cmb = new CommandBuffer { name = "SplatInvertSelection" };
            SetAssetDataOnCS(cmb, KernelIndices.InvertSelection);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.InvertSelection, Props.DstBuffer, m_GpuEditSelected);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.BufferSize, m_GpuEditSelected.count);
            DispatchUtilsAndExecute(cmb, KernelIndices.InvertSelection, m_GpuEditSelected.count);
            UpdateEditCountsAndBounds();
        }

        public bool EditExportData(GraphicsBuffer dstData, bool bakeTransform)
        {
            if (!EnsureEditingBuffers()) return false;

            int flags = 0;
            var tr = transform;
            Quaternion bakeRot = tr.localRotation;
            Vector3 bakeScale = tr.localScale;

            if (bakeTransform)
                flags = 1;

            using var cmb = new CommandBuffer { name = "SplatExportData" };
            SetAssetDataOnCS(cmb, KernelIndices.ExportData);
            cmb.SetComputeIntParam(m_CSSplatUtilities, "_ExportTransformFlags", flags);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_ExportTransformRotation", new Vector4(bakeRot.x, bakeRot.y, bakeRot.z, bakeRot.w));
            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_ExportTransformScale", bakeScale);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, tr.localToWorldMatrix);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.ExportData, "_ExportBuffer", dstData);

            DispatchUtilsAndExecute(cmb, KernelIndices.ExportData, m_SplatCount);
            return true;
        }

        public void EditSetSplatCount(int newSplatCount)
        {
            if (newSplatCount <= 0 || newSplatCount > HierarchicalSplatAsset.kMaxSplats)
            {
                Debug.LogError($"Invalid new splat count: {newSplatCount}");
                return;
            }
            if (newSplatCount == splatCount)
                return;

            int posStride = (int)(asset.posData.dataSize / asset.splatCount);
            int otherStride = (int)(asset.otherData.dataSize / asset.splatCount);
            int shStride = (int) (asset.shData.dataSize / asset.splatCount);

            // create new GPU buffers
            var newPosData = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, newSplatCount * posStride / 4, 4) { name = "GaussianPosData" };
            var newOtherData = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, newSplatCount * otherStride / 4, 4) { name = "GaussianOtherData" };
            var newSHData = new GraphicsBuffer(GraphicsBuffer.Target.Raw, newSplatCount * shStride / 4, 4) { name = "GaussianSHData" };

            // new texture is a RenderTexture so we can write to it from a compute shader
            var (texWidth, texHeight) = HierarchicalSplatAsset.CalcTextureSize(newSplatCount);
            var texFormat = HierarchicalSplatAsset.ColorFormatToGraphics(asset.colorFormat);
            var newColorData = new RenderTexture(texWidth, texHeight, texFormat, GraphicsFormat.None) { name = "GaussianColorData", enableRandomWrite = true };
            newColorData.Create();

            // selected/deleted buffers
            var selTarget = GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource | GraphicsBuffer.Target.CopyDestination;
            var selSize = (newSplatCount + 31) / 32;
            var newEditSelected = new GraphicsBuffer(selTarget, selSize, 4) {name = "HierarchicalSplatSelected"};
            var newEditSelectedMouseDown = new GraphicsBuffer(selTarget, selSize, 4) {name = "HierarchicalSplatSelectedInit"};
            var newEditDeleted = new GraphicsBuffer(selTarget, selSize, 4) {name = "HierarchicalSplatDeleted"};
            ClearGraphicsBuffer(newEditSelected);
            ClearGraphicsBuffer(newEditSelectedMouseDown);
            ClearGraphicsBuffer(newEditDeleted);

            var newGpuView = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newSplatCount, kGpuViewDataSize);
            InitSortBuffers(newSplatCount);

            // copy existing data over into new buffers
            EditCopySplats(transform, newPosData, newOtherData, newSHData, newColorData, newEditDeleted, newSplatCount, 0, 0, m_SplatCount);

            // use the new buffers and the new splat count
            m_GpuPosData.Dispose();
            m_GpuOtherData.Dispose();
            m_GpuSHData.Dispose();
            DestroyImmediate(m_GpuColorData);
            m_GpuView.Dispose();

            m_GpuEditSelected?.Dispose();
            m_GpuEditSelectedMouseDown?.Dispose();
            m_GpuEditDeleted?.Dispose();

            m_GpuPosData = newPosData;
            m_GpuOtherData = newOtherData;
            m_GpuSHData = newSHData;
            m_GpuColorData = newColorData;
            m_GpuView = newGpuView;
            m_GpuEditSelected = newEditSelected;
            m_GpuEditSelectedMouseDown = newEditSelectedMouseDown;
            m_GpuEditDeleted = newEditDeleted;

            DisposeBuffer(ref m_GpuEditPosMouseDown);
            DisposeBuffer(ref m_GpuEditOtherMouseDown);

            m_SplatCount = newSplatCount;
            editModified = true;
            
        }

        public void EditCopySplatsInto(HierarchicalSplatRenderer dst, int copySrcStartIndex, int copyDstStartIndex, int copyCount)
        {
            EditCopySplats(
                dst.transform,
                dst.m_GpuPosData, dst.m_GpuOtherData, dst.m_GpuSHData, dst.m_GpuColorData, dst.m_GpuEditDeleted,
                dst.splatCount,
                copySrcStartIndex, copyDstStartIndex, copyCount);
            dst.editModified = true;
        }

        public void EditCopySplats(
            Transform dstTransform,
            GraphicsBuffer dstPos, GraphicsBuffer dstOther, GraphicsBuffer dstSH, Texture dstColor,
            GraphicsBuffer dstEditDeleted,
            int dstSize,
            int copySrcStartIndex, int copyDstStartIndex, int copyCount)
        {
            if (!EnsureEditingBuffers()) return;

            Matrix4x4 copyMatrix = dstTransform.worldToLocalMatrix * transform.localToWorldMatrix;
            Quaternion copyRot = copyMatrix.rotation;
            Vector3 copyScale = copyMatrix.lossyScale;

            using var cmb = new CommandBuffer { name = "SplatCopy" };
            SetAssetDataOnCS(cmb, KernelIndices.CopySplats);

            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstPos", dstPos);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstOther", dstOther);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstSH", dstSH);
            cmb.SetComputeTextureParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstColor", dstColor);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstEditDeleted", dstEditDeleted);

            cmb.SetComputeIntParam(m_CSSplatUtilities, "_CopyDstSize", dstSize);
            cmb.SetComputeIntParam(m_CSSplatUtilities, "_CopySrcStartIndex", copySrcStartIndex);
            cmb.SetComputeIntParam(m_CSSplatUtilities, "_CopyDstStartIndex", copyDstStartIndex);
            cmb.SetComputeIntParam(m_CSSplatUtilities, "_CopyCount", copyCount);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_CopyTransformRotation", new Vector4(copyRot.x, copyRot.y, copyRot.z, copyRot.w));
            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_CopyTransformScale", copyScale);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, "_CopyTransformMatrix", copyMatrix);

            DispatchUtilsAndExecute(cmb, KernelIndices.CopySplats, copyCount);
        }

        void DispatchUtilsAndExecute(CommandBuffer cmb, KernelIndices kernel, int count)
        {
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)kernel, out uint gsX, out _, out _);
            cmb.DispatchCompute(m_CSSplatUtilities, (int)kernel, (int)((count + gsX - 1)/gsX), 1, 1);
            Graphics.ExecuteCommandBuffer(cmb);
        }

        public GraphicsBuffer GpuEditDeleted => m_GpuEditDeleted;*/
    }
}