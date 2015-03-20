using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Protocols.Http2.Hpack
{
    public class HuffmanDecoder
    {
        private readonly Node root = new Node();

        public HuffmanDecoder(int[] codes, byte[] lengths)
        {
            BuildTree(codes, lengths);
        }

        private void BuildTree(int[] codes, byte[] lengths)
        {
            for (int i = 0; i < lengths.Length; i++)
            {
                AddCode(i, codes[i], lengths[i]);
            }
        }

        private void AddCode(int sym, int code, byte len)
        {
            Node terminal = new Node(sym, len);

            Node current = root;
            while (len > 8)
            {
                len -= 8;
                int i = ((code >> len) & 0xFF);
                if (current.children == null)
                {
                    throw new ArgumentException("invalid dictionary: prefix not unique");
                }
                if (current.children[i] == null)
                {
                    current.children[i] = new Node();
                }
                current = current.children[i];
            }

            int shift = 8 - len;
            int start = (code << shift) & 0xFF;
            int end = 1 << shift;
            for (int i = start; i < start + end; i++)
            {
                current.children[i] = terminal;
            }
        }

        public void Decode(byte[] buf, Stream stream)
        {
            Node node = root;
            int current = 0;
            int nbits = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                int b = buf[i] & 0xFF;
                current = (current << 8) | b;
                nbits += 8;
                while (nbits >= 8)
                {
                    int c = (current >> (nbits - 8)) & 0xFF;
                    node = node.children[c];
                    if (node.children == null)
                    {
                        // terminal node
                        stream.WriteByte((byte)node.symbol);
                        nbits -= node.terminalBits;
                        node = root;
                    }
                    else
                    {
                        // non-terminal node
                        nbits -= 8;
                    }
                }
            }

            while (nbits > 0)
            {
                int c = (current << (8 - nbits)) & 0xFF;
                node = node.children[c];
                if (node.children != null || node.terminalBits > nbits)
                {
                    break;
                }
                stream.WriteByte((byte)node.symbol);
                nbits -= node.terminalBits;
                node = root;
            }
        }

        private class Node
        {
            // Internal nodes have children
            internal readonly Node[] children;

            // Terminal nodes have a symbol
            internal readonly int symbol;

            // Number of bits represented in the terminal node
            internal readonly int terminalBits;

            internal Node()
            {
                children = new Node[256];
                symbol = 0;
                terminalBits = 0;
            }

            internal Node(int symbol, int bits)
            {
                this.children = null;
                this.symbol = symbol;
                int b = bits & 0x07;
                this.terminalBits = b == 0 ? 8 : b;
            }
        }
    }
}
