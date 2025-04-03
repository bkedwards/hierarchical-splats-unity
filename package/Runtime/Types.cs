using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace HierarchicalSplatting
{
    public struct SHs
    {
        public Vector3 dc0;
        public Vector3 sh1, sh2, sh3, sh4, sh5, sh6, sh7, sh8, sh9, shA, shB, shC, shD, shE, shF;
    }

    public struct Node   // install/include/types.h
    {   
        public int depth;
        public int parent;
        public int start;
        public int count_leafs;
        public int count_merged;
        public int start_children;
        public int count_children;

        public Node (int depth, int parent, int start, int count_leafs, int count_merged, int start_children, int count_children) {
            this.depth = depth; //default value is -1 in original code
            this.parent = parent; //default value is -1 in original code
            this.start = start;
            this.count_leafs = count_leafs;
            this.count_merged = count_merged;
            this.start_children = start_children;
            this.count_children = count_children;
        }
    }

    public struct HalfNode
    {
        public int parent;
        public int start;
        public int start_children;
        public short dccc0;
        public short dccc1;
        public short dccc2;
        public short dccc3;

        public HalfNode(int parent, int start, int start_children, short dccc0, short dccc1, short dccc2, short dccc3)
        {
            this.parent = parent;
            this.start = start;
            this.start_children = start_children;
            this.dccc0 = dccc0;
            this.dccc1 = dccc1;
            this.dccc2 = dccc2;
            this.dccc3 = dccc3;
        }

    }
    public struct Box // install/include/types.h
    {
        public Vector4 minn;
        public Vector4 maxx;

        // Parameterized constructor
        public Box(Vector4 minn, Vector4 maxx)
        {
            this.minn = minn;
            this.maxx = maxx;
        }
    }

    public struct HalfBox
    {
        public short minn0;
        public short minn1;
        public short minn2;
        public short minn3;
        public short maxx0;
        public short maxx1;
        public short maxx2;
        public short maxx3;

    }

    public struct RichPoint
    {
        public Vector3 pos;
        public Vector3 n;
        public Vector3 sh0, sh1, sh2, sh3;
        public float alpha;
        public Vector3 scale;
        public Vector4 rot;

    }

    public class MemSet 
    {
        public GraphicsBuffer posBuff;
        public GraphicsBuffer scalesBuff;
        public GraphicsBuffer rotsBuff;
        public GraphicsBuffer alphasBuff;
        public GraphicsBuffer shsBuff;
        public GraphicsBuffer boxesBuff;
        public GraphicsBuffer nodesBuff;

        public MemSet(int size_pos, int size_nodes)
        {
            posBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size_pos, 3 * sizeof(float));
            scalesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size_pos, 3 * sizeof(float)); 
            rotsBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size_pos, 4 * sizeof(float));
            alphasBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size_pos, sizeof(float)); 
            shsBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size_pos, 48 * sizeof(float)); 
            boxesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size_nodes, 8 * sizeof(float));
            nodesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size_nodes, 7 * sizeof(int)); 
        }

        public void Release()
        {
            posBuff.Dispose();
            scalesBuff.Dispose();
            rotsBuff.Dispose();
            alphasBuff.Dispose();
            shsBuff.Dispose();
            boxesBuff.Dispose();
            nodesBuff.Dispose();
        }

    }

    public class LightSet
    {
        public int toRender;
        public GraphicsBuffer renderIndicesBuff;
        public GraphicsBuffer parentIndicesBuff;
        public GraphicsBuffer nodesOfRenderIndicesBuff;

        public LightSet(int size)
        {
            toRender = 0;
            renderIndicesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size, sizeof(int)); 
            parentIndicesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size, sizeof(int));
            nodesOfRenderIndicesBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size, sizeof(int)); 
        }

        public void Release()
        {
            renderIndicesBuff.Dispose();
            parentIndicesBuff.Dispose();
            nodesOfRenderIndicesBuff.Dispose();
        }


    }


}