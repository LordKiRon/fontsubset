using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenTypeFonts
{
    internal class TableDirectoryEntry
    {
        public string Identifier { get; set;}
        public int CheckSum { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
        public bool Needed { get; set; }
        public byte[] NewContent { get; set; }
        public int NewRelativeOffset { get; set; }
    }
}
