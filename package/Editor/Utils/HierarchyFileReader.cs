using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GaussianSplatting.Runtime;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using static HierarchicalSplatting.Editor.Utils.HierarchicalFileReadingUtils;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using GaussianSplatting.Editor.Utils;

namespace HierarchicalSplatting.Editor.Utils
{
    public unsafe class HierarchyFileReader {

        public static int ReadFileHeader(string filename)
        {
            int vertexCount = 0;
            if (File.Exists(filename))
            {
                using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read);
                if (fs.Length == 0)
                    throw new FileNotFoundException("File not found or is empty!");

                byte[] buffer = new byte[4];
                fs.Read(buffer, 0, 4);
                vertexCount = BitConverter.ToInt32(buffer, 0);
            }
            if (vertexCount <=0) {
                vertexCount*=-1;
            }
            return vertexCount;
        }

        static void Load(
            string filename, 
            out NativeArray<uint> pos,  
            out NativeArray<uint> shs, 
            out NativeArray<uint> other,
            out NativeArray<float4> color, 
            out NativeArray<Node> nodes, 
            out NativeArray<Box> boxes, 
            bool debug=false) 
        {
            NativeArray<byte> rawPosData;
            NativeArray<byte> rawSHData;
            NativeArray<byte> rawAlphaData;
            NativeArray<byte> rawScaleData;
            NativeArray<byte> rawRotData;
            NativeArray<byte> rawNodeData;
            NativeArray<byte> rawBoxData;

            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read);
            if (fs.Length == 0)
                throw new FileNotFoundException("File not found or is empty!");

            byte[] buffer = new byte[4];
            fs.Read(buffer, 0, 4);
            int P = BitConverter.ToInt32(buffer, 0);
            //Debug.Log($"P: {P}");

            if (P >= 0) 
            {

                rawPosData = new NativeArray<byte>(P * 3 * sizeof(uint), Allocator.Temp);
                fs.Read(rawPosData);
                var uintPosData = rawPosData.Reinterpret<uint>(1);
                pos = new NativeArray<uint>(P * 3, Allocator.Persistent);
                for (int i = 0; i < P * 3; i++) {
                    pos[i] = uintPosData[i];
                }
                rawPosData.Dispose();
                
                rawRotData = new NativeArray<byte>(P * sizeof(float4), Allocator.Temp);
                fs.Read(rawRotData);
                var float4RotData = rawRotData.Reinterpret<Unity.Mathematics.float4>(1);

                rawScaleData = new NativeArray<byte>(P * sizeof(float3), Allocator.Temp);
                fs.Read(rawScaleData);
                var float3ScaleData = rawScaleData.Reinterpret<float3>(1);

                other = new NativeArray<uint>(P * 4, Allocator.Persistent);
                for (int i = 0; i < P; i++)
                {
                    float4 rotQ = float4RotData[i].yzwx;
                    float4 rotQQ = GaussianUtils.PackSmallest3Rotation(rotQ);
                    uint enc = EncodeQuatToNorm10(rotQQ);
                    other[i * 4] = enc;
                    float3 scale = math.exp(float3ScaleData[i]);
                    other[i * 4 + 1] = math.asuint(scale.x);
                    other[i * 4 + 2] = math.asuint(scale.y);
                    other[i * 4 + 3] = math.asuint(scale.z);
                }
                rawRotData.Dispose();
                rawScaleData.Dispose();

                rawAlphaData = new NativeArray<byte>(P * sizeof(float), Allocator.Temp);
                fs.Read(rawAlphaData);
                var floatAlphaData = rawAlphaData.Reinterpret<float>(1);

                rawSHData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<SHs>(), Allocator.Temp);
                fs.Read(rawSHData);
                var SHsData = rawSHData.Reinterpret<float>(1);
                
