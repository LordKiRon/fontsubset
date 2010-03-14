using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTypeFonts
{
    static class IOUtils
    {
        public static int GetInt(byte[] buf, int offset)
        {
            return (buf[offset] << 24) | ((buf[offset + 1] & 0xFF) << 16) | ((buf[offset + 2] & 0xFF) << 8) | (buf[offset + 3] & 0xFF);
        }

        public static short GetShort(byte[] buf, int offset)
        {
            return (short)(((buf[offset] & 0xFF) << 8) | (buf[offset + 1] & 0xFF));
        }

        public static void WriteShort(Stream outStream,int n)
        {
            outStream.WriteByte((byte)(n>>8));
            outStream.WriteByte((byte)n);
        }

        public static void WriteInt(Stream outStream,int n)
        {
            outStream.WriteByte((byte)(n >> 24));
            outStream.WriteByte((byte)(n >> 16));
            outStream.WriteByte((byte)(n >> 8));
            outStream.WriteByte((byte)n);           
        }

        public static void StuffShort(byte[] arr, int index, short n)
        {
            arr[index] = (byte)(n >> 8);
            arr[index + 1] = (byte)n;
        }

        public static void StuffLong(byte[] arr, int index, long n)
        {
            arr[index] = (byte)(n >> 56); 
            arr[index + 1] = (byte)(n >> 48); 
            arr[index + 2] = (byte)(n >> 40); 
            arr[index + 3] = (byte)(n >> 32); 
            arr[index + 4] = (byte)(n >> 24); 
            arr[index + 5] = (byte)(n >> 16); 
            arr[index + 6] = (byte)(n >> 8); 
            arr[index + 7] = (byte)n;
        } 

    }
}
