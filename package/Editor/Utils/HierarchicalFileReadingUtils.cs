using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

namespace HierarchicalSplatting.Editor.Utils
{

    public static class HierarchicalFileReadingUtils {


        public static float HalfToFloat(short half)
        {
            int sign = (half >> 15) & 0x1;
            int exponent = (half >> 10) & 0x1F;
            int mantissa = half & 0x3FF;

            if (exponent == 0)
            {
                if (mantissa == 0)
                    return sign == 0 ? 0f : -0f; 
                else
                {
                    return sign == 0 ? (mantissa / 1024f) : -(mantissa / 1024f);
                }
            }
            else if (exponent == 31)
            {
                if (mantissa == 0)
                    return sign == 0 ? float.PositiveInfinity : float.NegativeInfinity;
                else
                    return float.NaN; // Handle NaN
            }
            // Normalized value
            float normalizedValue = (mantissa / 1024f) * (1 << (exponent - 15));
            return sign == 0 ? normalizedValue : -normalizedValue;
        }
        /*public static void ReadRichPointArray(BinaryReader reader, NativeArray<RichPoint> array, int count) {
            RichPoint point = new RichPoint();
            for (int i = 0; i<count; i++) {
                float[] pos = new float[3];
                float[] n = new float[3];
                float[] shs = new float[12];
                float alpha;
                float[] scale = new float[3];
                float[] rot = new float[4];
                for (int j = 0; j<3; j++) {
                    pos[j] = reader.ReadSingle();
                }
                for (int j = 0; j<3; j++) {
                    n[j] = reader.ReadSingle();
                }
                for (int j = 0; j<12; j++) {
                    shs[j] = reader.ReadSingle();
                }
                alpha = reader.ReadSingle();
                for (int j = 0; j<3; j++) {
                    scale[j] = reader.ReadSingle();
                }
                for (int j = 0; j<4; j++) {
                    rot[j] = reader.ReadSingle();
                }
                array[i] = new RichPoint(pos, n, shs, alpha, scale, rot, Allocator.Temp);
            }
        }*/
    } 
}