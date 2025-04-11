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

        long cachedMemorySize = -1;
        [HideInInspector]
        public uint[] posData;
        [HideInInspector]
        public uint[] otherData;
        [HideInInspector]
        public float4[] colorData;
        [HideInInspector]
        public uint[] shData;
        [HideInInspector]
        public Node[] nodeData;
        [HideInInspector]
        public Box[] boxData;
        [HideInInspector]
        public uint[] allPos;
        [HideInInspector]
        public uint[] allOther;
        [HideInInspector]
        public float4[] allColor;
        [HideInInspector]
        public uint[] allSHs;

        public struct ChunkInfo
        {
            public uint colR, colG, colB, colA;
            public float2 posX, posY, posZ;
            public uint sclX, sclY, sclZ;
            public uint shR, shG, shB;
        }

        public void Initialize(int splats, int skyboxnum)
        {
            m_SplatCount = splats;
            m_ScaffoldCount = skyboxnum;
            m_FormatVersion = kCurrentVersion;
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

        public void SetDataHash(Hash128 hash)
        {
            m_DataHash = hash;
        }

        public long CalculateMemorySize()
        {
            if (cachedMemorySize == -1) // Only calculate if not cached
            {
                long sizePos = splatCount * 3 * sizeof(float);
                long sizeSHs = splatCount * 48 * sizeof(float);
                long sizeRots = splatCount * 4 * sizeof(float);
                long sizeScales = splatCount * 3 * sizeof(float);
                long sizeAlphas = splatCount * sizeof(float);
                long sizeNodes = splatCount * 7 * sizeof(int);
                long sizeBoxes = splatCount * 8 * sizeof(float);

                cachedMemorySize = sizePos + sizeSHs + sizeRots + sizeScales + sizeAlphas + sizeNodes + sizeBoxes;
            }

            return cachedMemorySize;
        }
    }
}