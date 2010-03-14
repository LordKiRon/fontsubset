using System;
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
            using (Stream stream = File.OpenRead("AdobeHeitiStd-Regular.otf")) 
            //using (Stream stream = File.OpenRead("infont.ttf"))
            {
                OpenTypeFont font = new OpenTypeFont(stream,false);
                font.Play("Hello!");
                byte[] fontData = font.GetSubsettedFont();
                using ( FileStream outStream = File.Create("outfont.otf"))
                {
                    outStream.Write(fontData,0,fontData.Length);
                }
            }
        }
    }
}
