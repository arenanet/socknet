using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Protocols.Http2.Hpack
{
    public class HpackEncoder
    {
        private const int BUCKET_SIZE = 17;
        private static readonly byte[] EMPTY = new byte[0];

        // For testing
        private readonly bool useIndexing;
        private readonly bool forceHuffmanOn;
        private readonly bool forceHuffmanOff;

        // a linked hash map of header fields
        private readonly HeaderEntry[] headerFields = new HeaderEntry[BUCKET_SIZE];
        private readonly HeaderEntry head = new HeaderEntry(-1, EMPTY, EMPTY, int.MaxValue, null);
        private int size;
        private int capacity;

        public HpackEncoder(int maxHeaderTableSize)
            : this(maxHeaderTableSize, true, false, false)
        {
        }

        /**
         * Constructor for testing only.
         */
        public HpackEncoder(
            int maxHeaderTableSize,
            bool useIndexing,
            bool forceHuffmanOn,
            bool forceHuffmanOff
        )
        {
            if (maxHeaderTableSize < 0)
            {
                throw new ArgumentException("Illegal Capacity: " + maxHeaderTableSize);
            }
            this.useIndexing = useIndexing;
            this.forceHuffmanOn = forceHuffmanOn;
            this.forceHuffmanOff = forceHuffmanOff;
            this.capacity = maxHeaderTableSize;
            head.before = head.after = head;
        }

        /**
         * Encode the header field into the header block.
         */
        public void EncodeHeader(Stream outStream, HpackHeader header, bool sensitive)
        {

            // If the header value is sensitive then it must never be indexed
            if (sensitive)
            {
                int nameIndex = GetNameIndex(header.Name);
                EncodeLiteral(outStream, header, HpackHeader.IndexType.NEVER, nameIndex);
                return;
            }

            // If the peer will only use the static table
            if (capacity == 0)
            {
                int staticTableIndex = HpackStaticTable.GetIndex(header.Name, header.Value);
                if (staticTableIndex == -1)
                {
                    int nameIndex = HpackStaticTable.GetIndex(header.Name);
                    EncodeLiteral(outStream, header, HpackHeader.IndexType.NONE, nameIndex);
                }
                else
                {
                    EncodeInteger(outStream, 0x80, 7, staticTableIndex);
                }
                return;
            }

            int headerSize = header.Size;

            // If the headerSize is greater than the max table size then it must be encoded literally
            if (headerSize > capacity)
            {
                int nameIndex = GetNameIndex(header.Name);
                EncodeLiteral(outStream, header, HpackHeader.IndexType.NONE, nameIndex);
                return;
            }

            HeaderEntry headerField = GetEntry(header.Name, header.Value);
            if (headerField != null)
            {
                int index = GetIndex(headerField.index) + HpackStaticTable.Length;
                // Section 4.2 - Indexed Header Field
                EncodeInteger(outStream, 0x80, 7, index);
            }
            else
            {
                int staticTableIndex = HpackStaticTable.GetIndex(header.Name, header.Value);
                if (staticTableIndex != -1)
                {
                    // Section 4.2 - Indexed Header Field
                    EncodeInteger(outStream, 0x80, 7, staticTableIndex);
                }
                else
                {
                    int nameIndex = GetNameIndex(header.Name);
                    if (useIndexing)
                    {
                        EnsureCapacity(headerSize);
                    }
                    HpackHeader.IndexType indexType = useIndexing ? HpackHeader.IndexType.INCREMENTAL : HpackHeader.IndexType.NONE;
                    EncodeLiteral(outStream, header, indexType, nameIndex);
                    if (useIndexing)
                    {
                        Add(header);
                    }
                }
            }
        }

        /**
         * Set the maximum header table size.
         */
        public void SetMaxHeaderTableSize(Stream outStream, int maxHeaderTableSize)
        {
            if (maxHeaderTableSize < 0)
            {
                throw new ArgumentException("Illegal Capacity: " + maxHeaderTableSize);
            }
            this.capacity = maxHeaderTableSize;
            EnsureCapacity(0);
            EncodeInteger(outStream, (int)0x20, 5, maxHeaderTableSize);
        }

        /**
         * Return the maximum header table size.
         */
        public int GetMaxHeaderTableSize()
        {
            return capacity;
        }

        /**
         * @param mask  A mask to be applied to the first byte
         * @param n     The number of prefix bits
         * @param i     The value to encode
         */
        private static void EncodeInteger(Stream outStream, int mask, int n, int i)
        {
            if (n < 0 || n > 8)
            {
                throw new ArgumentException("N: " + n);
            }

            int nbits = 0xFF >> (8 - n);

            if (i < nbits)
            {
                outStream.WriteByte((byte)(mask | i));
            }
            else
            {
                outStream.WriteByte((byte)(mask | nbits));
                int length = i - nbits;
                while (true)
                {
                    if ((length & ~0x7F) == 0)
                    {
                        outStream.WriteByte((byte)length);
                        return;
                    }
                    else
                    {
                        outStream.WriteByte((byte)((length & 0x7F) | 0x80));
                        length >>= 7;
                    }
                }
            }
        }

        /**
         * 4.3.1. Literal Header Field with Incremental Indexing
         * 4.3.2. Literal Header Field without Indexing
         * 4.3.3. Literal Header Field never Indexed
         */
        private void EncodeLiteral(Stream outStream, HpackHeader header, HpackHeader.IndexType indexType, int nameIndex)
        {

            int mask;
            int prefixBits;
            switch (indexType)
            {
                case HpackHeader.IndexType.INCREMENTAL:
                    mask = 0x40;
                    prefixBits = 6;
                    break;
                case HpackHeader.IndexType.NONE:
                    mask = 0x00;
                    prefixBits = 4;
                    break;
                case HpackHeader.IndexType.NEVER:
                    mask = 0x10;
                    prefixBits = 4;
                    break;
                default:
                    throw new Exception("should not reach here");
            }
            EncodeInteger(outStream, mask, prefixBits, nameIndex == -1 ? 0 : nameIndex);

            if (nameIndex == -1)
            {
                EncodeStringLiteral(outStream, header.Name);
            }

            EncodeStringLiteral(outStream, header.Value);
        }

        /**
         * Encode string literal according to 4.1.2.
         *
         * @param out The out to encode into
         * @param string The string to encode
         */
        private void EncodeStringLiteral(Stream outStream, byte[] str)
        {
            int huffmanLength = Huffman.Encoder.GetEncodedLength(str);

            if ((huffmanLength < str.Length && !forceHuffmanOff) || forceHuffmanOn)
            {
                EncodeInteger(outStream, 0x80, 7, huffmanLength);
                Huffman.Encoder.Encode(outStream, str);
            }
            else
            {
                EncodeInteger(outStream, 0x00, 7, str.Length);
                outStream.Write(str, 0, str.Length);
            }
        }

        private int GetNameIndex(byte[] name)
        {
            int index = HpackStaticTable.GetIndex(name);
            if (index == -1)
            {
                index = GetIndex(name);
                if (index >= 0)
                {
                    index += HpackStaticTable.Length;
                }
            }
            return index;
        }

        /**
         * Ensure that the header table has enough room to hold 'headerSize' more bytes.
         * Removes the oldest entry from the header table until sufficient space is available.
         */
        private void EnsureCapacity(int headerSize)
        {
            while (size + headerSize > capacity)
            {
                int index = Length();
                if (index == 0)
                {
                    break;
                }
                Remove();
            }
        }

        /**
         * Return the number of header fields in the header table.
         * Exposed for testing.
         */
        int Length()
        {
            return Size() == 0 ? 0 : head.after.index - head.before.index + 1;
        }

        /**
         * Return the size of the header table.
         * Exposed for testing.
         */
        int Size()
        {
            return size;
        }

        /**
         * Return the header field at the given index.
         * Exposed for testing.
         */
        HpackHeader GetHeaderField(int index)
        {
            HeaderEntry entry = head;
            while (index-- >= 0)
            {
                entry = entry.before;
            }
            return entry;
        }

        /**
         * Returns the header entry with the lowest index value for the header field.
         * Returns null if header field is not in the header table.
         */
        private HeaderEntry GetEntry(byte[] name, byte[] value)
        {
            if (Length() == 0 || name == null || value == null)
            {
                return null;
            }
            int h = Hash(name);
            int i = Index(h);
            for (HeaderEntry e = headerFields[i]; e != null; e = e.next)
            {
                if (e.hash == h &&
                    HpackHeader.ByteArraysEqual(name, e.Name) &&
                    HpackHeader.ByteArraysEqual(value, e.Value))
                {
                    return e;
                }
            }
            return null;
        }

        /**
         * Returns the lowest index value for the header field name in the header table.
         * Returns -1 if the header field name is not in the header table.
         */
        private int GetIndex(byte[] name)
        {
            if (Length() == 0 || name == null)
            {
                return -1;
            }
            int h = Hash(name);
            int i = Index(h);
            int index = -1;
            for (HeaderEntry e = headerFields[i]; e != null; e = e.next)
            {
                if (e.hash == h && HpackHeader.ByteArraysEqual(name, e.Name))
                {
                    index = e.index;
                    break;
                }
            }
            return GetIndex(index);
        }

        /**
         * Compute the index into the header table given the index in the header entry.
         */
        private int GetIndex(int index)
        {
            if (index == -1)
            {
                return index;
            }
            return index - head.before.index + 1;
        }

        /**
         * Add the header field to the header table.
         * Entries are evicted from the header table until the size of the table
         * and the new header field is less than the table's capacity.
         * If the size of the new entry is larger than the table's capacity,
         * the header table will be cleared.
         */
        private void Add(HpackHeader header)
        {
            int headerSize = header.Size;

            // Clear the table if the header field size is larger than the capacity.
            if (headerSize > capacity)
            {
                Clear();
                return;
            }

            // Evict oldest entries until we have enough capacity.
            while (size + headerSize > capacity)
            {
                Remove();
            }

            // Copy name and value that modifications of original do not affect the header table.
            byte[] name = new byte[header.Name.Length];
            Array.Copy(header.Name, name, name.Length);

            byte[] value = new byte[header.Value.Length];
            Array.Copy(header.Value, value, value.Length);

            int h = Hash(name);
            int i = Index(h);
            HeaderEntry old = headerFields[i];
            HeaderEntry e = new HeaderEntry(h, name, value, head.before.index - 1, old);
            headerFields[i] = e;
            e.AddBefore(head);
            size += headerSize;
        }

        /**
         * Remove and return the oldest header field from the header table.
         */
        private HpackHeader Remove()
        {
            if (size == 0)
            {
                return null;
            }
            HeaderEntry eldest = head.after;
            int h = eldest.hash;
            int i = Index(h);
            HeaderEntry prev = headerFields[i];
            HeaderEntry e = prev;
            while (e != null)
            {
                HeaderEntry next = e.next;
                if (e == eldest)
                {
                    if (prev == eldest)
                    {
                        headerFields[i] = next;
                    }
                    else
                    {
                        prev.next = next;
                    }
                    eldest.Remove();
                    size -= eldest.Size;
                    return eldest;
                }
                prev = e;
                e = next;
            }
            return null;
        }

        /**
         * Remove all entries from the header table.
         */
        private void Clear()
        {
            for (int i = 0; i < headerFields.Length; i++)
            {
                headerFields[i] = null;
            }
            head.before = head.after = head;
            this.size = 0;
        }

        /**
         * Returns the hash code for the given header field name.
         */
        private static int Hash(byte[] name)
        {
            int h = 0;
            for (int i = 0; i < name.Length; i++)
            {
                h = 31 * h + name[i];
            }
            if (h > 0)
            {
                return h;
            }
            else if (h == int.MinValue)
            {
                return int.MaxValue;
            }
            else
            {
                return -h;
            }
        }

        /**
         * Returns the index into the hash table for the hash code h.
         */
        private static int Index(int h)
        {
            return h % BUCKET_SIZE;
        }

        /**
         * A linked hash map HeaderField entry.
         */
        internal class HeaderEntry : HpackHeader
        {
            // These fields comprise the doubly linked list used for iteration.
            internal HeaderEntry before, after;

            // These fields comprise the chained list for header fields with the same hash.
            internal HeaderEntry next;
            internal int hash;

            // This is used to compute the index in the header table.
            internal int index;

            /**
             * Creates new entry.
             */
            internal HeaderEntry(int hash, byte[] name, byte[] value, int index, HeaderEntry next)
                : base(name, value)
            {
                this.index = index;
                this.hash = hash;
                this.next = next;
            }

            /**
             * Removes this entry from the linked list.
             */
            internal void Remove()
            {
                before.after = after;
                after.before = before;
            }

            /**
             * Inserts this entry before the specified existing entry in the list.
             */
            internal void AddBefore(HeaderEntry existingEntry)
            {
                after = existingEntry;
                before = existingEntry.before;
                before.after = this;
                after.before = this;
            }
        }
    }
}
