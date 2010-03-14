using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenTypeFonts
{
    internal class EncodingSegment
    {
        public int Start { get; set; }
        public int End { get; set; }
        public short[] GlyphIds { get; set; }
        public bool ConstDelta { get; set; }
        public int GlyphsBefore { get; set; }
    }
}
