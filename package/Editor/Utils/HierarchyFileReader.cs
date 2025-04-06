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
            out Vector3[] pos,  
            out SHs[] shs, 
            out float[] alphas, 
            out Vector3[] scales, 
            out Vector4[] rot, 
            out Node[] nodes, 
            out Box[] boxes, 
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
                rawPosData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<Vector3>(), Allocator.Persistent);
                fs.Read(rawPosData);
                var VectorPosData = rawPosData.Reinterpret<Vector3>(1);
                pos = new Vector3[P];
                for (int i = 0; i < P; i++) {
                    pos[i] = VectorPosData[i];
                }
                
                rawRotData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<Vector4>(), Allocator.Persistent);
                fs.Read(rawRotData);
                var VectorRotData = rawRotData.Reinterpret<Vector4>(1);
                rot = new Vector4[P];
                for (int i = 0; i < P; i++) {
                    rot[i] = VectorRotData[i];
                }

                rawScaleData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<Vector3>(), Allocator.Persistent);
                fs.Read(rawScaleData);
                var VectorScaleData = rawScaleData.Reinterpret<Vector3>(1);
                scales = new Vector3[P];
                for (int i = 0; i < P; i++) {
                    scales[i] = new Vector3(
                        Mathf.Exp(VectorScaleData[i].x),
                        Mathf.Exp(VectorScaleData[i].y),
                        Mathf.Exp(VectorScaleData[i].z)
                    );
                }

                rawAlphaData = new NativeArray<byte>(P * sizeof(float), Allocator.Persistent);
                fs.Read(rawAlphaData);
                var FloatAlphaData = rawAlphaData.Reinterpret<float>(1);
                alphas = new float[P];
                for (int i = 0; i < P; i++) {
                    alphas[i] = FloatAlphaData[i];
                }

                rawSHData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<SHs>(), Allocator.Persistent);
                fs.Read(rawSHData);
                var SHsData = rawSHData.Reinterpret<SHs>(1);
                shs = new SHs[P];
                for (int i = 0; i < P; i++)
                {
                    shs[i] = SHsData[i];
                }
                // To Do: reorder SHs / scale color data

                fs.Read(buffer, 0, 4);
                int N = BitConverter.ToInt32(buffer, 0);

                rawNodeData = new NativeArray<byte>(N * UnsafeUtility.SizeOf<Node>(), Allocator.Persistent);
                fs.Read(rawNodeData);
                var NodeData = rawNodeData.Reinterpret<Node>(1);
                nodes = new Node[N];
                for (int i = 0; i < N; i++)
                {
                    nodes[i] = NodeData[i];
                }

                rawBoxData = new NativeArray<byte>(N * UnsafeUtility.SizeOf<Box>(), Allocator.Persistent);
                fs.Read(rawBoxData);
                var BoxData = rawBoxData.Reinterpret<Box>(1);
                boxes = new Box[N];
                for (int i = 0; i < P; i++)
                {
                    boxes[i] = BoxData[i];
                }

            }
            else 
            {
                P = -P;
                int halfSize = UnsafeUtility.SizeOf<short>(); // 2 bytes
                Debug.Log("halfSize: " + halfSize.ToString());
                rawPosData = new NativeArray<byte>(P * UnsafeUtility.SizeOf<Vector3>(), Allocator.Persistent);
                fs.Read(rawPosData);
                var VectorPosData = rawPosData.Reinterpret<Vector3>(1);
                pos = new Vector3[P];
                for (int i = 0; i < P; i++) {
                    pos[i] = VectorPosData[i];
                }

                rawRotData = new NativeArray<byte>(P * 4 * halfSize, Allocator.Temp);
                fs.Read(rawRotData);
                NativeArray<short> halfrot = rawRotData.Reinterpret<short>(1);

                
                rot = new Vector4[P];
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
                scales = new Vector3[P];
                for (int i = 0; i< P; i++)
                    scales[i] = new Vector3(
                        Mathf.Exp(HalfToFloat(halfscales[i*3])),
                        Mathf.Exp(HalfToFloat(halfscales[i*3 + 1])),
                        Mathf.Exp(HalfToFloat(halfscales[i*3 + 2]))
                    );

                rawScaleData.Dispose();

                rawAlphaData = new NativeArray<byte>(P * halfSize, Allocator.Persistent);
                fs.Read(rawAlphaData);
                NativeArray<short> halfalphas = rawAlphaData.Reinterpret<short>(1);

                alphas = new float[P];
                for (int i = 0; i < P; i++)
                    alphas[i] = HalfToFloat(halfalphas[i]);

                rawAlphaData.Dispose();
                
                rawSHData = new NativeArray<byte>(P * 48 * halfSize, Allocator.Persistent);
                fs.Read(rawSHData);
                NativeArray<short> halfSHs = rawSHData.Reinterpret<short>(1);

                shs = new SHs[P];
                for (int i = 0; i < P; i++) 
                {
                    SHs sh = new SHs();
                    int baseIdx = i*48;
                    sh.dc0 = new Vector3(
                            HalfToFloat(halfSHs[baseIdx]),
                            HalfToFloat(halfSHs[baseIdx + 1]),
                            HalfToFloat(halfSHs[baseIdx + 2]));
                    /*
                    //OG READ IN (NO REORDERING) -- CONFIRMED TO WORK
                    sh.sh1 = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 3]),
                            HalfToFloat(halfSHs[baseIdx + 4]),
                            HalfToFloat(halfSHs[baseIdx + 5]));
                    sh.sh2 = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 6]),
                            HalfToFloat(halfSHs[baseIdx + 7]),
                            HalfToFloat(halfSHs[baseIdx + 8]));
                    sh.sh3 = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 9]),
                            HalfToFloat(halfSHs[baseIdx + 10]),
                            HalfToFloat(halfSHs[baseIdx + 11]));
                    sh.sh4 = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 12]),
                            HalfToFloat(halfSHs[baseIdx + 13]),
                            HalfToFloat(halfSHs[baseIdx + 14]));
                    sh.sh5 = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 15]),
                            HalfToFloat(halfSHs[baseIdx + 16]),
                            HalfToFloat(halfSHs[baseIdx + 17]));
                    sh.sh6 = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 18]),
                            HalfToFloat(halfSHs[baseIdx + 19]),
                            HalfToFloat(halfSHs[baseIdx + 20]));
                    sh.sh7 = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 21]),
                            HalfToFloat(halfSHs[baseIdx + 22]),
                            HalfToFloat(halfSHs[baseIdx + 23]));
                    sh.sh8 = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 24]),
                            HalfToFloat(halfSHs[baseIdx + 25]),
                            HalfToFloat(halfSHs[baseIdx + 26]));
                    sh.sh9 = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 27]),
                            HalfToFloat(halfSHs[baseIdx + 28]),
                            HalfToFloat(halfSHs[baseIdx + 29]));
                    sh.shA = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 30]),
                            HalfToFloat(halfSHs[baseIdx + 31]),
                            HalfToFloat(halfSHs[baseIdx + 32]));
                    sh.shB = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 33]),
                            HalfToFloat(halfSHs[baseIdx + 34]),
                            HalfToFloat(halfSHs[baseIdx + 35]));
                    sh.shC = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 36]),
                            HalfToFloat(halfSHs[baseIdx + 37]),
                            HalfToFloat(halfSHs[baseIdx + 38]));
                    sh.shD = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 39]),
                            HalfToFloat(halfSHs[baseIdx + 40]),
                            HalfToFloat(halfSHs[baseIdx + 41]));
                    sh.shE = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 42]),
                            HalfToFloat(halfSHs[baseIdx + 43]),
                            HalfToFloat(halfSHs[baseIdx + 44]));
                    sh.shF = new Vector3(
                            HalfToFloat(halfSHs[baseIdx + 45]),
                            HalfToFloat(halfSHs[baseIdx + 46]),
                            HalfToFloat(halfSHs[baseIdx + 47]));*/

                    Vector3[] reordered = new Vector3[15];
                    for (int j = 0; j < 15; j++)
                    {
                        float r = HalfToFloat(halfSHs[baseIdx + j + 3]);
                        float g = HalfToFloat(halfSHs[baseIdx + j + 18]);
                        float b = HalfToFloat(halfSHs[baseIdx + j + 33]);
                        reordered[j] = new Vector3(r, g, b);
                    }

                    // Assign reordered coefficients to fields sh1 - shF
                    sh.sh1 = reordered[0];
                    sh.sh2 = reordered[1];
                    sh.sh3 = reordered[2];
                    sh.sh4 = reordered[3];
                    sh.sh5 = reordered[4];
                    sh.sh6 = reordered[5];
                    sh.sh7 = reordered[6];
                    sh.sh8 = reordered[7];
                    sh.sh9 = reordered[8];
                    sh.shA = reordered[9];
                    sh.shB = reordered[10];
                    sh.shC = reordered[11];
                    sh.shD = reordered[12];
                    sh.shE = reordered[13];
                    sh.shF = reordered[14];
                    shs[i] = sh;

                }

                rawSHData.Dispose();

                fs.Read(buffer,0, 4);
                int N = BitConverter.ToInt32(buffer, 0);

                rawNodeData = new NativeArray<byte>(N * UnsafeUtility.SizeOf<HalfNode>(), Allocator.Persistent);
                fs.Read(rawNodeData);
                NativeArray<HalfNode> halfnodes = rawNodeData.Reinterpret<HalfNode>(1);

                rawBoxData = new NativeArray<byte>(N * UnsafeUtility.SizeOf<HalfBox>(), Allocator.Persistent);
                fs.Read(rawBoxData);
                NativeArray<HalfBox> halfboxes = rawBoxData.Reinterpret<HalfBox>(1);

                boxes = new Box[N];
                nodes = new Node[N];

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
                    Debug.Log($"P: {P}");

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

                    Debug.Log($"N: {N}");
                }
            }
        }        
        
        
        public static int LoadHierarchy(
            string filename,  
            out Vector3[] pos,  
            out SHs[] shs,  
            out float[] alphas,  
            out Vector3[] scales,  
            out Vector4[] rot,  
            out Node[] nodes,  
            out Box[] boxes) 
        {
            
            Load(filename, out pos, out shs, out alphas, out scales, out rot, out nodes, out boxes, false);
            
            int P = pos.Length;

            //LinearizeData(ref rot, ref scales, ref alphas);

            return P;
        }
        
        public static int loadScaffold(
            string filename, 
            out Vector3[] pos, 
            out SHs[] shs, 
            out float[] alphas, 
            out Vector3[] scales, 
            out Vector4[] rot) 
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

            pos = new Vector3[count];
            shs = new SHs[count];
            alphas = new float[count];
            scales = new Vector3[count];
            rot = new Vector4[count];

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

                /*
                // OG READ-IN - NO REORDERING -- NOT CONFIRMED TO WORK YET
                sh.sh1 = new Vector(p.sh1[0], p.sh2[0], p.sh3[0]);
                sh.sh6 = new Vector(p.sh1[1], p.sh2[1], p.sh3[1]);
                sh.shB = new Vector(p.sh1[2], p.sh2[2], p.sh3[2]);

                sh.sh2 = Vector3.zero;
                sh.sh3 = Vector3.zero;
                sh.sh4 = Vector3.zero;
                sh.sh5 = Vector3.zero;
                sh.sh7 = Vector3.zero;
                sh.sh8 = Vector3.zero;
                sh.sh9 = Vector3.zero;
                sh.shA = Vector3.zero;
                sh.shC = Vector3.zero;
                sh.shD = Vector3.zero;
                sh.shE = Vector3.zero;
                sh.shF = Vector3.zero;
                */

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