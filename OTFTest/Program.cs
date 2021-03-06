﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenTypeFonts;

namespace OTFTest
{
    class Program
    {
        static void Main(string[] args)
        {
            //using (Stream stream = File.OpenRead("test.otf")) 
            using (Stream stream = File.OpenRead("ariblk.ttf")) 
            //using (Stream stream = File.OpenRead("infont.otf"))
            {
                OpenTypeFont font = new OpenTypeFont(stream,false);
                font.Play("Hello!");
                byte[] fontData = font.GetSubsettedFont();
                using ( FileStream outStream = File.Create("outfont.ttf"))
                {
                    outStream.Write(fontData,0,fontData.Length);
                }
            }
        }
    }
}
