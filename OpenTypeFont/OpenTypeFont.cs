using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace OpenTypeFonts
{
    public enum FontPropertyConstants
    {
        STYLE_REGULAR = 0,
        STYLE_ITALIC = 1,
        STYLE_OBLIQUE = 2,
        WEIGHT_NORMAL = 400,
        WEIGHT_BOLD = 700,
    }

    public enum PlatformId
    {
        Unicode = 0,
        Macintosh = 1,
        Reserved_Not_Use = 2,
        Microsoft = 3,
    }

    public class OpenTypeFont
    {
        private FileHeader Header = new FileHeader();
        private readonly List<NameEntry> names = new List<NameEntry>();
        private bool noUnicodeBigUnmarked = false;
        private readonly Dictionary<int,CharacterData> characters = new Dictionary<int, CharacterData>();
        private Dictionary<object,object> dictCFF = new Dictionary<object, object>();
        private int MaxGlyphSize;
        private List<GlyphData> glyphs;
        private string nameCFF;
        private StringCFF[] stringsCFF;
        private object[] globalSubrsCFF;
        private Dictionary<object, object> privateDictCFF;
        private object[] privateSubrsCFF;
        private int variableWidthCount;
        private Stream inputStream;
        private int NewVariableWidthCount;
        private int NewGlyphCount;
        private string FontID { get; set; }

        // Font properties (read from OS/2 section)
        public int Weight { get; set; }
        public int Width { get; set; }
        public FontPropertyConstants Style { get; set; }
        public int FsType { get; set; }
        public int SubscriptXSize { get; set; }
        public int SubscriptYSize { get; set; }
        public int SubscriptXOffset { get; set; }
        public int SubscriptYOffset { get; set; }
        public int SuperscriptXSize { get; set; }
        public int SuperscriptYSize { get; set; }
        public int SuperscriptXOffset { get; set; }
        public int SuperscriptYOffset { get; set; }
        public int StrikeoutSize { get; set; }
        public int StrikeoutPosition { get; set; }
        public int FamilyClass { get; set; }
        public int UnicodeRange1 { get; set; }
        public int UnicodeRange2 { get; set; }
        public int UnicodeRange3 { get; set; }
        public int UnicodeRange4 { get; set; }
        public int TypoAscender { get; set; }
        public int TypoDescender { get; set; }
        public int TypoLineGap { get; set; }
        public int WinAscent { get; set; }
        public int WinDescent { get; set; }
        public int CodePageRange1 { get; set; }
        public int CodePageRange2 { get; set; }
        public int Height { get; set; }
        public int CapHeight { get;set; }
        public int DefaultChar { get; set; }
        public int BreakChar { get; set; }
        public int MaxContext { get; set; }
        
        private byte[] panos = new byte[10];
        private char[] achVendID = new char[4];
        private int fsSelection;
        private int Os2version;
        private int CharRange;


        public string FamilyName { get; set; }
        public bool TrueTypeGlyphs { get; set; }


        private const int CFF_INDEX_NAMES = 0;          
        private const int CFF_INDEX_DICTS = 1;          
        private const int CFF_INDEX_BINARY = 2;          
        private const int CFF_INDEX_BINARY_RANGE = 3;

        private const int CFF_STD_STRING_COUNT = 391;

        private const long baseTime = (66 * 365 + 16) * 24 * 60 * 60;

        	// CFF dict keys with SID values
	    private readonly int[] sidIndicesCFF = { 0, 1, 2, 3, 4, 0x100, 0x100 | 21,
			0x100 | 22, 0x100 | 30, 0x100 | 38 };


        public OpenTypeFont(Stream stream,bool queryOnly)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }
            Header.Load(stream);
            ReadOs2(stream);
            ReadNames(stream);
            if (!queryOnly)
            {
                ReadCMap(stream); 
                ReadGlyphData(stream); 
                ReadMetrics(stream);
            }
            inputStream = stream;
        }

        private void ReadMetrics(Stream stream)
        {
            byte[] buffer = new byte[5];
            TableDirectoryEntry hhea = Header.TableMap["hhea"];
            stream.Seek(hhea.Offset + 34,SeekOrigin.Begin);
            stream.Read(buffer, 0, 2);
            variableWidthCount = IOUtils.GetShort(buffer, 0) & 0xFFFF;
            TableDirectoryEntry hmtx = Header.TableMap["hmtx"];
            if (hmtx.Length != variableWidthCount * 2 + glyphs.Count * 2)
            {
                throw new Exception("bad hmtx table length");
            }
            stream.Seek(hmtx.Offset, SeekOrigin.Begin);
            short advance = 0;
            for (int i = 0; i < variableWidthCount; i++)
            {
                stream.Read(buffer, 0, 4);
                advance = IOUtils.GetShort(buffer, 0); 
                glyphs[i].Advance = advance; 
                glyphs[i].Lsb = IOUtils.GetShort(buffer, 2); 
            }
            for (int i = variableWidthCount; i < glyphs.Count; i++)
            {
                stream.Read(buffer, 0, 2);
                glyphs[i].Advance = advance;
                glyphs[i].Lsb = IOUtils.GetShort(buffer, 2); 
            }
        }

        private void ReadGlyphData(Stream stream)
        {
            bool isTtf = Header.TableMap.ContainsKey("glyf");
            if (isTtf)
            {
                TrueTypeGlyphs = true;
                ReadGlyphDataTT(stream);
            }
            else
            {
                ReadCFF(stream);
            }
            ReadGlyphsWidths(stream);
        }

        private void ReadGlyphsWidths(Stream stream)
        {
            foreach (var glyph in glyphs)
            {
                stream.Seek(glyph.Offset, SeekOrigin.Begin);
                byte[] buf = new byte[10];
                stream.Read(buf,0,10);
                int min = IOUtils.GetShort(buf, 2);
                int max = IOUtils.GetShort(buf, 6);
            }
        }

        private void ReadCFF(Stream stream)
        {
            TableDirectoryEntry cff = Header.TableMap["CFF "];
            stream.Seek(cff.Offset, SeekOrigin.Begin);
            byte[] buffer = new byte[5];
            stream.Read(buffer, 0, 4);
            int offSize = buffer[3];                 
            if (offSize > 4 || offSize <= 0)
            {
                throw new Exception("invalid CFF index offSize");
            }
            stream.Seek(cff.Offset + (buffer[2] & 0xFF), SeekOrigin.Begin);
            object[] names = ReadIndexCFF(stream,CFF_INDEX_NAMES);
            if (names.Length > 1)
            {
                throw new Exception("CFF data contains multiple fonts");
            }
            nameCFF = names[0].ToString();
            Object[] dicts = ReadIndexCFF(stream,CFF_INDEX_DICTS);
            if (dicts.Length> 1)
            {
                throw new Exception("CFF data contains multiple font dicts");
            }
            dictCFF = (Dictionary<object, object>)dicts[0];
            Object[] strings = ReadIndexCFF(stream,CFF_INDEX_NAMES);
            stringsCFF = new StringCFF[strings.Length];
            for (int i = 0; i < strings.Length; i++)
            {
                StringCFF s = new StringCFF();
                stringsCFF[i] = s;
                s.Value = (String)strings[i];
            }
            globalSubrsCFF = ReadIndexCFF(stream,CFF_INDEX_BINARY_RANGE);
            int charstrings = Convert.ToInt32( dictCFF[new KeyCFF(KeyCFF.CHARSTRINGS)]);
            if (charstrings == 0)
            {
                throw new Exception("Invalid CFF data: no charstrings");
            }
            stream.Seek(cff.Offset + charstrings, SeekOrigin.Begin);
            Object[] glyphRanges = ReadIndexCFF(stream,CFF_INDEX_BINARY_RANGE);
            glyphs = new List<GlyphData>();
            for (int i = 0; i < glyphRanges.Length; i++)
            {
                Range range = (Range) glyphRanges[i];
                GlyphData glyph = new GlyphData();
                glyph.Length = range.Length;
                glyph.Offset = range.Offset;
                glyphs.Add(glyph);
            }
            int charset = Convert.ToInt32(dictCFF[new KeyCFF(KeyCFF.CHARSET)]);
            if (charset != 0)
            {
                stream.Seek(cff.Offset + charset, SeekOrigin.Begin);
                stream.Read(buffer, 0, 1);
                int format = buffer[0] & 0xFF;
                switch (format) 
                { 
                    case 2:                                 
                        ReadCharsetFormat2CFF(stream); 
                        break; 
                } 
            }


            if (dictCFF.ContainsKey(new KeyCFF(KeyCFF.PRIVATE)))
            {
                Object[] privatedict = (Object[])dictCFF[new KeyCFF(KeyCFF.PRIVATE)]; 
                int size = Convert.ToInt32(privatedict[0]);
                int offset = Convert.ToInt32(privatedict[1]);
                stream.Seek(cff.Offset + offset,SeekOrigin.Begin);
                privateDictCFF = ReadDictCFF(stream,cff.Offset + offset + size);
                stream.Seek(cff.Offset + offset + size, SeekOrigin.Begin);
                privateSubrsCFF = ReadIndexCFF(stream,CFF_INDEX_BINARY_RANGE);
            }
        }

        private void ReadCharsetFormat2CFF(Stream stream)
        {
            int glyphIndex = 0;
            byte[] buffer = new byte[5];
            while (glyphIndex < glyphs.Count)
            {
                stream.Read(buffer, 0, 4);
                int sid = IOUtils.GetShort(buffer, 0);
                int len = IOUtils.GetShort(buffer, 2);
                for (int i = 0; i <= len && glyphIndex < glyphs.Count; i++)
                {
                    glyphs[glyphIndex++].NamesIdCFF = sid++;                          
                }
            }
        }

        private object[] ReadIndexCFF(Stream stream, int kind)
        {
            byte[] buffer = new byte[256];
            stream.Read(buffer, 0, 2);
            int count = IOUtils.GetShort(buffer, 0) & 0xFFFF;
            if (count == 0)
            {
                return new object[0];
            }
            stream.Read(buffer, 0, 1);
            int offSize = buffer[0];
            if (offSize > 4 || offSize <= 0)
            {
                throw new Exception("invalid CFF index offSize");
            }
            int[] offsets = new int[count + 1];
            for (int i = 0; i <= count; i++)
            {
                stream.Read(buffer, 0, offSize);
                int off = 0;
                switch(offSize)
                {
                    case 1:
                        off = buffer[0] & 0xFF;
                        break;
                    case 2:
                        off = IOUtils.GetShort(buffer, 0) & 0xFFFF;
                        break;
                    case 3:
                        off = (IOUtils.GetInt(buffer, 0) >> 8) & 0xFFFFFF;
                        break;
                    default: // 4
                        off = IOUtils.GetInt(buffer, 0);
                        break;
                }
                offsets[i] = off;
            }
            int offset = (int)(stream.Position - 1);
            for (int i = 0; i <= count; i++)
            {
                offsets[i] += offset;
            }
            Object[] arr = new Object[count];
            stream.Seek(offsets[0], SeekOrigin.Begin);
             for (int i = 0; i < count; i++)
             {
                 switch (kind)
                 {
                     case CFF_INDEX_NAMES:
                         {
                             int len = offsets[i + 1] - offsets[i];
                             byte[] b = (len > buffer.Length ? new byte[len] : buffer);
                             stream.Read(b, 0, len);
                             int strlen = 0;
                             while (strlen < len && b[strlen] != 0)
                             {
                                 strlen++;
                             }
                             Encoding isoEncoding = Encoding.GetEncoding("iso-8859-1");
                             arr[i] = isoEncoding.GetString(b, 0, strlen);
                         }
                         break;
                     case CFF_INDEX_DICTS:
                         int last = offsets[i + 1];
                         arr[i] = ReadDictCFF(stream,last);
                         stream.Seek(last, SeekOrigin.Begin);
                         break;
                     case CFF_INDEX_BINARY:
                         {
                             int len = offsets[i + 1] - offsets[i];
                             byte[] b = new byte[len];
                             stream.Read(b, 0, len);
                             arr[i] = b;
                         }
                         break;
                     case CFF_INDEX_BINARY_RANGE:
                         {
                             int len = offsets[i + 1] - offsets[i];
                             arr[i] = new Range {Offset = offsets[i], Length = len};
                         }
                         break;
                 }
             }
            stream.Seek(offsets[count], SeekOrigin.Begin);
            return arr;
        }

        private Dictionary<object,object> ReadDictCFF(Stream stream, int last)
        {
            Dictionary<object,object> table = new Dictionary<object, object>();
            List<object> acc = new List<object>();
            while (stream.Position < last)
            {
                object value;
                while (true)
                {
                    value = ReadObjectCFF(stream);
                    if (value is KeyCFF)
                    {
                        break;
                    }
                    acc.Add(value);
                }
                object key = value;
                if ( acc.Count == 1)
                {
                    value = acc[0];
                }
                else
                {
                    value =  acc.ToArray();
                }
                acc.Clear();
                table.Add(key,value);
            }
            return table;
        }

        private object ReadObjectCFF(Stream stream)
        {
            byte[] buffer = new byte[256];
            stream.Read(buffer, 0,1);
            int b0 = buffer[0] & 0xFF;
            if (b0 == 30)
            {
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    stream.Read(buffer, 0, 1);
                    int n = buffer[0];
                    if (decodeNibbleCFF(sb, n >> 4))
                    {
                        break;
                    }
                    if (decodeNibbleCFF(sb, n))
                    {
                        break;
                    }
                }
                return double.Parse(sb.ToString());
            }
            if (b0 <= 21)
            {
                if (b0 == 12)
                {
                    stream.Read(buffer, 0, 1);
                    int b1 = buffer[0] & 0xFF;
                    return new KeyCFF(b1 | 0x100);
                }
                return new KeyCFF(b0);
            }
            if (32 <= b0 && b0 <= 246)
            {
                return (b0 - 139);
            }
            if (247 <= b0 && b0 <= 254)
            {
                stream.Read(buffer, 0, 1);
                int b1 = buffer[0] & 0xFF;
                if (b0 <= 250)
                {
                    return ((b0 - 247) * 256 + b1 + 108);
                }
                return (-(b0 - 251) * 256 - b1 - 108);
            }
            if (b0 == 28)
            {
                stream.Read(buffer, 0, 2);
                int b1 = buffer[0] & 0xFF; 
                int b2 = buffer[1] & 0xFF;
                return ((short)((b1 << 8) | b2));                  
            }
            if (b0 == 29)
            {
                stream.Read(buffer, 0, 4);
                int b1 = buffer[0] & 0xFF; 
                int b2 = buffer[1] & 0xFF; 
                int b3 = buffer[2] & 0xFF; 
                int b4 = buffer[3] & 0xFF;
                return ((b1 << 24) | (b2 << 16) | (b3 << 8) | b4);                  
            }
            throw new Exception("error reading CFF object");
        }

        private bool decodeNibbleCFF(StringBuilder sb, int nibble)
        {
            nibble = nibble & 0xF;
            switch (nibble)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                case 8:
                case 9:
                    sb.Append(nibble.ToString());
                    break;
                case 0xA:
                    sb.Append(".");
                    break;
                case 0xB:
                    sb.Append("E");
                    break;
                case 0xC:
                    sb.Append("E-");
                    break;
                case 0xE: 
                    sb.Append("-"); 
                    break;
                case 0xF: 
                    return true; // end                 
                default:
                    throw new Exception("could not read CFF float");
            }
            return false;
        }

        private void ReadGlyphDataTT(Stream stream)
        {
            int glyphCount = ReadGlyphCountTT(stream);
            int indexToLocFormat = ReadIndexToLocFormatTT(stream);
            TableDirectoryEntry glyf = Header.TableMap["glyf"];
            TableDirectoryEntry locations = Header.TableMap["loca"];
            int entrySize = (indexToLocFormat == 0 ? 2 : 4);
            if (locations.Length != (glyphCount + 1) * entrySize)
            {
                throw new Exception("bad 'loca' table size");
            }
            stream.Seek(locations.Offset, SeekOrigin.Begin);
            byte[] buf = new byte[locations.Length];
            stream.Read(buf,0, locations.Length);
            List<GlyphData> data = new List<GlyphData>();
            int offset = 0;
            for (int i = 0; i <= glyphCount; i++)
            {
                int glyphOffset = (indexToLocFormat == 0 ? (IOUtils.GetShort(buf, offset) & 0xFFFF) * 2 : IOUtils.GetInt(buf, offset)) + glyf.Offset;
                offset += entrySize;
                if (i > 0)
                {
                    int len = glyphOffset - data[i - 1].Offset;
                    if (len < 0)
                    {
                        throw new Exception("negative glyph length");
                    }
                    if (MaxGlyphSize < len)
                    {
                        MaxGlyphSize = len;
                    }
                    data[i - 1].Length = len;
                }
                if (i != glyphCount)
                {
                    GlyphData gdata = new GlyphData();
                    gdata.Offset = glyphOffset;
                    data.Add(gdata);
                } 
            }
            glyphs = data;
        }

        private int ReadIndexToLocFormatTT(Stream stream)
        {
            TableDirectoryEntry maxp = Header.TableMap["head"]; // expect size of 54
            stream.Seek(maxp.Offset + 50,SeekOrigin.Begin);
            byte[] buffer = new byte[3];
            stream.Read(buffer, 0, 2);
            return IOUtils.GetShort(buffer, 0);
        }

        private int ReadGlyphCountTT(Stream stream)
        {
            TableDirectoryEntry maxp = Header.TableMap["maxp"];
            stream.Seek(maxp.Offset + 4,SeekOrigin.Begin);
            byte[] buffer = new byte[3];
            stream.Read(buffer,0,2);
            return IOUtils.GetShort(buffer,0) & 0xFFFF;
        }

        private void ReadCMap(Stream stream)
        {
            TableDirectoryEntry cmap = Header.TableMap["cmap"];
            if (cmap == null)
            {
                throw new Exception("No cmap table found");
            }
            stream.Seek(cmap.Offset, SeekOrigin.Begin);
            byte[] buffer = new byte[9];
            stream.Read(buffer, 0, 4);
            if (IOUtils.GetShort(buffer,0)!=0)
            {
                throw new Exception("Unknown cmap version");
            }
            int encCount = IOUtils.GetShort(buffer, 2);
            List<EncodingRecord> encs = new List<EncodingRecord>();
            for (int i = 0; i < encCount; i++)
            {
                EncodingRecord er = new EncodingRecord();
                encs.Add(er);
                stream.Read(buffer, 0, 8);
                er.PlatformID = IOUtils.GetShort(buffer, 0);
                er.EncodingID = IOUtils.GetShort(buffer, 2);
                er.Offset = IOUtils.GetInt(buffer, 4) + cmap.Offset;
            }
            foreach (var er in encs)
            {
                stream.Seek(er.Offset, SeekOrigin.Begin);
                stream.Read(buffer, 0, 6);
                er.Format = IOUtils.GetShort(buffer, 0);
                er.Length = IOUtils.GetShort(buffer, 2) & 0xFFFF;
                er.Language = IOUtils.GetShort(buffer, 4);
                if (er.PlatformID == (short)PlatformId.Microsoft && er.EncodingID == 1)
                {
                    // Unicode
                    if (er.Format == 4)
                    {
                        ReadFormat4CMap(stream,er);
                    }
                }
            }
        }

        private void ReadFormat4CMap(Stream stream, EncodingRecord er)
        {
            byte[] buffer = new byte[9];
            stream.Read(buffer, 0, 8);
            int segCount = (IOUtils.GetShort(buffer, 0) & 0xFFFF) / 2; 
            short[] endCount = ReadShorts(stream,segCount);
            stream.Read(buffer, 0, 2);
            short[] startCount = ReadShorts(stream, segCount);
            short[] idDelta = ReadShorts(stream, segCount);
            short[] idRangeOffset = ReadShorts(stream, segCount);
            int lengthRemains = er.Length - 16 - 8 * segCount;
            short[] glyphIds = ReadShorts(stream,lengthRemains);
            for (int i = 0; i < segCount; i++)
            {
                int start = startCount[i] & 0xFFFF;
                int end = endCount[i] & 0xFFFF;
                short rangeOffset = (short)(idRangeOffset[i] / 2);
                short delta = idDelta[i];
                for (int ch = start; ch <= end; ch++)
                {
                    CharacterData data = new CharacterData();
                    if (rangeOffset == 0)
                    {
                        data.GlyphIndex = (short) (ch + delta);
                    }
                    else
                    {
                        int index = ch - start + rangeOffset - (segCount - i);
                        int glyphIndex = glyphIds[index];
                        if (glyphIndex != 0)
                        {
                            glyphIndex += delta;
                        }
                        data.GlyphIndex = (short) glyphIndex;
                    }
                    characters.Add(ch,data);
                }
            }
        }

        private short[] ReadShorts(Stream stream,int size)
        {
            if (size < 0 )
            {
                throw new ArgumentException("size");
            }
            byte[] buffer = new byte[256];
            short[] arr = new short[size];
            int offset = 0;
            while(size > 0)
            {
                int count = size;
                if (count > buffer.Length/2)
                {
                    count = buffer.Length/2;
                }
                stream.Read(buffer, 0, 2*count);
                for (int i = 0; i < count; i++)
                {
                    arr[offset++] = IOUtils.GetShort(buffer, 2*i);
                }
                size -= count;
            }
            return arr;
        }

        private void ReadNames(Stream stream)
        {
            TableDirectoryEntry table = Header.TableMap["name"];
            if (table == null)
            {
                throw new Exception("No name table found");
            }
            stream.Seek(table.Offset, SeekOrigin.Begin);
            byte[] buffer = new byte[256];
            stream.Read(buffer, 0, 6);
            if (IOUtils.GetShort(buffer,0)!=0)
            {
                throw new Exception("unknown cmap version");
            }
            int count = IOUtils.GetShort(buffer, 2) & 0xFFFF;
            int offset = (IOUtils.GetShort(buffer, 4) & 0xFFFF) + table.Offset; 
            names.Clear();
            for (int i = 0; i < count; i++)
            {
                NameEntry entry = new NameEntry();
                names.Add(entry);
                stream.Read(buffer, 0, 12);
                entry.PlatformId = IOUtils.GetShort(buffer, 0);
                entry.EncodingId = IOUtils.GetShort(buffer, 2);
                entry.LanguageId = IOUtils.GetShort(buffer, 4);
                entry.NameId = IOUtils.GetShort(buffer, 6);
                entry.Length = IOUtils.GetShort(buffer, 8);
                entry.Offset = (IOUtils.GetShort(buffer, 10) & 0xFFFF) + offset;
            }
            foreach (var name in names)
            {
                if ((name.NameId == 1 || name.NameId == 16) && IsUnicodeEntry(name) )
                {
                    stream.Seek(name.Offset, SeekOrigin.Begin);
                    stream.Read(buffer, 0, name.Length);
                    FamilyName = DecodeUnicode(buffer,0,name.Length);
                    if (name.NameId == 16)
                    {
                        break;
                    }
                }
            }
        }

        private string DecodeUnicode(byte[] unicode, int offset, int length)
        {
            try
            {
                if (!noUnicodeBigUnmarked)
                {
                    UnicodeEncoding encoder = new UnicodeEncoding(true,false,true);
                    return encoder.GetString(unicode, offset, length);
                }
            }
            catch (Exception)
            {
                noUnicodeBigUnmarked = true;
            }
            // just do it "by hand"     
            // TODO: change it to regular C# encoding when I know what encoding is it
            // unlike in Java it probably supported
            char[] buf = new char[length / 2];                 
            for (int i = 0; i < buf.Length; i++)
            {
                buf[i] = (char) (((unicode[2 * i] & 0xFF) << 8) | (unicode[2 * i + 1] & 0xFF));
            }                 
            return new string(buf); 
        }

        private static bool IsUnicodeEntry(NameEntry en)
        {
            if (en.PlatformId == (short)PlatformId.Unicode)
            {
                return true;
            }
            if (en.PlatformId == (short)PlatformId.Microsoft && (en.EncodingId == 1 || en.EncodingId == 10) &&
                ((en.LanguageId & 0x3FF) == 9))
            {
                return true;
            }
            return false;
        }

        private void ReadOs2(Stream stream)
        {
            TableDirectoryEntry os2 = Header.TableMap["OS/2"];
            if (os2 == null)
            {
                throw new Exception("No OS/2 table found");
            }
            stream.Seek(os2.Offset, SeekOrigin.Begin);
            byte[] buffer = new byte[30];
            stream.Read(buffer, 0, 2);
            Os2version = IOUtils.GetShort(buffer,0);

            stream.Read(buffer, 0,30);
            //int currAverage = IOUtils.GetShort(buffer, 0); // not needed in future
            Weight = IOUtils.GetShort(buffer, 2);
            Width = IOUtils.GetShort(buffer, 4);
            FsType = IOUtils.GetShort(buffer, 6);
            SubscriptXSize = IOUtils.GetShort(buffer, 8);
            SubscriptYSize = IOUtils.GetShort(buffer, 10);
            SubscriptXOffset = IOUtils.GetShort(buffer, 12);
            SubscriptYOffset = IOUtils.GetShort(buffer, 14);
            SuperscriptXSize = IOUtils.GetShort(buffer, 16);
            SuperscriptYSize = IOUtils.GetShort(buffer, 18);
            SuperscriptXOffset = IOUtils.GetShort(buffer, 20);
            SuperscriptYOffset = IOUtils.GetShort(buffer, 22);
            StrikeoutSize = IOUtils.GetShort(buffer, 24);
            StrikeoutPosition = IOUtils.GetShort(buffer, 26);
            FamilyClass = IOUtils.GetShort(buffer, 28);

            stream.Read(panos, 0, 10);

            if (Os2version == 0)
            {
                stream.Read(buffer, 0, 4); // ulCharRange - not really defined, ok to set all to 0 (4 bytes)
                CharRange = IOUtils.GetInt(buffer, 0);
            }
            else
            {
                stream.Read(buffer, 0, 16);
                UnicodeRange1 = IOUtils.GetInt(buffer, 0);
                UnicodeRange2 = IOUtils.GetInt(buffer, 4);
                UnicodeRange3 = IOUtils.GetInt(buffer, 8);
                UnicodeRange4 = IOUtils.GetInt(buffer, 12);
            }

            stream.Read(buffer, 0, 4);
            Encoding isoEncoding = Encoding.GetEncoding("iso-8859-1");
            achVendID = isoEncoding.GetChars(buffer, 0, 4);

            stream.Read(buffer, 0, 2);
            fsSelection = IOUtils.GetShort(buffer, 0);
            if ((fsSelection &1) != 0 ) // no idea why just this, but that's ported from Java
            {
                Style = FontPropertyConstants.STYLE_ITALIC;
            }

            stream.Read(buffer, 0, 4); // first and last char index, we will regenerate them, so no need to store

            stream.Read(buffer,0, 10);
            TypoAscender = IOUtils.GetShort(buffer, 0);
            TypoDescender = IOUtils.GetShort(buffer, 2);
            TypoLineGap = IOUtils.GetShort(buffer, 4);
            WinAscent = IOUtils.GetShort(buffer, 6);
            WinDescent = IOUtils.GetShort(buffer, 8);

            if (Os2version == 0) // here is OS2 version 0 ends
            {
                return;
            }

            stream.Read(buffer, 0, 8);
            CodePageRange1 = IOUtils.GetInt(buffer, 0);
            CodePageRange2 = IOUtils.GetInt(buffer, 4);

            if (Os2version == 1) // here is OS2 version 0 ends
            {
                return;
            }

            stream.Read(buffer, 0, 10);

            Height      = IOUtils.GetInt(buffer, 0);
            CapHeight   = IOUtils.GetInt(buffer, 2);
            DefaultChar = IOUtils.GetInt(buffer, 4);
            BreakChar   = IOUtils.GetInt(buffer, 6);
            MaxContext  = IOUtils.GetInt(buffer, 8);

        }

        public void Play(string text)
        {
            foreach (var ch in text)
            {
                Play(ch);
            }
        }

        private bool Play(char ch)
        {
            if (characters.ContainsKey(ch))
            {
                CharacterData chData = characters[ch];
                chData.Needed = true;
                if (chData.GlyphIndex >= 0 && chData.GlyphIndex < glyphs.Count)
                {
                    GlyphData glyph = glyphs[chData.GlyphIndex];
                    glyph.Needed = true;
                    return true;                    
                }
            }
            return false;
        }

        private short CalculateAverageWeight()
        {
            int averageWeight = 0;
            int count = 1;
            foreach (var glyph in glyphs)
            {
                if (glyph.Needed)
                {
                    averageWeight += glyph.Advance;
                    if (glyph.Advance != 0)
                    {
                        count++;
                    }
                }
            }
            return (short)(averageWeight/count);
        }


        public byte[] GetSubsettedFont()
        {
            if (!CanSubset())
            {
                // we do nothing right now but have to warn user or/and disallow
            }
            if (TrueTypeGlyphs)
            {
                ResolveCompositeGlyphsTT();
            }
            ReindexGlyphs(); 
            SweepTables(); 
            return WriteTables(); 
        }

        private byte[] WriteTables()
        {
            MemoryStream outStream = new MemoryStream();
            int numTables = 0;
            foreach (var entry in Header.TableDirectory)
            {
                if (entry.Needed)
                {
                    numTables++;
                }
            }
            // file header
            if (TrueTypeGlyphs)
            {
                IOUtils.WriteInt(outStream, 0x10000);    
            }
            else
            {
                outStream.WriteByte((byte)'O');
                outStream.WriteByte((byte)'T');
                outStream.WriteByte((byte)'T');
                outStream.WriteByte((byte)'O');
            }
            IOUtils.WriteShort(outStream,(short)numTables);
            int log = floorPowerOf2(numTables);
            int entrySelector = log;
            int searchRange = 1 << (log + 4);
            int rangeShift = numTables*16 - searchRange;
            IOUtils.WriteShort(outStream,(short)searchRange);
            IOUtils.WriteShort(outStream,(short)entrySelector);
            IOUtils.WriteShort(outStream,(short)rangeShift);

            // table entries
            int baseOffset = numTables * 16 + 12;
            foreach (var entry in Header.TableDirectory)
            {
                if (entry.Needed)
                {
                    Encoding isoEncoding = Encoding.GetEncoding("iso-8859-1");
                    byte[] tag = isoEncoding.GetBytes(entry.Identifier);
                    outStream.Write(tag,0,tag.Length);
                    for (int k = tag.Length; k < 4; k++)
                    {
                        outStream.WriteByte(0);
                    }
                    if (entry.NewContent == null)
                    {
                        IOUtils.WriteInt(outStream,entry.CheckSum);
                        IOUtils.WriteInt(outStream,entry.NewRelativeOffset + baseOffset);
                        IOUtils.WriteInt(outStream,entry.Length);
                    }
                    else
                    {
                        int checkSum = CalculateTableCheckSum(entry.NewContent);
                        IOUtils.WriteInt(outStream, checkSum);
                        IOUtils.WriteInt(outStream, entry.NewRelativeOffset + baseOffset);
                        IOUtils.WriteInt(outStream, entry.NewContent.Length);                        
                    }
                }   
            }

            // table content
            foreach (var entry in Header.TableDirectory)
            {
                if (entry.Needed)
                {
                    if (entry.NewContent != null)
                    {
                        outStream.Write(entry.NewContent,0,entry.NewContent.Length);
                        int len = entry.NewContent.Length;
                        int padCount = (4 - len) & 3;
                        while (padCount > 0)
                        {
                            outStream.WriteByte(0);
                            padCount--;
                        }
                    }
                    else
                    {
                        CopyBytes(outStream,entry.Offset,(entry.Length + 3) & ~3);
                    }
                }
            }

            // adjust checkSumAdjustment
            TableDirectoryEntry head = Header.TableMap["head"];
            byte[] result = outStream.ToArray();
            int checkSumTotal = CalculateTableCheckSum(result);
            int checkSumAdjustment = (int)(0xB1B0AFBA - checkSumTotal);
            int index = head.NewRelativeOffset + baseOffset + 8;
            SetIntAtIndex(result, index, checkSumAdjustment);
            return result;
        }


        private void SetIntAtIndex(byte[] arr, int index, int value)
        {
            arr[index++] = (byte)(value >> 24); 
            arr[index++] = (byte)(value >> 16); 
            arr[index++] = (byte)(value >> 8); 
            arr[index++] = (byte)value; 
        }

        private int CalculateTableCheckSum(byte[] content)
        {
            int result = 0;
            int wordCount = content.Length / 4;
            for (int i = 0; i < wordCount; i++)
            {
                result += GetInt(content, i * 4);
            }
            int offset = 24;
            for (int i = wordCount * 4; i < content.Length; i++)
            {
                result += ((content[i] & 0xFF) << offset);
                offset -= 8;
            }
            return result;
        }

        private int GetInt(byte[] buf, int offset)
        {
            return (buf[offset] << 24) | ((buf[offset + 1] & 0xFF) << 16) | ((buf[offset + 2] & 0xFF) << 8) | (buf[offset + 3] & 0xFF); 
        }

        private void SweepTables()
        {
            byte[] glyfSection; 
            byte[] cffSection;              
            if (TrueTypeGlyphs)
            {
                glyfSection = BuildGlyphsTT();
                cffSection = null;               
            }
            else
            {
                glyfSection = null; 
                cffSection = BuildCFF(); 
            }
            int offset = 0;
            for (int i = 0; i < Header.NumTables; i++)
            {
                TableDirectoryEntry entry = Header.TableDirectory[i];
                if (entry.Identifier.Equals("cmap"))
                {
                    // replace
                    entry.NewContent = BuildCMap();
                    entry.Needed = true;
                }
                else if (entry.Identifier.Equals("head"))
                {
                    entry.Needed = true;
                    entry.NewContent = BuildHead(entry, glyfSection != null && glyfSection.Length <= 0x1FFFF);
                }
                else if (entry.Identifier.Equals("hhea"))
                {
                    entry.Needed = true;
                    entry.NewContent = BuildHHea(entry);                          
                }
                else if (entry.Identifier.Equals("hmtx"))
                {
                    entry.Needed = true;
                    entry.NewContent = BuildHMtx();                          
                }
                else if (entry.Identifier.Equals("maxp")) 
                {
                    entry.Needed = true;
                    entry.NewContent = BuildMaxP(entry);                          
                }
                else if (entry.Identifier.Equals("name")) 
                {
                    // replace
                    entry.Needed = true;
                    entry.NewContent = BuildNames();
                }
                else if (entry.Identifier.Equals("OS/2")) 
                {
                    entry.NewContent = BuildOS2(entry);
                    entry.Needed = true;
                }
                else if (entry.Identifier.Equals("post")) 
                {
                    // good as is                                  
                    entry.Needed = true;                    
                }
                else if (entry.Identifier.Equals("cvt "))
                {
                    // good as is                                  
                    entry.Needed = true;
                }
                else if (entry.Identifier.Equals("fpgm"))
                {
                    // good as is                                  
                    entry.Needed = true;
                }
                else if (entry.Identifier.Equals("glyf"))
                {
                    // replace
                    entry.Needed = true;
                    entry.NewContent = glyfSection;
                }
                else if (entry.Identifier.Equals("loca"))
                {
                    // replace
                    entry.Needed = true;
                    entry.NewContent = BuildGlyphLocations(glyfSection.Length <= 0x1FFFF); ;
                }
                else if (entry.Identifier.Equals("CFF "))
                {
                    // replace
                    entry.Needed = true;
                    entry.NewContent = cffSection;
                }
                else if (entry.Identifier.Equals("prep"))
                {
                    // good as is                                  
                    entry.Needed = true;
                }
                else if (entry.Identifier.Equals("fpgm"))
                {
                    // good as is                                  
                    entry.Needed = true;
                }
                else if (entry.Identifier.Equals("gasp"))
                {
                    // good as is                                  
                    entry.Needed = true;
                }
                if (entry.Needed)
                {
                    entry.NewRelativeOffset = offset;
                    int len = (entry.NewContent != null ? entry.NewContent.Length: entry.Length); 
                    offset += ((len + 3) & ~3); 
                }
            }
        }

        private byte[] BuildOS2(TableDirectoryEntry oldOS2)
        {
            MemoryStream outStream = new MemoryStream();
            IOUtils.WriteShort(outStream, Os2version); 
            short xAvgCharWidth = CalculateAverageWeight();
            IOUtils.WriteShort(outStream,xAvgCharWidth);
            IOUtils.WriteShort(outStream,Weight);
            IOUtils.WriteShort(outStream, Width);
            IOUtils.WriteShort(outStream, FsType);
            IOUtils.WriteShort(outStream, SubscriptXSize);
            IOUtils.WriteShort(outStream, SubscriptYSize);
            IOUtils.WriteShort(outStream, SubscriptXOffset);
            IOUtils.WriteShort(outStream, SubscriptYOffset);
            IOUtils.WriteShort(outStream, SuperscriptXSize);
            IOUtils.WriteShort(outStream, SuperscriptYSize);
            IOUtils.WriteShort(outStream, SuperscriptXOffset);
            IOUtils.WriteShort(outStream, SuperscriptYOffset);
            IOUtils.WriteShort(outStream, StrikeoutSize);
            IOUtils.WriteShort(outStream, StrikeoutPosition);
            IOUtils.WriteShort(outStream, FamilyClass);
            outStream.Write(panos,0,10);

            if (Os2version == 0)
            {
                IOUtils.WriteInt(outStream, CharRange);
            }
            else
            {
                // TODO: calculate and adjust the ranges that really present in the font according to version 4 of the table
                IOUtils.WriteInt(outStream, UnicodeRange1);
                IOUtils.WriteInt(outStream, UnicodeRange2);
                IOUtils.WriteInt(outStream, UnicodeRange3);
                IOUtils.WriteInt(outStream, UnicodeRange4);
            }

            Encoding isoEncoding = Encoding.GetEncoding("iso-8859-1");
            outStream.Write(isoEncoding.GetBytes(achVendID,0,4),0,4);

            IOUtils.WriteShort(outStream, fsSelection);

            int firstCharIndex = GetNewFirstIndex();
            IOUtils.WriteShort(outStream,firstCharIndex);

            int lastCharIndex = GetNewLastIndex(); 
            IOUtils.WriteShort(outStream,lastCharIndex);

            IOUtils.WriteShort(outStream, TypoAscender);
            IOUtils.WriteShort(outStream, TypoDescender);
            IOUtils.WriteShort(outStream, TypoLineGap);
            IOUtils.WriteShort(outStream, WinAscent);
            IOUtils.WriteShort(outStream, WinDescent);

            if (Os2version == 0)
            {
                return outStream.ToArray();
            }

            // TODO: recalculate codepages in case version 0 or wrong
            IOUtils.WriteInt(outStream, CodePageRange1);
            IOUtils.WriteInt(outStream, CodePageRange2);

            if (Os2version == 1)
            {
                return outStream.ToArray();
            }


            // TODO: add calculations in case default not set
            IOUtils.WriteShort(outStream, Height);
            IOUtils.WriteShort(outStream, CapHeight);
            IOUtils.WriteShort(outStream, DefaultChar);
            IOUtils.WriteShort(outStream, BreakChar);
            IOUtils.WriteShort(outStream, MaxContext);

            return outStream.ToArray();
        }

        private int GetNewLastIndex()
        {
            int value = 0;
            foreach (var ch in characters.Keys)
            {
                if (characters[ch].Needed)
                {
                    if ((ch > value) && (ch != 0xFFFF))
                    {
                        value = ch;
                    }
                }
            }
            if (value > 0xFFFF) // can't be higher then that
            {
                value = 0xFFFF;
            }
            return value;
           
        }

        private int GetNewFirstIndex()
        {
            int value = 0xFFFFF; // this is maximum allowed
            foreach (var ch in characters.Keys)
            {
                if (characters[ch].Needed)
                {
                    if (ch < value)
                    {
                        value = ch;
                    }
                }
            }
            return value;
        }

        private byte[] BuildGlyphLocations(bool asShorts)
        {
            int offset = 0;
            MemoryStream outStream = new MemoryStream();
             if (asShorts)
             {
                 IOUtils.WriteShort(outStream,0);
             }
             else
             {
                 IOUtils.WriteInt(outStream,0);
             }
            foreach (var glyph in glyphs)
            {
                if (glyph.Needed)
                {
                    int paddedLength = (glyph.Length + 3) & ~3;
                    offset += paddedLength;
                    if (asShorts)
                    {
                        IOUtils.WriteShort(outStream, offset / 2);
                    }
                    else
                    {
                        IOUtils.WriteInt(outStream, offset/2);
                    }
                }
            }
            return outStream.ToArray();
        }

        private byte[] BuildNames()
        {
            int offset = 0;
            int count = 0;
            foreach (var entry in names)
            {
                switch (entry.NameId)
                {
                    case 0:
                        // copyright; always keep
                        entry.Needed = true;
                        break;
                    case 1:
                    // family 
                    case 4:
                    // full name 
                    case 16:
                    // prefered family 
                    case 17:
                    // Postscript name for the font
                    case 6:
                        // preferred full name
                        entry.Needed = true;
                        inputStream.Seek(entry.Offset, SeekOrigin.Begin);
                        byte[] buffer = new byte[entry.Offset];
                        inputStream.Read(buffer, 0, entry.Length);
                        if (IsUnicodeEntry(entry))
                        {
                            string name = DecodeUnicode(buffer, 0, entry.Length);
                            name = "Subset-" + name;
                            entry.NewContent = EncodeUnicode(name);
                        }
                        else
                        {
                            Encoding isoEncoding = Encoding.GetEncoding("iso-8859-1");
                            int length = DetectStringLength(buffer, isoEncoding);
                            string name = isoEncoding.GetString(buffer, 0, length);
                            name = "Subset-" + name;
                            entry.NewContent = isoEncoding.GetBytes(name);
                        }
                        break;
                    case 2:
                        // subfamily (bold italic etc)
                        entry.Needed = true;
                        break;
                    case 7:
                        // trademark; always keep
                        entry.Needed = true;
                        break;
                    case 8:
                        // manufacturer name
                        entry.Needed = true;
                        break;
                    case 3:
                        entry.Needed = true;
                        if (IsUnicodeEntry(entry))
                        {
                            entry.NewContent = EncodeUnicode(GetFontID());
                        }
                        else
                        {
                            Encoding isoEncoding = Encoding.GetEncoding("iso-8859-1");
                            entry.NewContent = isoEncoding.GetBytes(GetFontID());
                        }
                        break;
                     default:
                        // TODO: figure out which entries are required                         
                        //entry.Needed = true;
                        break;
                }
                if (entry.Needed)
                {
                    entry.NewRelativeOffset = offset;
                    int len;
                    if (entry.NewContent != null)
                    {
                        len = entry.NewContent.Length;
                    }
                    else
                    {
                        len = entry.Length;
                    }
                    offset += len;
                    count++;
                }
            }

            MemoryStream outStream = new MemoryStream();
            IOUtils.WriteShort(outStream,0); // format
            IOUtils.WriteShort(outStream,(short)count);
            IOUtils.WriteShort(outStream, (short) (count*12 + 6));
            foreach (var entry in names)
            {
                if (entry.Needed)
                {
                    IOUtils.WriteShort(outStream, entry.PlatformId);
                    IOUtils.WriteShort(outStream, entry.EncodingId);
                    IOUtils.WriteShort(outStream, entry.LanguageId);
                    IOUtils.WriteShort(outStream, entry.NameId);
                    int len;
                    if ( entry.NewContent != null)
                    {
                        len = entry.NewContent.Length;
                    }
                    else
                    {
                        len = entry.Length;
                    }
                    IOUtils.WriteShort(outStream, len);
                    IOUtils.WriteShort(outStream, entry.NewRelativeOffset);
                }
            }

            foreach (var entry in names)
            {
                if (entry.Needed)
                {
                    if (entry.NewContent != null)
                    {
                        outStream.Write(entry.NewContent,0,entry.NewContent.Length);
                    }
                    else
                    {
                        CopyBytes(outStream,entry.Offset,entry.Length);
                    }
                }
            }
            return outStream.ToArray();
        }

        private int DetectStringLength(byte[] buffer, Encoding encoding)
        {
            string temp = encoding.GetString(buffer);
            int len = 0;
            foreach (var character in temp)
            {
                if (character == '\0')
                {
                    return len;
                }
                len++;
            }
            return len;
        }

        private string GetFontID()
        {
            if (FontID == null)
            {
                FontID = string.Format("Subset:{0:X}",DateTime.Now.Ticks);
            }
            return FontID;
        }

        private byte[] EncodeUnicode(string str)
        {
            try
            {
                if (!noUnicodeBigUnmarked)
                {
                    UnicodeEncoding encoder = new UnicodeEncoding(true, false, true);
                    return encoder.GetBytes(str);
                }
            }
            catch (Exception)
            {
                noUnicodeBigUnmarked = true;
            }
            // just do it "by hand"                 
            int len = str.Length;                 
            byte[] buf = new byte[len*2];                 
            for( int i = 0 ; i < len ; i++ ) 
            {                         
                char c = str[i];                         
                buf[2*i] = (byte)(c>>8);                         
                buf[2*i+1] = (byte)(c);                 
            }                 
            return buf;             
        }

        private byte[] BuildMaxP(TableDirectoryEntry maxp)
        {
            byte[] buf = new byte[maxp.Length]; // expect at least 6 byte long
            inputStream.Seek(maxp.Offset, SeekOrigin.Begin);
            inputStream.Read(buf, 0, maxp.Length);
            buf[4] = (byte)(NewGlyphCount >> 8);
            buf[5] = (byte)(NewGlyphCount);
            return buf;
        }

        private byte[] BuildHMtx()
        {
            MemoryStream outStream = new MemoryStream();
            for (int i = 0; i < glyphs.Count; i++)
            {
                GlyphData glyph = glyphs[i];
                if (glyph.Needed)
                {
                    if (glyph.NewIndex < NewVariableWidthCount)
                    {
                        IOUtils.WriteShort(outStream,glyph.Advance);
                    }
                    IOUtils.WriteShort(outStream,glyph.Lsb);
                }
            }
            return outStream.ToArray();
        }

        private byte[] BuildHHea(TableDirectoryEntry hhea)
        {
            byte[] buf = new byte[hhea.Length]; // expect 36 bytes long
            inputStream.Seek(hhea.Offset, SeekOrigin.Begin);
            inputStream.Read(buf, 0, hhea.Length);
            buf[34] = (byte)(NewVariableWidthCount >> 8);
            buf[35] = (byte)(NewVariableWidthCount);
            return buf;
        }

        private byte[] BuildHead(TableDirectoryEntry head, bool shortOffsets)
        {
            byte[] buf = new byte[head.Length]; // expect 54 bytes long                  
            inputStream.Seek(head.Offset, SeekOrigin.Begin);
            inputStream.Read(buf, 0, head.Length);
            buf[8] = 0; // zero out checkSumAdjustment
            buf[9] = 0;
            buf[10] = 0;
            buf[11] = 0;
            IOUtils.StuffLong(buf, 28, DateTime.Now.Ticks / 1000 + baseTime);
            buf[50] = 0;
            buf[51] = (byte)(shortOffsets ? 0 : 1);
            return buf;
        }

        private byte[] BuildCFF()
        {
            Dictionary<object,object> dict = new Dictionary<object, object>();
            foreach (var cff in dictCFF)
            {
                dict.Add(cff.Key,cff.Value);
            }
            SweepDictCFF(dict);
            string[] newStrings = ReindexStringsCff();
            MemoryStream outStream = new MemoryStream();

            outStream.WriteByte(1); // version major 
            outStream.WriteByte(0); // version minor 

            outStream.WriteByte(4); // header size 

            outStream.WriteByte(3); // abs offset size

            string[] names = { "Subset-" + nameCFF };
            WriteIndexCFF(outStream, names);

            
            int origPrivateDictLen = 0;
            if (dict.ContainsKey(new KeyCFF(KeyCFF.PRIVATE)))
            {
                object[] origprivatedict = (object[])dict[new KeyCFF(KeyCFF.PRIVATE)];
                origPrivateDictLen = Convert.ToInt32(origprivatedict[0]);    
            }
            
            IntPlaceholderCFF charset = new IntPlaceholderCFF();
            IntPlaceholderCFF charstrings = new IntPlaceholderCFF();
            IntPlaceholderCFF privatedict = new IntPlaceholderCFF();
            if (dict.ContainsKey(new KeyCFF(KeyCFF.PRIVATE)))
            {
                object[] pd = (object[])dict[new KeyCFF(KeyCFF.PRIVATE)];
                object[] privateval = { pd[0], privatedict };
                dict[new KeyCFF(KeyCFF.PRIVATE)] = privateval;
            }
            
            dict[new KeyCFF(KeyCFF.CHARSET)] =charset;
            dict[new KeyCFF(KeyCFF.CHARSTRINGS)] =  charstrings;
            
            dict.Remove(new KeyCFF(KeyCFF.ENCODING));
            object[] dicts = {dict};
            WriteIndexCFF(outStream, dicts);
            WriteIndexCFF(outStream, newStrings);
            WriteIndexCFF(outStream, globalSubrsCFF);
            int charsetOffset = (int)outStream.Length;
            WriteCharsetCff(outStream);
            int charstringsOffset = (int)outStream.Length;
            WriteIndexCFF(outStream, MakeGlyphArrayCff());
            int privatedictOffset = (int)outStream.Length;
            if (privateDictCFF != null)
            {
                WriteDictCff(outStream, privateDictCFF);
                int privatesubrOffset = (int)outStream.Length;
                if (privatesubrOffset - privatedictOffset != origPrivateDictLen)
                {
                    throw new Exception("private dict writing error");
                }
                if (privateSubrsCFF != null)
                {
                    WriteIndexCFF(outStream, privateSubrsCFF);
                }
            }
            

            byte[] result = outStream.ToArray();
            SetIntAtIndex(result,charset.Offset,charsetOffset);
            SetIntAtIndex(result, charstrings.Offset, charstringsOffset);
            if (privateDictCFF != null)
            {
                SetIntAtIndex(result, privatedict.Offset, privatedictOffset);                
            }
            return result;
        }

        private void WriteDictCff(MemoryStream outStream, Dictionary<object, object> dict)
        {
            if (dict == null)
            {
                return;
            }
            IEnumerable keys = dict.Keys;
            foreach (var key in keys)
            {
                object val = dict[key];
                WriteObjectCFF(outStream,val);
                WriteObjectCFF(outStream, key);
            }
        }

        private void WriteObjectCFF(MemoryStream outStream, object obj)
        {
            if (obj is KeyCFF)
            {
                int key = (obj as KeyCFF).KeyId;
                if (key < 0xFF)
                {
                    outStream.WriteByte((byte)key);
                }
                else
                {
                    outStream.WriteByte(12);
                    outStream.WriteByte((byte)key);
                }
            }
            else if (obj is int || obj is short)
            {
                int val = Convert.ToInt32(obj);
                WriteIntCFF(outStream, val);
            }
            else if (obj is StringCFF)
            {
                int val = (obj as StringCFF).NewIndex;
                WriteIntCFF(outStream, val);
            }
            else if (obj is double)
            {
                string s = obj.ToString();
                outStream.WriteByte(30);
                int b = 0;
                int i = 0;
                int len = s.Length;
                bool first = true;
                while (true)
                {
                    int n;
                    if (i >= len )
                    {
                        n = 0xF;
                    }
                    else
                    {
                        char c = s[i];
                        if ('0' <= c && c <= '9')
                        {
                            n = c - '0';
                        }
                        else if (c == '-')
                            n = 0xE;
                        else if (c == '.')
                            n = 0xA;
                        else if (c == 'E' || c == 'e')
                        {
                            c = s[i + 1];
                            if (c == '-')
                            {
                                i++;
                                n = 0xB;
                            }
                            else
                            {
                                n = 0xC;
                            }
                        }
                        else
                        {
                            throw new Exception(string.Format("Bad number : {0}",s));
                        }
                    }
                    if ( first )
                    {
                        b = n;
                    }
                    else
                    {
                        b = (b << 4) | n;
                        outStream.WriteByte((byte)b);
                        if (i >= len)
                        {
                            break;
                        }
                    }
                    first = !first;
                    i++;
                }
            }
            else if (obj is IntPlaceholderCFF)
            {
                outStream.WriteByte(29);
                (obj as IntPlaceholderCFF).Offset = (int)outStream.Length;
                IOUtils.WriteInt(outStream,0);
            }
            else if (obj is object[])
            {
                object[] arr = obj as object[];
                foreach (var element in arr)
                {
                    WriteObjectCFF(outStream,element);
                }
            }
            else
            {
                throw new Exception("unknown object");
            }
        }

        private void WriteIntCFF(MemoryStream outStream, int val)
        {
            if (-107 <= val && val <= 107)
            {
                outStream.WriteByte((byte)(val + 139));
            }
            else if (108 <= val && val <= 1131)
            {
                val -= 108;
                outStream.WriteByte((byte)((val >> 8) + 247));
                outStream.WriteByte((byte)val);
            }
            else if (-1131 <= val && val <= -108)
            {
                val = -val - 108;
                outStream.WriteByte((byte)((val >> 8) + 251));
                outStream.WriteByte((byte)val);
            }
            else if (-0x8000 <= val && val <= 0x7FFF)
            {
                outStream.WriteByte(28);
                IOUtils.WriteShort(outStream, val);
            }
            else
            {
                outStream.WriteByte(29);
                IOUtils.WriteInt(outStream, val);
            }
        }

        private object[] MakeGlyphArrayCff()
        {
            object[] subset = new object[NewGlyphCount];
            int index = 0;
            foreach (var glyph in glyphs)
            {
                if (glyph.Needed)
                {
                    subset[index++] = glyph;
                }
            }
            return subset;
        }

        private void WriteCharsetCff(MemoryStream outStream)
        {
            int i = 0;
            int count = 0;
            int prevsid = -1;
            outStream.WriteByte(2); // use format 2
            while (true)
            {
                GlyphData glyph = glyphs[i++];
                if (glyph.Needed)
                {
                    int sid = glyph.NamesIdCFF;
                    if (sid >= CFF_STD_STRING_COUNT)
                    {
                        sid = stringsCFF[sid - CFF_STD_STRING_COUNT].NewIndex;
                    }
                    if (prevsid == -1)
                    {
                        IOUtils.WriteShort(outStream,sid);
                        count = 0;
                    }
                    else if (prevsid != sid -1)
                    {
                        IOUtils.WriteShort(outStream, count);
                        IOUtils.WriteShort(outStream, sid);
                        count = 0;
                    }
                    else
                    {
                        count++;
                    }
                    prevsid = sid;
                }
                if (i >= glyphs.Count)
                {
                    IOUtils.WriteShort(outStream, count);
                    break;
                }
            }
        }

        private void WriteIndexCFF(MemoryStream outStream, object[] index)
        {
            if (index == null)
            {
                return;
            }
            IOUtils.WriteShort(outStream,index.Length);
            //if (index.Length == null) // always false
            //{
            //    return;
            //}
            MemoryStream data = new MemoryStream();
            int[] offsets = new int[index.Length+1];
            offsets[0] = 1;
            bool adjastOffsets = false;
            int i = 0;
            foreach (var item in index)
            {
                if (item is string)
                {
                    Encoding isoEncoding = Encoding.GetEncoding("iso-8859-1");
                    byte[] sb = isoEncoding.GetBytes(item as string);
                    data.Write(sb,0,sb.Length);
                }
                else if (item is Dictionary<object,object>)
                {
                    WriteDictCff(data, item as Dictionary<object,object>);
                    adjastOffsets = true;
                }
                else if (item is Range)
                {
                    Range r = item as Range;
                    CopyBytes(data,r.Offset,r.Length);
                }
                else if (item is GlyphData)
                {
                    GlyphData r = item as GlyphData;
                    CopyBytes(data, r.Offset, r.Length);
                }
                else
                {
                    throw new Exception("unknown index type");
                }
                offsets[i + 1] = (int)(data.Length + 1);
            }
            byte offSize;
            int offset = offsets[index.Length];
            if (offset <= 0xFF)
            {
                offSize = 1;
            }
            else if (offset <= 0xFFFF)
            {
                offSize = 2;
            }
            else if ( offset <= 0xFFFFFF )
            {
                offSize = 3;
            }
            else
            {
                offSize = 4;
            }
            outStream.WriteByte(offSize);
            foreach(var off in offsets)
            {
                WriteOffsetCff(outStream, offSize, off);
            }

            if (adjastOffsets)
            {
                int offsetAdjust = (int) outStream.Length;
                foreach (var item in index)
                {
                    if (item is Dictionary<object,object>)
                    {
                        AdjustOffsetDictCFF(item as Dictionary<object, object>, offsetAdjust);
                    }
                }
            }

            data.WriteTo(outStream);
        }

        private void AdjustOffsetDictCFF(Dictionary<object, object> dict, int offsetAdj)
        {
            IEnumerable elements = dict.Values;
            foreach (var val in elements)
            {
                AdjustOffsetObjectCff(val,offsetAdj);
            }
        }

        private void AdjustOffsetObjectCff(object obj, int offsetAdj)
        {
            if (obj is IntPlaceholderCFF)
            {
                (obj as IntPlaceholderCFF).Offset += offsetAdj;
            }
            else if (obj is object[])
            {
                object[] arr = obj as object[];
                foreach (var element in arr)
                {
                    AdjustOffsetObjectCff(element,offsetAdj);   
                }
            }
        }

        private void WriteOffsetCff(MemoryStream outStream, byte offSize, int offset)
        {
	        switch (offSize) 
            {
		case 1:
			outStream.WriteByte((byte)offset);
			break;
		case 2:
			IOUtils.WriteShort(outStream, offset);
			break;
		case 3:
			outStream.WriteByte((byte)(offset >> 16));
			IOUtils.WriteShort(outStream, offset);
			break;
		case 4:
			IOUtils.WriteInt(outStream, offset);
			break;
		}            
        }

        private string[] ReindexStringsCff()
        {
            foreach (var cff in stringsCFF)
            {
                cff.Needed = true;
            }
            int index = 0;
            foreach (var cff in stringsCFF)
            {
                if (cff.Needed)
                {
                    cff.NewIndex = CFF_STD_STRING_COUNT + index++;
                }
            }
            string[] newArr = new string[index];
            index = 0;
            foreach (var cff in stringsCFF)
            {
                if ( cff.Needed )
                {
                    if (cff.Value != nameCFF)
                    {
                        newArr[index++] = cff.Value;                        
                    }
                    else
                    {
                        newArr[index++] = "Subset-" + cff.Value;
                    }
                }
            }
            return newArr;
        }

        private void SweepDictCFF(Dictionary<object, object> dict)
        {
            foreach(var key in sidIndicesCFF)
            {
                KeyCFF cffKey = new KeyCFF(key);
                if (!dict.ContainsKey(cffKey))
                {
                    continue;
                }
                object val = dict[cffKey];
                if (val is int)
                {
                    int sid = Convert.ToInt32(val);
                    if ( sid >= CFF_STD_STRING_COUNT)
                    {
                        StringCFF str = stringsCFF[sid - CFF_STD_STRING_COUNT];
                        str.Needed = true;
                        dict[cffKey] = str;
                    }
                }
                else
                {
                    throw new Exception("unsupported value for SID key");
                }
            }
        }

        public void CopyBytes(MemoryStream outStream, int offset, int len)
        {
            inputStream.Seek(offset,SeekOrigin.Begin);    
            byte[] buffer = new byte[256];
            while (len > 0)
            {
                int r = len;                         
                if (r > buffer.Length)
                {
                    r = buffer.Length;
                }
                inputStream.Read(buffer,0,r);                         
                outStream.Write(buffer, 0, r);                         
                len -= r;
            }
        } 

        private byte[] BuildGlyphsTT()
        {
            MemoryStream outStream = new MemoryStream();
            byte[] buffer = new byte[256];
            for (int i = 0; i < glyphs.Count; i++)
            {
                GlyphData glyph = glyphs[i];
                if (glyph.Needed)
                {
                    if (glyph.CompositeTT)
                    {
                        byte[] arr = glyph.Length > buffer.Length ? new byte[glyph.Length]: buffer;
                        inputStream.Seek(glyph.Offset, SeekOrigin.Begin);
                        inputStream.Read(arr, 0, glyph.Length);
                        int index = 10;
                        while (index <= glyph.Length - 4)
                        {
                            int flags = IOUtils.GetShort(arr, index);
                            int glyphID = IOUtils.GetShort(arr, index + 2) & 0xFFFF;
                            IOUtils.StuffShort(arr,index+2,(short) glyphs[glyphID].NewIndex);
                            if ((flags & 0x20) == 0)
                            {
                                break;
                            }
                            index += 4 + GetCompositeGlyphArgSize(flags); // MORE_COMPONENTS                                          
                        }
                        outStream.Write(arr,0,glyph.Length);
                    }
                    else
                    {
                        CopyBytes(outStream,glyph.Offset,glyph.Length);
                    }
                    int padCount = (4 - glyph.Length) & 3;
                    while (padCount > 0)
                    {
                        padCount--;
                        outStream.WriteByte(0);
                    }
                }
            }
            return outStream.ToArray();
        }

        private byte[] BuildCMap()
        {
            // build a bit array of needed characters 
            List<int> charMask = new List<int>();
            IEnumerable chars = characters.Keys;
            int maxChar = 0;
            foreach (var ch in chars)
            {
                int code = (int)ch;
                CharacterData data =  characters[code];
                if (data.Needed)
                {
                    int ic = code;
                    charMask.Add(ic);
                    if (ic > maxChar)
                    {
                        maxChar = ic;
                    }
                }
            }

            // collect segments
            List<EncodingSegment> segments = new List<EncodingSegment>();
            EncodingSegment segment = null;
            for (int ch = 1; ch <= maxChar; ch++)
            {
               
                if (charMask.Contains(ch))
                {
                    if (segment == null)
                    {
                        segment = new EncodingSegment();
                        segments.Add(segment);
                        segment.Start = ch;
                    }
                }
                else
                {
                    if (segment != null)
                    {
                        segment.End = ch - 1;
                        segment = null;
                    }
                }
            }
            if (segment != null)
            {
                segment.End = maxChar;
            }
            if (maxChar < 0xFFFF)
            {
                segment = new EncodingSegment();
                segments.Add(segment);
                segment.Start = 0xFFFF;
                segment.End = 0xFFFF;
            }

            // collect glyph ids for the segments
            int segCount = segments.Count();
            int sectionLength = 16 + 8 * segCount;
            int glyphsBefore = 0;
            for (int i = 0; i < segCount; i++)
            {
                segment = segments[i];
                int segLen = segment.End - segment.Start + 1;
                short[] glyphIds = new short[segLen];
                segment.GlyphIds = glyphIds;
                int delta = 0;
                for (int k = 0; k < segLen; k++)
                {
                    int ch = k + segment.Start;
                    CharacterData data = characters[ch];
                    short glyphIndex = (data == null ? (short)0 : data.GlyphIndex);
                    if (glyphIndex != 0)
                    {
                        GlyphData glyph = glyphs[glyphIndex];
                        glyphIndex = (short)glyph.NewIndex;
                    }
                    int d = glyphIndex - ch;
                    if (k == 0)
                    {
                        delta = d;
                        segment.ConstDelta = true;
                    }
                    else
                    {
                         if (delta != d)
                         {
                             segment.ConstDelta = false;
                         }
                    }
                    glyphIds[k] = glyphIndex;
                }
                if (!segment.ConstDelta)
                {
                    segment.GlyphsBefore = glyphsBefore;
                    sectionLength += 2 * segment.GlyphIds.Length;
                    glyphsBefore += segment.GlyphIds.Length;
                }
            }
            if (sectionLength > 0xFFFF)
            {
                throw new Exception("cmap is too long");
            }

            // write out cmap section
            MemoryStream result = new MemoryStream();

            // cmap Header
            IOUtils.WriteShort(result,0); // version number (0)
            IOUtils.WriteShort(result, 2); // number of tables = 2

            // Encoding Record
            IOUtils.WriteShort(result, (short)PlatformId.Unicode); // platform ID = Unicode (0)
            IOUtils.WriteShort(result, 3); // Platform speciffic encoding = 3 (Unicode 2.0 or later semantics)
            IOUtils.WriteInt(result, 20); // table offset

            // Encoding Record
            IOUtils.WriteShort(result, (short)PlatformId.Microsoft); // platform = Microsoft (3)
            IOUtils.WriteShort(result, 1); // encoding = Unicode BMP (UCS-2) (1)
            IOUtils.WriteInt(result, 20); // table offset

            // Format 4 table
            IOUtils.WriteShort(result, 4); // format 4
            IOUtils.WriteShort(result, (short)sectionLength); // length
            IOUtils.WriteShort(result, 0); // language (0 = language independent)
            IOUtils.WriteShort(result, (short)(segCount*2));
            int log = floorPowerOf2(segCount & 0xFFFF);
            int entrySelector = log;
            int searchRange = 1 << (log + 1);
            int rangeShift = segCount * 2 - searchRange;
            IOUtils.WriteShort(result, (short)searchRange);
            IOUtils.WriteShort(result, (short)entrySelector);
            IOUtils.WriteShort(result, (short)rangeShift);

            for (int i = 0; i < segCount; i++)
            {
                segment = segments.ElementAt(i);
                IOUtils.WriteShort(result, segment.End);
            }
            IOUtils.WriteShort(result, 0);
            for (int i = 0; i < segCount; i++)
            {
                segment = segments.ElementAt(i);
                IOUtils.WriteShort(result, segment.Start);
            }

            for (int i = 0; i < segCount; i++)
            {
                segment = segments.ElementAt(i);
                if (segment.ConstDelta)
                {
                    IOUtils.WriteShort(result, (short)(segment.GlyphIds[0] - segment.Start));                                  
                }
                else
                {
                    IOUtils.WriteShort(result, 0);
                }
            }

            for (int i = 0; i < segCount; i++)
            {
                segment = segments.ElementAt(i);
                if (segment.ConstDelta)
                {
                    IOUtils.WriteShort(result, 0);
                }
                else
                {
                    int rangeOffset = 2 * (segCount - i + segment.GlyphsBefore);
                    IOUtils.WriteShort(result, (short)rangeOffset);
                }
            }

            for (int i = 0; i < segCount; i++)
            {
                segment = segments.ElementAt(i);
                if (!segment.ConstDelta)
                {
                    for (int k = 0; k < segment.GlyphIds.Length; k++)
                    {
                        IOUtils.WriteShort(result, segment.GlyphIds[k]);
                    }
                }
            }

            // convert to byte array
            byte[] arr = result.ToArray();
            if (arr.Length != sectionLength + 20)
            {
                throw new Exception("inconsistent cmap");
            }
            return arr;
        }


        private int floorPowerOf2(int n)
        {
            for (int i = 1; i < 32; i++)
            {
                int p = 1 << i; if (n < p)
                {
                    return i - 1;
                }
            } 
            throw new Exception("out of range");             
        }

        private void ReindexGlyphs()
        {
            int index = 0;
            int lastAdvance = 0x10000;
            for (int i = 0; i < glyphs.Count; i++)
            {
                if (i < 2)
                {
                    glyphs[i].Needed = true;
                }
                if (glyphs[i].Needed)
                {
                    glyphs[i].NewIndex = index++;
                    if (glyphs[i].Advance != lastAdvance)
                    {
                        lastAdvance = glyphs[i].Advance;
                        NewVariableWidthCount = index;
                    }
                    if (stringsCFF != null)
                    {
                        int sid = glyphs[i].NamesIdCFF;
                        if (sid >= CFF_STD_STRING_COUNT)
                        {
                            stringsCFF[sid - CFF_STD_STRING_COUNT].Needed = true;
                        }
                    }
                }
            }
            NewGlyphCount = index;
        }

        private void ResolveCompositeGlyphsTT()
        {
            for (int i = 0; i < glyphs.Count; i++)
            {
                if (glyphs[i].Needed)
                {
                    byte[] buffer = new byte[256];
                    inputStream.Seek(glyphs[i].Offset, SeekOrigin.Begin);
                    inputStream.Read(buffer, 0, 10);
                    short numberOfContours = IOUtils.GetShort(buffer, 0);
                     if (numberOfContours < 0)
                     {
                         // composite glyph
                         glyphs[i].CompositeTT = true;
                         int remains = glyphs[i].Length- 10;
                         while (remains > 0)
                         {
                             inputStream.Read(buffer, 0, 4);
                             remains -= 4;
                             int flags = IOUtils.GetShort(buffer, 0);
                             int glyphIndex = (IOUtils.GetShort(buffer, 2) & 0xFFFF);
                             glyphs[glyphIndex].Needed = true;
                             if ((flags & 0x20) == 0) // MORE_COMPONENTS not set                                                     
                             {
                                 break;
                             }
                             int argSize = GetCompositeGlyphArgSize(flags);
                             inputStream.Read(buffer, 0, argSize);
                             remains -= argSize;
                         }
                     }
                }
            }
        }

        private static int GetCompositeGlyphArgSize(int flags)
        {
            int argSize = 0;
            if ((flags & 1) != 0) // ARG_1_AND_2_ARE_WORDS is set                          
            {
                argSize = 4;
            }
            else
            {
                argSize = 2;
            }

            if ((flags & 8) != 0) // WE_HAVE_A_SCALE                          
            {
                argSize += 2;
            }
            else if ((flags & 0x40) != 0) // WE_HAVE_AN_X_AND_Y_SCALE
            {
                argSize += 4;
            }
            else if ((flags & 0x80) != 0) // WE_HAVE_A_TWO_BY_TWO
            {
                argSize += 8;
            }
            return argSize;
        }

        private bool CanSubset()
        {
            if ((FsType & 0xF) == 2)
            {
                return false; // explicitly disallowed embedding and subsetting                 
            }
            if ((FsType & 0x0100) != 0)
            {
                return false; // explicitly disallowed subsetting                 
            }
            return true; 
        }
    }
}
