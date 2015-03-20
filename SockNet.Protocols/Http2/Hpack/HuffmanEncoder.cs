using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Protocols.Http2.Hpack
{
    public class HuffmanEncoder
    {
        private readonly int[] codes;
        private readonly byte[] lengths;

        public HuffmanEncoder(int[] codes, byte[] lengths)
        {
            this.codes = codes;
            this.lengths = lengths;
        }

        public void Encode(Stream outStream, byte[] data)
        {
            Encode(outStream, data, 0, data.Length);
        }

        public void Encode(Stream outStream, byte[] data, int off, int len)
        {
            if (outStream == null)
            {
                throw new ArgumentNullException("out");
            }
            else if (data == null)
            {
                throw new ArgumentNullException("data");
            }
            else if (off < 0 || len < 0 || (off + len) < 0 || off > data.Length || (off + len) > data.Length)
            {
                throw new ArgumentOutOfRangeException();
            }
            else if (len == 0)
            {
                return;
            }

            long current = 0;
            int n = 0;

            for (int i = 0; i < len; i++)
            {
                int b = data[off + i] & 0xFF;
                int code = codes[b];
                int nbits = lengths[b];

                current <<= nbits;
                current |= code;
                n += nbits;

                while (n >= 8)
                {
                    n -= 8;
                    outStream.WriteByte(((byte)(current >> n)));
                }
            }

            if (n > 0)
            {
                current <<= (8 - n);
                current |= (0xFF >> n); // this should be EOS symbol
                outStream.WriteByte((byte)current);
            }
        }

        public int GetEncodedLength(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }
            long len = 0;
            foreach (byte b in data)
            {
                len += lengths[b & 0xFF];
            }
            return (int)((len + 7) >> 3);
        }
    }
}
