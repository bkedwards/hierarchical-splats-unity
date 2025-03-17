// SPDX-License-Identifier: MIT

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using HierarchicalSplatting;
using UnityEngine.Experimental.Rendering;

namespace HierarchicalSplatting.Runtime
{
    public class HierarchicalSplatAsset : ScriptableObject
    {
        public const int kCurrentVersion = 2025_03_10;
        public const int kChunkSize = 256;
        public const int kTextureWidth = 2048; // allows up to 32M splats on desktop GPU (2k width x 16k height)
        public const int kMaxSplats = 8_600_000; // mostly due to 2GB GPU buffer size limit when exporting a splat (2GB / 248B is just over 8.6M)

        [SerializeField] int m_FormatVersion;
        [SerializeField] int m_SplatCount;
        [SerializeField] int m_ScaffoldCount;
        [SerializeField] Hash128 m_DataHash;

        public int formatVersion => m_FormatVersion;
        public int splatCount => m_SplatCount;
        public int scaffoldCount => m_ScaffoldCount;
        public Hash128 dataHash => m_DataHash;

        public NativeArray<Vector3> Pos;
        public NativeArray<Vector3> Scales;
        public NativeArray<Vector4> Rots;
        public NativeArray<float> Alphas;
        public NativeArray<SHs> SHs;
        public NativeArray<Box> Boxes;
        public NativeArray<Node> Nodes;

        public NativeArray<Vector3> SkyPos;
        public NativeArray<Vector3> SkyScale;
        public NativeArray<Vector4> SkyRot;
        public NativeArray<float> SkyAlpha;
        public NativeArray<SHs> SkySH;



        public void Initialize(int splats, int skyboxnum)
        {
            Debug.Log("Initialize");
            m_SplatCount = splats;
            m_ScaffoldCount = skyboxnum;
            m_FormatVersion = kCurrentVersion;
        }

        public void SetDataHash(Hash128 hash)
        {
            m_DataHash = hash;
        }

        public void SetHierarchyData(
            NativeArray<Vector3> Pos,
            NativeArray<Vector3> Scales,
            NativeArray<Vector4> Rots,
            NativeArray<float> Alphas,
            NativeArray<SHs> SHs,
            NativeArray<Box> Boxes,
            NativeArray<Node> Nodes)
        {
            this.Pos = Pos;
            this.Scales = Scales;
            this.Rots = Rots;
            this.Alphas = Alphas;
            this.SHs = SHs;
            this.Boxes = Boxes;
            this.Nodes = Nodes;
        }

        public void SetScaffoldData(
            NativeArray<Vector3> SkyPos,
            NativeArray<Vector3> SkyScale,
            NativeArray<Vector4> SkyRot,
            NativeArray<float> SkyAlpha,
            NativeArray<SHs> SkySH)
        {
            this.SkyPos = SkyPos;
            this.SkyScale = SkyScale;
            this.SkyRot = SkyRot;
            this.SkyAlpha = SkyAlpha;
            this.SkySH = SkySH;
        }

        public static (int,int) CalcTextureSize(int splatCount)
        {
            int width = kTextureWidth;
            int height = math.max(1, (splatCount + width - 1) / width);
            // our swizzle tiles are 16x16, so make texture multiple of that height
            int blockHeight = 16;
            height = (height + blockHeight - 1) / blockHeight * blockHeight;
            return (width, height);
        }
    }
}