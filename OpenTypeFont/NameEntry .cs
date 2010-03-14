using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenTypeFonts
{
    internal class NameEntry
    {
        public short PlatformId { get; set; }
        public short EncodingId { get; set; }
        public short LanguageId { get; set; }
        public short NameId { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
        public bool Needed { get; set; }
        public byte[] NewContent { get; set; }
        public int NewRelativeOffset { get; set; }
    }
}
