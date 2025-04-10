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

        public int formatVersion => m_FormatVersion;
        public int splatCount => m_SplatCount;
        public int scaffoldCount => m_ScaffoldCount;
        long cachedMemorySize = -1;
        [HideInInspector]
        public uint[] posData;
        [HideInInspector]
        public uint[] otherData;
        [HideInInspector]
        public Vector4[] colorData;
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
        public Vector4[] allColor;
        [HideInInspector]
        public uint[] allSHs;

        public struct ChunkInfo
        {
            public uint colR, colG, colB, colA;
            public float2 posX, posY, posZ;
            public uint sclX, sclY, sclZ;
            public uint shR, shG, shB;
        }


        public int padded;
        public void Initialize(int splats, int skyboxnum)
        {
            Debug.Log("Initialize");
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