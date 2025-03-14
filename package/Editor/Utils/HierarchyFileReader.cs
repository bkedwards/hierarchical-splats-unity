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
    public class HierarchyFileReader {

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
            out NativeArray<Vector3> pos,  
            out NativeArray<SHs> shs, 
            out NativeArray<float> alphas, 
            out NativeArray<Vector3> scales, 
            out NativeArray<Vector4> rot, 
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

            const float kSH_C0 = 0.2820948f;

            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read);
            if (fs.Length == 0)
                throw new FileNotFoundException("File not found or is empty!");

            byte[] buffer = new byte[4];
            fs.Read(buffer, 0, 4);
            int P = BitConverter.ToInt32(buffer, 0);
            //Debug.Log($"P: {P}");

            if (P >= 0) 
            {
                rawPosData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<Vector3>(), Allocator.Persistent);
                fs.Read(rawPosData);
                pos = rawPosData.Reinterpret<Vector3>(1);
                
                rawRotData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<Vector4>(), Allocator.Persistent);
                fs.Read(rawRotData);
                rot = rawRotData.Reinterpret<Vector4>(1);

                rawScaleData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<Vector3>(), Allocator.Persistent);
                fs.Read(rawScaleData);
                scales = rawScaleData.Reinterpret<Vector3>(1);

                rawAlphaData = new NativeArray<byte>(P * sizeof(float), Allocator.Persistent);
                fs.Read(rawAlphaData);
                alphas = rawAlphaData.Reinterpret<float>(1);

                rawSHData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<SHs>(), Allocator.Persistent);
                fs.Read(rawSHData);
                shs = rawSHData.Reinterpret<SHs>(1);
                // To Do: reorder SHs / scale color data

                fs.Read(buffer, 0, 4);
                int N = BitConverter.ToInt32(buffer, 0);

                rawNodeData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<Node>(), Allocator.Persistent);
                fs.Read(rawNodeData);
                nodes = rawNodeData.Reinterpret<Node>(1);
  

                rawBoxData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<Box>(), Allocator.Persistent);
                fs.Read(rawBoxData);
                boxes = rawBoxData.Reinterpret<Box>(1);

            }
            else 
            {
                P = -P;
                int halfSize = UnsafeUtility.SizeOf<short>(); // 2 bytes

                rawPosData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<Vector3>(), Allocator.Persistent);
                fs.Read(rawPosData);
                pos = rawPosData.Reinterpret<Vector3>(1);

                rawRotData = new NativeArray<byte>(P * 4 * halfSize, Allocator.Temp);
                fs.Read(rawRotData);
                NativeArray<short> halfrot = rawRotData.Reinterpret<short>(1);
                
                rot = new NativeArray<Vector4>(P, Allocator.Persistent);
                for (int i = 0; i< P; i++)
                    rot[i] = new Vector4(
                        HalfToFloat(halfrot[i*4]),
                        HalfToFloat(halfrot[i*4 + 1]),
                        HalfToFloat(halfrot[i*4 + 2]),
                        HalfToFloat(halfrot[i*4 + 3])
                    );
                rawRotData.Dispose();

                rawScaleData = new NativeArray<byte>(P * 3 * halfSize, Allocator.Persistent);
                fs.Read(rawScaleData);
                NativeArray<short> halfscales = rawScaleData.Reinterpret<short>(1);

                scales = new NativeArray<Vector3>(P, Allocator.Persistent);
                for (int i = 0; i< P; i++)
                    scales[i] = new Vector3(
                        HalfToFloat(halfscales[i*3]),
                        HalfToFloat(halfscales[i*3 + 1]),
                        HalfToFloat(halfscales[i*3 + 2])
                    );

                rawScaleData.Dispose();

                rawAlphaData = new NativeArray<byte>(P * halfSize, Allocator.Persistent);
                fs.Read(rawAlphaData);
                NativeArray<short> halfalphas = rawAlphaData.Reinterpret<short>(1);

                alphas = new NativeArray<float>(P, Allocator.Persistent);
                for (int i = 0; i < P; i++)
                    alphas[i] = HalfToFloat(halfalphas[i]);

                rawAlphaData.Dispose();
                
                rawSHData = new NativeArray<byte>(P * 48 * halfSize, Allocator.Persistent);
                fs.Read(rawSHData);
                NativeArray<short> halfSHs = rawSHData.Reinterpret<short>(1);

                shs = new NativeArray<SHs>(P, Allocator.Persistent);
                for (int i = 0; i < P; i++) 
                {
                    /*int baseIdx = i * 48;
                    SHs sh = new SHs();
                    sh.dc0 = new Vector3(HalfToFloat(halfshs[baseIdx])* kSH_C0 + 0.5f, HalfToFloat(halfshs[baseIdx + 1])* kSH_C0 + 0.5f, HalfToFloat(halfshs[baseIdx + 2])* kSH_C0 + 0.5f);

                    for (int j = 0; j < 15; j++) 
                    {
                        int idx1 = baseIdx + (j + 3);
                        int idx2 = baseIdx + (j + 18);
                        int idx3 = baseIdx + (j + 33);

                        float f1 = HalfToFloat(halfshs[idx1]);
                        float f2 = HalfToFloat(halfshs[idx2]);
                        float f3 = HalfToFloat(halfshs[idx3]);
                        switch (j)
                        {
                            case 0: sh.sh1 = new Vector3(f1, f2, f3); break;
                            case 1: sh.sh2 = new Vector3(f1, f2, f3); break;
                            case 2: sh.sh3 = new Vector3(f1, f2, f3); break;
                            case 3: sh.sh4 = new Vector3(f1, f2, f3); break;
                            case 4: sh.sh5 = new Vector3(f1, f2, f3); break;
                            case 5: sh.sh6 = new Vector3(f1, f2, f3); break;
                            case 6: sh.sh7 = new Vector3(f1, f2, f3); break;
                            case 7: sh.sh8 = new Vector3(f1, f2, f3); break;
                            case 8: sh.sh9 = new Vector3(f1, f2, f3); break;
                            case 9: sh.shA = new Vector3(f1, f2, f3); break;
                            case 10: sh.shB = new Vector3(f1, f2, f3); break;
                            case 11: sh.shC = new Vector3(f1, f2, f3); break;
                            case 12: sh.shD = new Vector3(f1, f2, f3); break;
                            case 13: sh.shE = new Vector3(f1, f2, f3); break;
                            case 14: sh.shF = new Vector3(f1, f2, f3); break;
                        }
                    }
                    shs[i] = sh;*/

                    int baseIdx = i * 48;
                    SHs sh = new SHs
                    {
                        dc0 = new Vector3(
                            HalfToFloat(halfSHs[baseIdx]) * kSH_C0 + 0.5f,
                            HalfToFloat(halfSHs[baseIdx + 1]) * kSH_C0 + 0.5f,
                            HalfToFloat(halfSHs[baseIdx + 2]) * kSH_C0 + 0.5f)
                    };

                    Vector3[] shValues = new Vector3[15];
                    for (int j = 0; j < 15; j++)
                    {
                        shValues[j] = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + j + 3]),
                            HalfToFloat(halfSHs[baseIdx + j + 18]),
                            HalfToFloat(halfSHs[baseIdx + j + 33])
                        );
                    }

                    unsafe
                    {

                        fixed (Vector3* src = shValues)
                        {
                            Vector3* dest = &sh.sh1;

                            UnsafeUtility.MemCpy(dest, src, sizeof(Vector3) * 15);
                        }
                    }

                    shs[i] = sh;
                }

                //halfSHs.Dispose();    

                Debug.Log($"CreateAsset::SH.Length: {shs.Length}");

                rawSHData.Dispose();

                fs.Read(buffer,0, 4);
                int N = BitConverter.ToInt32(buffer, 0);

                rawBoxData = new NativeArray<byte>(N * UnsafeUtility.SizeOf<HalfBox>(), Allocator.Persistent);
                fs.Read(rawBoxData);
                NativeArray<HalfBox> halfboxes = rawBoxData.Reinterpret<HalfBox>(1);

                rawNodeData = new NativeArray<byte>(N * UnsafeUtility.SizeOf<HalfNode>(), Allocator.Persistent);
                fs.Read(rawNodeData);
                NativeArray<HalfNode> halfnodes = rawNodeData.Reinterpret<HalfNode>(1);

                boxes = new NativeArray<Box>(N, Allocator.Persistent);
                nodes = new NativeArray<Node>(N, Allocator.Persistent);

                for (int i = 0; i < N; i++) {
                    nodes[i] = new Node(halfnodes[i].dccc0,halfnodes[i].parent, halfnodes[i].start, halfnodes[i].dccc2,halfnodes[i].dccc3, halfnodes[i].start_children, halfnodes[i].dccc1);
                        
                    Vector4 minn_vec = new Vector4(HalfToFloat(halfboxes[i].minn0), HalfToFloat(halfboxes[i].minn1), HalfToFloat(halfboxes[i].minn2), HalfToFloat(halfboxes[i].minn3));
                    Vector4 maxx_vec = new Vector4(HalfToFloat(halfboxes[i].maxx0), HalfToFloat(halfboxes[i].maxx1), HalfToFloat(halfboxes[i].maxx2), HalfToFloat(halfboxes[i].maxx3));
                    boxes[i] = new Box(minn_vec, maxx_vec);
                }

                rawBoxData.Dispose();
                rawNodeData.Dispose();

                if (debug)
                {
                    /*Debug.Log($"P: {P}");

                    float s1 = rawPosData.Length / (1024f * 1024f * 1024f);
                    float s2 = rawRotData.Length / (1024f * 1024f * 1024f);
                    float s3 = rawScaleData.Length / (1024f * 1024f * 1024f);
                    float s4 = rawAlphaData.Length / (1024f * 1024f * 1024f);
                    float s5 = rawSHData.Length / (1024f * 1024f * 1024f);
                    float s6 = rawNodeData.Length / (1024f * 1024f * 1024f);
                    float s7 = rawBoxData.Length / (1024f * 1024f * 1024f);

                    Debug.Log($"rawPosData.Length: {s1} GB");

                    Debug.Log($"rawRotData.Length: {s2} GB");

                    Debug.Log($"rawScaleData.Length: {s3} GB");

                    Debug.Log($"rawAlphaData.Length: {s4} GB");

                    Debug.Log($"rawSHData.Length: {s5} GB");

                    Debug.Log($"rawNodeData.Length: {s6} GB");

                    Debug.Log($"rawBoxData.Length: {s7} GB");

                    Debug.Log($"Total Memory: {s1 + s2 + s3 + s4 + s5 + s6 + s7} GB");

                    Debug.Log($"N: {N}");*/
                }
            }
        }        
        
        
        public static int LoadHierarchy(
            string filename,  
            out NativeArray<Vector3> pos,  
            out NativeArray<SHs> shs,  
            out NativeArray<float> alphas,  
            out NativeArray<Vector3> scales,  
            out NativeArray<Vector4> rot,  
            out NativeArray<Node> nodes,  
            out NativeArray<Box> boxes) 
        {
            
            Load(filename, out pos, out shs, out alphas, out scales, out rot, out nodes, out boxes, false);
            
            int P = pos.Length;

            LinearizeData(ref rot, ref scales, ref alphas);

            return P;
        }
        
        public static int loadScaffold(
            string filename, 
            out NativeArray<Vector3> pos, 
            out NativeArray<SHs> shs, 
            out NativeArray<float> alphas, 
            out NativeArray<Vector3> scales, 
            out NativeArray<Vector4> rot) 
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
            using (FileStream fileStream = new FileStream(plyfile, FileMode.Open, FileAccess.Read))
            using (StreamReader reader = new StreamReader(fileStream, Encoding.ASCII)) // Read text header
            {
                string buff;
                reader.ReadLine();
                reader.ReadLine();
                buff = reader.ReadLine();
                if (buff == null)
                    throw new Exception("Unexpected EOF in: " + plyfile);

                string[] parts = buff.Split(' ');
                if (parts.Length < 3)
                    throw new Exception("Invalid header format: " + plyfile);

                int noerp = int.Parse(parts[2]); 

                while ((buff = reader.ReadLine()) != null)
                {
                    if (buff.Trim() == "end_header")
                        break;
                }

                fileStream.Position = fileStream.Seek(0, SeekOrigin.Current);

                using (BinaryReader binReader = new BinaryReader(fileStream))
                {
                    rawPointData = new NativeArray<byte>(count * 104, Allocator.Temp);

                    binReader.Read(rawPointData);

                    points = rawPointData.Reinterpret<RichPoint>(1);
                
                }
            }

            pos = new NativeArray<Vector3>(count, Allocator.Persistent);
            shs = new NativeArray<SHs>(count, Allocator.Persistent);
            alphas = new NativeArray<float>(count, Allocator.Persistent);
            scales = new NativeArray<Vector3>(count, Allocator.Persistent);
            rot = new NativeArray<Vector4>(count, Allocator.Persistent);

            for (int k = 0; k < count; k++)
            {
                RichPoint p = points[k];

                pos[k] = p.pos;
                rot[k] = p.rot;

                Vector3 v = p.scale;
                scales[k] = new Vector3(
                    Mathf.Exp(v.x),
                    Mathf.Exp(v.y),
                    Mathf.Exp(v.z)
                );
                alphas[k] = GaussianUtils.Sigmoid(p.alpha);

                SHs sh = new SHs();

                sh.dc0 = p.sh0;
                sh.sh1 = p.sh1;
                sh.sh2 = p.sh2;
                sh.sh3 = p.sh3;

                sh.sh4 = Vector3.zero;
                sh.sh5 = Vector3.zero;
                sh.sh6 = Vector3.zero;
                sh.sh7 = Vector3.zero;
                sh.sh8 = Vector3.zero;
                sh.sh9 = Vector3.zero;
                sh.shA = Vector3.zero;
                sh.shB = Vector3.zero;
                sh.shC = Vector3.zero;
                sh.shD = Vector3.zero;
                sh.shE = Vector3.zero;
                sh.shF = Vector3.zero;

                shs[k] = sh;
            }
            rawPointData.Dispose();
            
            return pos.Length;
        }

        [BurstCompile]
        struct LinearizeDataJob : IJobParallelFor
        {
            public NativeArray<Vector4> rot;
            public NativeArray<Vector3> scales;
            public NativeArray<float> alphas;
            
            public void Execute(int index)
            {
                //rot
                var q = rot[index];
                var qq = GaussianUtils.NormalizeSwizzleRotation(new float4(q.x, q.y, q.z, q.w));
                qq = GaussianUtils.PackSmallest3Rotation(qq);
                rot[index] = new Vector4(qq.x, qq.y, qq.z, qq.w);

                // scale
                scales[index] = GaussianUtils.LinearScale(scales[index]);
                
                //opacities
                alphas[index] = GaussianUtils.Sigmoid(alphas[index]);

            }
        }

        static void LinearizeData(ref NativeArray<Vector4> rot, ref NativeArray<Vector3> scales, ref NativeArray<float> alphas)
        {
            LinearizeDataJob job = new LinearizeDataJob();
            job.rot = rot;
            job.scales = scales;
            job.alphas = alphas;
            job.Schedule(rot.Length, 4096).Complete();
        }

    }
}