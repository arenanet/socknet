using System;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Protocols.Http2.Hpack
{
    public class HpackHeader
    {
        public static readonly Encoding ISO_ENCODING = Encoding.GetEncoding("ISO-8859-1");
        public static readonly Encoding STRING_ENCODING = Encoding.Unicode;

        public const int HEADER_ENTRY_OVERHEAD = 32;

        public enum IndexType
        {
            INCREMENTAL, // 6.2.1. Literal Header Field with Incremental Indexing
            NONE,        // 6.2.2. Literal Header Field without Indexing
            NEVER        // 6.2.3. Literal Header Field never Indexed
        }

        public static bool ByteArraysEqual(byte[] b1, byte[] b2)
        {
            bool equal = true;

            if (b1 != b2) // if this is the same array then we don't need to compare them
            {
                if (b1 != null && b2 != null && b1.Length == b2.Length)
                {
                    for (int i = 0; i < b1.Length; i++)
                    {
                        if (b1[i] != b2[i])
                        {
                            equal = false;
                            break;
                        }
                    }
                }
                else
                {
                    equal = false;
                }
            }

            return equal;
        }

        public static byte[] ToIso(string value)
        {
            byte[] utfBytes = STRING_ENCODING.GetBytes(value);
            byte[] isoBytes = Encoding.Convert(STRING_ENCODING, ISO_ENCODING, utfBytes);
            return isoBytes;
        }

        public static int SizeOf(byte[] name, byte[] value)
        {
            return name.Length + value.Length + HEADER_ENTRY_OVERHEAD;
        }

        public byte[] Name { private set; get; }
        public string NameAsString
        {
            get
            {
                return Name == null ? "" : ISO_ENCODING.GetString(Name);
            }
        }

        public byte[] Value { private set; get; }
        public string ValueAsString
        {
            get
            {
                return Value == null ? "" : ISO_ENCODING.GetString(Value);
            }
        }

        public HpackHeader(string name)
            : this(name, "")
        {
        }

        public HpackHeader(string name, string value)
        {
            this.Name = ToIso(name);
            this.Value = ToIso(value);
        }

        public HpackHeader(byte[] name)
            : this(name, new byte[0])
        {
        }

        public HpackHeader(byte[] name, byte[] value)
        {
            this.Name = name;
            this.Value = value;
        }

        public int Size
        {
            get
            {
                return Name.Length + Value.Length + HEADER_ENTRY_OVERHEAD;
            }
        }

        public override string ToString()
        {
            return NameAsString + ": " + ValueAsString;
        }

        public override int GetHashCode()
        {
            int num = 352654597;
            int num2 = num;
            for (int i = Name.Length; i > 0; i -= 2)
            {
                num = (((num << 5) + num) + (num >> 27)) ^ Name[i - 1];
                if (i <= 2)
                {
                    break;
                }
                num2 = (((num2 << 5) + num2) + (num2 >> 27)) ^ Name[i - 2];
            }
            for (int i = Value.Length; i > 0; i -= 2)
            {
                num = (((num << 5) + num) + (num >> 27)) ^ Value[i - 1];
                if (i <= 2)
                {
                    break;
                }
                num2 = (((num2 << 5) + num2) + (num2 >> 27)) ^ Value[i - 2];
            }
            return (num + (num2 * 1566083941));
        }

        public override bool Equals(object obj)
        {
            if (!(obj is HpackHeader))
            {
                return false;
            }

            HpackHeader compareObj = (HpackHeader)obj;

            if (Size != compareObj.Size)
            {
                return false;
            }

            return ByteArraysEqual(Name, compareObj.Name) && ByteArraysEqual(Value, compareObj.Value);
        }
    }
}
