using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenTypeFonts
{
    internal class GlyphData
    {
        public bool Needed { get; set; }
        public bool CompositeTT { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
        public short Advance { get; set; }
        public short Lsb { get; set; }
        public int NamesIdCFF { get; set; }
        public int NewIndex { get; set; }

    }
}
