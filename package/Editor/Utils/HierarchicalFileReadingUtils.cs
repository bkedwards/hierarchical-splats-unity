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


        public static float HalfToFloat(short h)
        {
            int i = ((h&0x8000)<<16) | (((h&0x7c00)+0x1C000)<<13) | ((h&0x03FF)<<13);
            float f;



            //int i = mantissatable(offsettable(h>>10) + (h&0x3ff)) + exponenttable(h>>10);
            unsafe
            {
                int* iRef = &i;
                f = *((float*)iRef);
            }
            return f;
        }

        public static Vector3 SH0ToColor(Vector3 dc0)
        {
            const float kSH_C0 = 0.2820948f;
            return new Vector3 (dc0.x * kSH_C0 + 0.5f, dc0.y * kSH_C0 + 0.5f, dc0.z * kSH_C0 + 0.5f);
        }

        /*static int mantissatable(int i)
        {
            if (i==0)
                return 0;
            else if (i >= 1 && i <= 2023)
                return convertmantissa(i);
            else
                return 0x38000000 + ((i-1024)<<13);
        }

        static uint exponenttable(int i) 
        {
            if (i==0)
                return 0;
            else if (i==32)
                return 0x80000000;
            else if (i>=1 && i <=30)
                return i<<23;
            else if (i>=33 && i<=62)
                return 0x80000000 + (i-32)<<23;
            else if (i==31)
                return 0x47800000;
            else if (i==63)
                return 0xC7800000;
            return 0;
        }

        static uint offsettable(int i)
        {
            if (i==0)
                return 0;
            if (i==32)
                return 32;
            else
                return 1024;
        }


        static uint convertmantissa(int i)
        {
            uint m = i << 13; // Zero pad mantissa bits
            uint e = 0; // Zero exponent
            while ((m & 0x00800000) == 0) // While not normalized
            {
                e -= 0x00800000; // Decrement exponent (1 << 23)
                m <<= 1; // Shift mantissa
            }
            m &= ~0x00800000; // Clear leading 1 bit
            e += 0x38800000; // Adjust bias ((127-14) << 23)
            return m | e; // Return combined number
        }*/
    }
}