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
        [SerializeField] TextAsset m_PosData;
        [SerializeField] TextAsset m_OtherData;
        [SerializeField] TextAsset m_ColorData;
        [SerializeField] TextAsset[] m_SHData;
        [SerializeField] TextAsset m_BoxData;
        [SerializeField] TextAsset m_NodeData;

        [SerializeField] int[] m_nodeIndices;

        public int formatVersion => m_FormatVersion;
        public int splatCount => m_SplatCount;
        public int scaffoldCount => m_ScaffoldCount;
        public Hash128 dataHash => m_DataHash;

        public TextAsset posData => m_PosData;
        public TextAsset otherData => m_OtherData;
        public TextAsset colorData => m_ColorData; 
        public TextAsset[] shData => m_SHData; 
        public TextAsset nodeData => m_NodeData;
        public TextAsset boxData => m_BoxData;
        public int[] nodeIndices => m_nodeIndices;



        public void Initialize(int splats, int skyboxnum)
        {
            Debug.Log("Initialize");
            m_SplatCount = splats;
            m_ScaffoldCount = skyboxnum;
            m_FormatVersion = kCurrentVersion;
            m_nodeIndices = new int[splats];
            for (int i =0; i< splats; i++)
                m_nodeIndices[i] = i;
        }

        public void SetDataHash(Hash128 hash)
        {
            m_DataHash = hash;
        }

        public void SetAssetFiles(TextAsset dataPos, TextAsset dataOther, TextAsset dataColor, TextAsset[] dataSh, TextAsset dataBox, TextAsset dataNode)
        {
            m_PosData = dataPos;
            m_OtherData = dataOther;
            m_ColorData = dataColor;
            m_SHData = dataSh;
            m_BoxData = dataBox;
            m_NodeData = dataNode;
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