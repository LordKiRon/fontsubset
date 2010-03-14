using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenTypeFonts
{
    internal class EncodingRecord
    {
        public short PlatformID { get; set; }
        public short EncodingID { get; set; }
        public int Offset { get; set; }
        public short Format { get; set; }
        public int Length { get; set; }
        public short Language { get; set; }
    }
}