                color = new NativeArray<float4>(P, Allocator.Persistent);
                shs = new NativeArray<uint>(P * 48, Allocator.Persistent);
                for (int i = 0; i<P; i++)
                {
                    int baseIdx = i * 48;
                    float3 rgb = new float3(SHsData[baseIdx], SHsData[baseIdx + 1], SHsData[baseIdx + 2]);
                    color[i] = new float4(rgb * 0.2820948f + 0.5f, floatAlphaData[i]);

                    for (int j = 3; j < 48; j++)
                    {
                        shs[baseIdx + (j - 3)] = math.asuint(SHsData[baseIdx + j]);
                    }
                    for (int j = 0; j < 3; j++)
                    {
                        shs[baseIdx + (j + 45)] = 0;
                    }
                }

                rawAlphaData.Dispose();
                rawSHData.Dispose();

                fs.Read(buffer, 0, 4);
                int N = BitConverter.ToInt32(buffer, 0);

                rawNodeData = new NativeArray<byte>(N * UnsafeUtility.SizeOf<Node>(), Allocator.Temp);
                fs.Read(rawNodeData);
                var NodeData = rawNodeData.Reinterpret<Node>(1);
                nodes = new NativeArray<Node>(N, Allocator.Persistent);
                for (int i = 0; i < N; i++)
                {
                    nodes[i] = NodeData[i];
                }

