using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenTypeFonts
{
    internal class KeyCFF
    {
        public const int ENCODING = 16;
        public const int CHARSET = 15;
        public const int CHARSTRINGS = 17;
        public const int PRIVATE = 18;

        public int KeyId
        {
            get { return keyId; }
        }

        private int keyId;

        public KeyCFF(int keyid)
        {
            keyId = keyid;
        }

        public override bool Equals(object obj)
        {
            if (obj.GetType() != GetType())
            {
                return false;
            }
            return (KeyId == ((KeyCFF)obj).KeyId);
        }

        public override int GetHashCode()
        {
            return KeyId;
        }
    }
}
