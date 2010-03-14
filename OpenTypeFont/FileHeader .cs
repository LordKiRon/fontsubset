using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTypeFonts
{
    internal class FileHeader
    {
        private readonly List<TableDirectoryEntry> tableDirectory = new List<TableDirectoryEntry>();
        private readonly Dictionary<string,TableDirectoryEntry> tableMap = new Dictionary<string, TableDirectoryEntry>();

        public int Version { get; set; }
        public short NumTables { get; set; }
        public short SearchRange { get; set; }
        public short EntrySelector { get; set; }
        public short RangeShift { get; set; }
        public List<TableDirectoryEntry> TableDirectory { get { return tableDirectory; } }
        public Dictionary<string, TableDirectoryEntry> TableMap { get { return tableMap; } }

        public void Load(Stream stream)
        {
            byte[] buffer = new byte[256];
            int readResult = stream.Read(buffer, 0, 12);
            if ( readResult != 12)
            {
                throw new Exception(string.Format("could not read {0} bytes",12));
            }
            int version = IOUtils.GetInt(buffer, 0);
            switch (version)
            {
                case 0x74746366: // TrueType collection file                         
                    // we can only read the first font in the collection   
                    stream.Read(buffer, 0, 4);
                    stream.Seek(IOUtils.GetInt(buffer, 0), SeekOrigin.Begin);
                    stream.Read(buffer, 0, 12);
                    version = IOUtils.GetInt(buffer, 0);
                    break;
                case 0x00010000: // regular TrueType
                    break;
                case 0x4f54544f: // CFF based
                    break;
                default:
                    throw new Exception("Invalid OpenType file");
            }
            Version = version;
            NumTables = IOUtils.GetShort(buffer, 4);
            SearchRange = IOUtils.GetShort(buffer, 6);
            EntrySelector = IOUtils.GetShort(buffer, 8);
            RangeShift = IOUtils.GetShort(buffer, 10);
            tableDirectory.Clear();
            for (int i = 0; i < NumTables; i++)
            {
                TableDirectoryEntry entry = new TableDirectoryEntry();
                stream.Read(buffer, 0, 16);
                char[] arr = { (char)(buffer[0] & 0xFF), 
                                 (char)(buffer[1] & 0xFF), 
                                 (char)(buffer[2] & 0xFF), 
                                 (char)(buffer[3] & 0xFF) };
                int len = buffer[0] == 0 ? 0 : (buffer[1] == 0 ? 1 : (buffer[2] == 0 ? 2 : (buffer[3] == 0 ? 3 : 4))); 
                entry.Identifier = new string(arr,0,len);
                entry.CheckSum = IOUtils.GetInt(buffer, 4);
                entry.Offset = IOUtils.GetInt(buffer, 8);
                entry.Length = IOUtils.GetInt(buffer, 12);
                tableDirectory.Add(entry);
                tableMap.Add(entry.Identifier,entry);
            }
        }
    }
}