                rawBoxData = new NativeArray<byte>(N * UnsafeUtility.SizeOf<Box>(), Allocator.Temp);
                fs.Read(rawBoxData);
                var BoxData = rawBoxData.Reinterpret<Box>(1);
                boxes = new NativeArray<Box>(N, Allocator.Persistent);
                for (int i = 0; i < N; i++)
                {
                    boxes[i] = BoxData[i];
                }

            }
            else 
            {
                P = -P;
                int halfSize = sizeof(short); // 2 bytes

                rawPosData = new NativeArray<byte>(P * 3 * sizeof(uint), Allocator.Temp);
                fs.Read(rawPosData);
                var uintPosData = rawPosData.Reinterpret<uint>(1);
                pos = new NativeArray<uint>(P * 3, Allocator.Persistent);
                for (int i = 0; i < P * 3; i++) {
                    pos[i] = uintPosData[i];
                }
                rawPosData.Dispose();

                rawRotData = new NativeArray<byte>(P * 4 * halfSize, Allocator.Temp);
                fs.Read(rawRotData);
                NativeArray<short> halfrot = rawRotData.Reinterpret<short>(1);
                
                rawScaleData = new NativeArray<byte>(P * 3 * halfSize, Allocator.Temp);
                fs.Read(rawScaleData);
                NativeArray<short> halfscales = rawScaleData.Reinterpret<short>(1);

                other = new NativeArray<uint>(P * 4, Allocator.Persistent);
                for (int i = 0; i < P; i++)
                {
                    float4 rotQ = new float4(
                        HalfToFloat(halfrot[i * 4 + 1]),
                        HalfToFloat(halfrot[i * 4 + 2]),
                        HalfToFloat(halfrot[i * 4 + 3]),
                        HalfToFloat(halfrot[i * 4])
                        );
                    float4 rotQQ = GaussianUtils.PackSmallest3Rotation(rotQ);
                    uint enc = EncodeQuatToNorm10(rotQQ);
                    other[i * 4] = enc;
                    float3 scale = new float3(
                        HalfToFloat(halfscales[i*3]),
                        HalfToFloat(halfscales[i*3 + 1]),
                        HalfToFloat(halfscales[i*3 + 2])
                    );
                    float3 expScale = math.exp(scale);
                    other[i * 4 + 1] = math.asuint(expScale.x);
                    other[i * 4 + 2] = math.asuint(expScale.y);
                    other[i * 4 + 3] = math.asuint(expScale.z);
                }

                rawRotData.Dispose();
                rawScaleData.Dispose();

                rawAlphaData = new NativeArray<byte>(P * halfSize, Allocator.Temp);
                fs.Read(rawAlphaData);
                NativeArray<short> halfAlphas = rawAlphaData.Reinterpret<short>(1);
              
                rawSHData = new NativeArray<byte>(P * 48 * halfSize, Allocator.Temp);
                fs.Read(rawSHData);
                NativeArray<short> halfSHs = rawSHData.Reinterpret<short>(1);

                color = new NativeArray<float4>(P, Allocator.Persistent);
                shs = new NativeArray<uint>(P * 48, Allocator.Persistent);
                for (int i = 0; i<P; i++)
                {
                    int baseIdx = i * 48;
                    float c1 = HalfToFloat(halfSHs[baseIdx]);
                    float c2 = HalfToFloat(halfSHs[baseIdx + 1]);
                    float c3 = HalfToFloat(halfSHs[baseIdx + 2]);
                    float a1 = HalfToFloat(halfAlphas[i]);
                    float3 c = new float3(c1,c2,c3);
                    c = c * 0.2820948f + 0.5f;
                    color[i] = new float4(c, a1);

                    for (int j = 3; j < 48; j++)
                    {
                        shs[baseIdx + (j - 3)] = math.asuint(HalfToFloat(halfSHs[baseIdx + j]));
                    }
                    for (int j = 0; j < 3; j++)
                    {
                        shs[baseIdx + (j + 45)] = math.asuint(0);
                    }

                }

                rawAlphaData.Dispose();
                rawSHData.Dispose();

                fs.Read(buffer,0, 4);
                int N = BitConverter.ToInt32(buffer, 0);

                rawNodeData = new NativeArray<byte>(N * UnsafeUtility.SizeOf<HalfNode>(), Allocator.Temp);
                fs.Read(rawNodeData);
                NativeArray<HalfNode> halfnodes = rawNodeData.Reinterpret<HalfNode>(1);

                rawBoxData = new NativeArray<byte>(N * UnsafeUtility.SizeOf<HalfBox>(), Allocator.Temp);
                fs.Read(rawBoxData);
                NativeArray<HalfBox> halfboxes = rawBoxData.Reinterpret<HalfBox>(1);

                nodes = new NativeArray<Node>(N, Allocator.Persistent);
                boxes = new NativeArray<Box>(N, Allocator.Persistent);

                for (int i = 0; i < N; i++) {
                    nodes[i] = new Node(halfnodes[i].dccc0,halfnodes[i].parent, halfnodes[i].start, halfnodes[i].dccc2,halfnodes[i].dccc3, halfnodes[i].start_children, halfnodes[i].dccc1);
                        
                    Vector4 minn_vec = new Vector4(HalfToFloat(halfboxes[i].minn0), HalfToFloat(halfboxes[i].minn1), HalfToFloat(halfboxes[i].minn2), HalfToFloat(halfboxes[i].minn3));
                    Vector4 maxx_vec = new Vector4(HalfToFloat(halfboxes[i].maxx0), HalfToFloat(halfboxes[i].maxx1), HalfToFloat(halfboxes[i].maxx2), HalfToFloat(halfboxes[i].maxx3));
                    boxes[i] = new Box(minn_vec, maxx_vec);
                }

                rawBoxData.Dispose();
                rawNodeData.Dispose();
            }
        }        
        
        public static int LoadHierarchy(
            string filename,  
            out NativeArray<uint> pos,  
            out NativeArray<uint> shs,  
            out NativeArray<uint> other,  
            out NativeArray<float4> color,  
            out NativeArray<Node> nodes,  
            out NativeArray<Box> boxes) 
        {
            
            Load(filename, out pos, out shs, out other, out color, out nodes, out boxes, false);
            
            int P = pos.Length;

            return P;
        }
        
        
        public static int loadScaffold(
            string filename, 
            out NativeArray<uint> pos, 
            out NativeArray<uint> shs, 
            out NativeArray<float4> color,
            out NativeArray<uint> other
        ) 
        {
            
            string txtfile = filename + "/pc_info.txt";
            string plyfile = filename + "/point_cloud.ply";

            if (!File.Exists(txtfile))
                throw new Exception("Scaffold description not found! " + txtfile);

            int count;
            using (StreamReader descfile = new StreamReader(txtfile))
            {
                string line = descfile.ReadLine();
                if (line == null)
                    throw new Exception("Invalid file format: " + txtfile);

                count = int.Parse(line);
            }

            if (!File.Exists(plyfile))
                throw new Exception("Scaffold not found! " + plyfile);

            NativeArray<RichPoint> points;
            NativeArray<byte> rawPointData;
            long headerEndPos = 0;
            using (FileStream fileStream = new FileStream(plyfile, FileMode.Open, FileAccess.Read))
            {
                using (StreamReader reader = new StreamReader(fileStream, Encoding.ASCII, false, 1024, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Trim() == "end_header")
                            break;
                    }
                    headerEndPos = fileStream.Position;
                }

                fileStream.Position = headerEndPos;
                using (BinaryReader binReader = new BinaryReader(fileStream))
                {
                    int structSize = UnsafeUtility.SizeOf<RichPoint>();
                    rawPointData = new NativeArray<byte>(count * structSize, Allocator.Temp);
                    binReader.Read(rawPointData);
                    points = rawPointData.Reinterpret<RichPoint>(1);
                }
            }

            pos = new NativeArray<uint>(count * 3, Allocator.Persistent);
            other = new NativeArray<uint>(count * 4, Allocator.Persistent);
            color = new NativeArray<float4>(count, Allocator.Persistent);
            shs = new NativeArray<uint>(count * 48, Allocator.Persistent);

            for (int k = 0; k < count; k++)
            {
                RichPoint p = points[k];

                pos[k * 3] = math.asuint(p.pos.x);
                pos[k * 3 + 1] = math.asuint(p.pos.y);
                pos[k * 3 + 2] = math.asuint(p.pos.z);

                float4 rotQ = ((Unity.Mathematics.float4)p.rot).yzwx;
                float4 rotQQ = GaussianUtils.PackSmallest3Rotation(rotQ);
                uint enc = EncodeQuatToNorm10(rotQQ);
                other[k * 4] = enc;
                float3 scale = math.exp((float3)(p.scale));
                other[k * 4 + 1] = math.asuint(scale.x);
                other[k * 4 + 2] = math.asuint(scale.y);
                other[k * 4 + 3] = math.asuint(scale.z);

                int baseIdx = k * 48;

                float3 c = (float3)(p.sh0);
                float a = GaussianUtils.Sigmoid(p.alpha);
                c = c * 0.2820948f + 0.5f;
                color[k] = new float4(c, a);

                shs[baseIdx] = math.asuint(p.sh1.x);
                shs[baseIdx + 1] = math.asuint(p.sh1.y);
                shs[baseIdx + 2] = math.asuint(p.sh1.z);
                shs[baseIdx + 3] = math.asuint(p.sh2.x);
                shs[baseIdx + 4] = math.asuint(p.sh2.y);
                shs[baseIdx + 5] = math.asuint(p.sh2.z);
                shs[baseIdx + 6] = math.asuint(p.sh3.x);
                shs[baseIdx + 7] = math.asuint(p.sh3.y);
                shs[baseIdx + 8] = math.asuint(p.sh3.z);

                for (int j = 9; j < 48; j++)
                {
                    shs[baseIdx + j] = 0;
                }
            }
            rawPointData.Dispose();
            
            return count;
        }

        static uint EncodeQuatToNorm10(float4 v) // 32 bits: 10.10.10.2
        {
            return (uint) (v.x * 1023.5f) | ((uint) (v.y * 1023.5f) << 10) | ((uint) (v.z * 1023.5f) << 20) | ((uint) (v.w * 3.5f) << 30);
        }

    }
}