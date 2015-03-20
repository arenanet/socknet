using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Protocols.Http2.Hpack
{
    public class HpackDecoder
    {
        private static readonly Exception DECOMPRESSION_EXCEPTION =
      new Exception("decompression failure");
        private static readonly Exception ILLEGAL_INDEX_VALUE =
            new Exception("illegal index value");
        private static readonly Exception INVALID_MAX_DYNAMIC_TABLE_SIZE =
            new Exception("invalid max dynamic table size");
        private static readonly Exception MAX_DYNAMIC_TABLE_SIZE_CHANGE_REQUIRED =
            new Exception("max dynamic table size change required");

        public delegate void OnHeaderDelegate(HpackHeader header, bool sensitive);

        private static readonly byte[] EMPTY = { };

        private readonly HpackDynamicTable dynamicTable;

        private int maxHeaderSize;
        private int maxHpackDynamicTableSize;
        private int encoderMaxHpackDynamicTableSize;
        private bool maxHpackDynamicTableSizeChangeRequired;

        private long headerSize;
        private State state;
        private HpackHeader.IndexType indexType;
        private int index;
        private bool huffmanEncoded;
        private int skipLength;
        private int nameLength;
        private int valueLength;
        private byte[] name;

        private enum State
        {
            READ_HEADER_REPRESENTATION,
            READ_MAX_DYNAMIC_TABLE_SIZE,
            READ_INDEXED_HEADER,
            READ_INDEXED_HEADER_NAME,
            READ_LITERAL_HEADER_NAME_LENGTH_PREFIX,
            READ_LITERAL_HEADER_NAME_LENGTH,
            READ_LITERAL_HEADER_NAME,
            SKIP_LITERAL_HEADER_NAME,
            READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX,
            READ_LITERAL_HEADER_VALUE_LENGTH,
            READ_LITERAL_HEADER_VALUE,
            SKIP_LITERAL_HEADER_VALUE
        }

        public HpackDecoder(int maxHeaderSize, int maxHeaderTableSize)
        {
            dynamicTable = new HpackDynamicTable(maxHeaderTableSize);
            this.maxHeaderSize = maxHeaderSize;
            maxHpackDynamicTableSize = maxHeaderTableSize;
            encoderMaxHpackDynamicTableSize = maxHeaderTableSize;
            maxHpackDynamicTableSizeChangeRequired = false;
            Reset();
        }

        private void Reset()
        {
            headerSize = 0;
            state = State.READ_HEADER_REPRESENTATION;
            indexType = HpackHeader.IndexType.NONE;
        }

        /**
         * Decode the header block into header fields.
         */
        public void Decode(Stream inStream, OnHeaderDelegate headerListener)
        {
            while (inStream.Position < inStream.Length)
            {
                switch (state)
                {
                    case State.READ_HEADER_REPRESENTATION:
                        byte b = (byte)inStream.ReadByte();
                        if (maxHpackDynamicTableSizeChangeRequired && (b & 0xE0) != 0x20)
                        {
                            // Encoder MUST signal maximum dynamic table size change
                            throw MAX_DYNAMIC_TABLE_SIZE_CHANGE_REQUIRED;
                        }
                        if (b < 0)
                        {
                            // Indexed Header Field
                            index = b & 0x7F;
                            if (index == 0)
                            {
                                throw ILLEGAL_INDEX_VALUE;
                            }
                            else if (index == 0x7F)
                            {
                                state = State.READ_INDEXED_HEADER;
                            }
                            else
                            {
                                IndexHeader(index, headerListener);
                            }
                        }
                        else if ((b & 0x40) == 0x40)
                        {
                            // Literal Header Field with Incremental Indexing
                            indexType = HpackHeader.IndexType.INCREMENTAL;
                            index = b & 0x3F;
                            if (index == 0)
                            {
                                state = State.READ_LITERAL_HEADER_NAME_LENGTH_PREFIX;
                            }
                            else if (index == 0x3F)
                            {
                                state = State.READ_INDEXED_HEADER_NAME;
                            }
                            else
                            {
                                // Index was stored as the prefix
                                readName(index);
                                state = State.READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                            }
                        }
                        else if ((b & 0x20) == 0x20)
                        {
                            // Dynamic Table Size Update
                            index = b & 0x1F;
                            if (index == 0x1F)
                            {
                                state = State.READ_MAX_DYNAMIC_TABLE_SIZE;
                            }
                            else
                            {
                                setHpackDynamicTableSize(index);
                                state = State.READ_HEADER_REPRESENTATION;
                            }
                        }
                        else
                        {
                            // Literal Header Field without Indexing / never Indexed
                            indexType = ((b & 0x10) == 0x10) ? HpackHeader.IndexType.NEVER : HpackHeader.IndexType.NONE;
                            index = b & 0x0F;
                            if (index == 0)
                            {
                                state = State.READ_LITERAL_HEADER_NAME_LENGTH_PREFIX;
                            }
                            else if (index == 0x0F)
                            {
                                state = State.READ_INDEXED_HEADER_NAME;
                            }
                            else
                            {
                                // Index was stored as the prefix
                                readName(index);
                                state = State.READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                            }
                        }
                        break;

                    case State.READ_MAX_DYNAMIC_TABLE_SIZE:
                        int maxSize = DecodeULE128(inStream);
                        if (maxSize == -1)
                        {
                            return;
                        }

                        // Check for numerical overflow
                        if (maxSize > int.MaxValue - index)
                        {
                            throw DECOMPRESSION_EXCEPTION;
                        }

                        setHpackDynamicTableSize(index + maxSize);
                        state = State.READ_HEADER_REPRESENTATION;
                        break;

                    case State.READ_INDEXED_HEADER:
                        int headerIndex = DecodeULE128(inStream);
                        if (headerIndex == -1)
                        {
                            return;
                        }

                        // Check for numerical overflow
                        if (headerIndex > int.MaxValue - index)
                        {
                            throw DECOMPRESSION_EXCEPTION;
                        }

                        IndexHeader(index + headerIndex, headerListener);
                        state = State.READ_HEADER_REPRESENTATION;
                        break;

                    case State.READ_INDEXED_HEADER_NAME:
                        // Header Name matches an entry in the Header Table
                        int nameIndex = DecodeULE128(inStream);
                        if (nameIndex == -1)
                        {
                            return;
                        }

                        // Check for numerical overflow
                        if (nameIndex > int.MaxValue - index)
                        {
                            throw DECOMPRESSION_EXCEPTION;
                        }

                        readName(index + nameIndex);
                        state = State.READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                        break;

                    case State.READ_LITERAL_HEADER_NAME_LENGTH_PREFIX:
                        b = (byte)inStream.ReadByte();
                        huffmanEncoded = (b & 0x80) == 0x80;
                        index = b & 0x7F;
                        if (index == 0x7f)
                        {
                            state = State.READ_LITERAL_HEADER_NAME_LENGTH;
                        }
                        else
                        {
                            nameLength = index;

                            // Disallow empty names -- they cannot be represented in HTTP/1.x
                            if (nameLength == 0)
                            {
                                throw DECOMPRESSION_EXCEPTION;
                            }

                            // Check name length against max header size
                            if (ExceedsMaxHeaderSize(nameLength))
                            {

                                if (indexType == HpackHeader.IndexType.NONE)
                                {
                                    // Name is unused so skip bytes
                                    name = EMPTY;
                                    skipLength = nameLength;
                                    state = State.SKIP_LITERAL_HEADER_NAME;
                                    break;
                                }

                                // Check name length against max dynamic table size
                                if (nameLength + HpackHeader.HEADER_ENTRY_OVERHEAD > dynamicTable.Capacity())
                                {
                                    dynamicTable.Clear();
                                    name = EMPTY;
                                    skipLength = nameLength;
                                    state = State.SKIP_LITERAL_HEADER_NAME;
                                    break;
                                }
                            }
                            state = State.READ_LITERAL_HEADER_NAME;
                        }
                        break;

                    case State.READ_LITERAL_HEADER_NAME_LENGTH:
                        // Header Name is a Literal String
                        nameLength = DecodeULE128(inStream);
                        if (nameLength == -1)
                        {
                            return;
                        }

                        // Check for numerical overflow
                        if (nameLength > int.MaxValue - index)
                        {
                            throw DECOMPRESSION_EXCEPTION;
                        }
                        nameLength += index;

                        // Check name length against max header size
                        if (ExceedsMaxHeaderSize(nameLength))
                        {
                            if (indexType == HpackHeader.IndexType.NONE)
                            {
                                // Name is unused so skip bytes
                                name = EMPTY;
                                skipLength = nameLength;
                                state = State.SKIP_LITERAL_HEADER_NAME;
                                break;
                            }

                            // Check name length against max dynamic table size
                            if (nameLength + HpackHeader.HEADER_ENTRY_OVERHEAD > dynamicTable.Capacity())
                            {
                                dynamicTable.Clear();
                                name = EMPTY;
                                skipLength = nameLength;
                                state = State.SKIP_LITERAL_HEADER_NAME;
                                break;
                            }
                        }
                        state = State.READ_LITERAL_HEADER_NAME;
                        break;

                    case State.READ_LITERAL_HEADER_NAME:
                        // Wait until entire name is readable
                        if (inStream.Length - inStream.Position < nameLength)
                        {
                            return;
                        }

                        name = ReadStringLiteral(inStream, nameLength);

                        state = State.READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                        break;

                    case State.SKIP_LITERAL_HEADER_NAME:
                        skipLength -= Skip(inStream, skipLength);

                        if (skipLength == 0)
                        {
                            state = State.READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                        }
                        break;

                    case State.READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX:
                        b = (byte)inStream.ReadByte();
                        huffmanEncoded = (b & 0x80) == 0x80;
                        index = b & 0x7F;
                        if (index == 0x7f)
                        {
                            state = State.READ_LITERAL_HEADER_VALUE_LENGTH;
                        }
                        else
                        {
                            valueLength = index;

                            // Check new header size against max header size
                            long newHeaderSize = (long)nameLength + (long)valueLength;
                            if (ExceedsMaxHeaderSize(newHeaderSize))
                            {
                                // truncation will be reported during endHeaderBlock
                                headerSize = maxHeaderSize + 1;

                                if (indexType == HpackHeader.IndexType.NONE)
                                {
                                    // Value is unused so skip bytes
                                    state = State.SKIP_LITERAL_HEADER_VALUE;
                                    break;
                                }

                                // Check new header size against max dynamic table size
                                if (newHeaderSize + HpackHeader.HEADER_ENTRY_OVERHEAD > dynamicTable.Capacity())
                                {
                                    dynamicTable.Clear();
                                    state = State.SKIP_LITERAL_HEADER_VALUE;
                                    break;
                                }
                            }

                            if (valueLength == 0)
                            {
                                InsertHeader(headerListener, name, EMPTY, indexType);
                                state = State.READ_HEADER_REPRESENTATION;
                            }
                            else
                            {
                                state = State.READ_LITERAL_HEADER_VALUE;
                            }
                        }

                        break;

                    case State.READ_LITERAL_HEADER_VALUE_LENGTH:
                        // Header Value is a Literal String
                        valueLength = DecodeULE128(inStream);
                        if (valueLength == -1)
                        {
                            return;
                        }

                        // Check for numerical overflow
                        if (valueLength > int.MaxValue - index)
                        {
                            throw DECOMPRESSION_EXCEPTION;
                        }
                        valueLength += index;

                        // Check new header size against max header size
                        long rHeaderSize = (long)nameLength + (long)valueLength;
                        if (rHeaderSize + headerSize > maxHeaderSize)
                        {
                            // truncation will be reported during endHeaderBlock
                            headerSize = maxHeaderSize + 1;

                            if (indexType == HpackHeader.IndexType.NONE)
                            {
                                // Value is unused so skip bytes
                                state = State.SKIP_LITERAL_HEADER_VALUE;
                                break;
                            }

                            // Check new header size against max dynamic table size
                            if (rHeaderSize + HpackHeader.HEADER_ENTRY_OVERHEAD > dynamicTable.Capacity())
                            {
                                dynamicTable.Clear();
                                state = State.SKIP_LITERAL_HEADER_VALUE;
                                break;
                            }
                        }
                        state = State.READ_LITERAL_HEADER_VALUE;
                        break;

                    case State.READ_LITERAL_HEADER_VALUE:
                        // Wait until entire value is readable
                        if (inStream.Length - inStream.Position < valueLength)
                        {
                            return;
                        }

                        byte[] value = ReadStringLiteral(inStream, valueLength);
                        InsertHeader(headerListener, name, value, indexType);
                        state = State.READ_HEADER_REPRESENTATION;
                        break;

                    case State.SKIP_LITERAL_HEADER_VALUE:
                        valueLength -= Skip(inStream, valueLength);

                        if (valueLength == 0)
                        {
                            state = State.READ_HEADER_REPRESENTATION;
                        }
                        break;

                    default:
                        throw new Exception("should not reach here");
                }
            }
        }

        private static int Skip(Stream stream, int skip)
        {
            skip = Math.Min((int)(stream.Length - stream.Position), skip);
            stream.Position += skip;
            return skip;
        }

        /**
         * End the current header block. Returns if the header field has been truncated.
         * This must be called after the header block has been completely decoded.
         */
        public bool EndHeaderBlock()
        {
            bool truncated = headerSize > maxHeaderSize;
            Reset();
            return truncated;
        }

        /**
         * Set the maximum header table size.
         * If this is below the maximum size of the header table used by the encoder,
         * the beginning of the next header block MUST signal this change.
         */
        public void SetMaxHeaderTableSize(int maxHeaderTableSize)
        {
            this.maxHpackDynamicTableSize = maxHeaderTableSize;
            if (maxHeaderTableSize < encoderMaxHpackDynamicTableSize)
            {
                // decoder requires less space than encoder
                // encoder MUST signal this change
                maxHpackDynamicTableSizeChangeRequired = true;
                dynamicTable.SetCapacity(maxHeaderTableSize);
            }
        }

        /**
         * Return the maximum header table size.
         * This is the maximum size allowed by both the encoder and the decoder.
         */
        public int GetMaxHeaderTableSize()
        {
            return dynamicTable.Capacity();
        }

        /**
         * Return the number of header fields in the header table.
         * Exposed for testing.
         */
        int Length()
        {
            return dynamicTable.Length();
        }

        /**
         * Return the size of the header table.
         * Exposed for testing.
         */
        int Size()
        {
            return dynamicTable.Size();
        }

        /**
         * Return the header field at the given index.
         * Exposed for testing.
         */
        HpackHeader getHeaderField(int index)
        {
            return dynamicTable.GetEntry(index + 1);
        }

        private void setHpackDynamicTableSize(int HpackDynamicTableSize)
        {
            if (HpackDynamicTableSize > maxHpackDynamicTableSize)
            {
                throw INVALID_MAX_DYNAMIC_TABLE_SIZE;
            }
            encoderMaxHpackDynamicTableSize = HpackDynamicTableSize;
            maxHpackDynamicTableSizeChangeRequired = false;
            dynamicTable.SetCapacity(HpackDynamicTableSize);
        }

        private void readName(int index)
        {
            if (index <= HpackStaticTable.Length)
            {
                HpackHeader headerField = HpackStaticTable.GetEntry(index);
                name = headerField.Name;
            }
            else if (index - HpackStaticTable.Length <= dynamicTable.Length())
            {
                HpackHeader headerField = dynamicTable.GetEntry(index - HpackStaticTable.Length);
                name = headerField.Name;
            }
            else
            {
                throw ILLEGAL_INDEX_VALUE;
            }
        }

        private void IndexHeader(int index, OnHeaderDelegate headerListener)
        {
            if (index <= HpackStaticTable.Length)
            {
                HpackHeader headerField = HpackStaticTable.GetEntry(index);
                AddHeader(headerListener, headerField.Name, headerField.Value, false);
            }
            else if (index - HpackStaticTable.Length <= dynamicTable.Length())
            {
                HpackHeader headerField = dynamicTable.GetEntry(index - HpackStaticTable.Length);
                AddHeader(headerListener, headerField.Name, headerField.Value, false);
            }
            else
            {
                throw ILLEGAL_INDEX_VALUE;
            }
        }

        private void InsertHeader(OnHeaderDelegate headerListener, byte[] name, byte[] value, HpackHeader.IndexType indexType)
        {
            AddHeader(headerListener, name, value, indexType == HpackHeader.IndexType.NEVER);

            switch (indexType)
            {
                case HpackHeader.IndexType.NONE:
                    goto case HpackHeader.IndexType.NEVER;
                case HpackHeader.IndexType.NEVER:
                    break;

                case HpackHeader.IndexType.INCREMENTAL:
                    dynamicTable.Add(new HpackHeader(name, value));
                    break;

                default:
                    throw new Exception("should not reach here");
            }
        }

        private void AddHeader(OnHeaderDelegate headerListener, byte[] name, byte[] value, bool sensitive)
        {
            if (name.Length == 0)
            {
                throw new ArgumentException("name is empty");
            }
            long newSize = headerSize + name.Length + value.Length;
            if (newSize <= maxHeaderSize)
            {
                headerListener(new HpackHeader(name, value), sensitive);
                headerSize = (int)newSize;
            }
            else
            {
                // truncation will be reported during endHeaderBlock
                headerSize = maxHeaderSize + 1;
            }
        }

        private bool ExceedsMaxHeaderSize(long size)
        {
            // Check new header size against max header size
            if (size + headerSize <= maxHeaderSize)
            {
                return false;
            }

            // truncation will be reported during endHeaderBlock
            headerSize = maxHeaderSize + 1;
            return true;
        }

        private byte[] ReadStringLiteral(Stream inStream, int length)
        {
            byte[] buf = new byte[length];
            if (inStream.Read(buf, 0, length) != length)
            {
                throw DECOMPRESSION_EXCEPTION;
            }

            if (huffmanEncoded)
            {
                MemoryStream decodedStream = new MemoryStream();
                Huffman.Decoder.Decode(buf, decodedStream);
                buf = new byte[decodedStream.Position];
                decodedStream.Position = 0;
                decodedStream.Read(buf, 0, buf.Length);
            }

            return buf;
        }

        // Unsigned Little Endian Base 128 Variable-Length Integer Encoding
        private static int DecodeULE128(Stream inStream)
        {
            long startPosition = inStream.Position;
            int result = 0;
            int shift = 0;
            while (shift < 32)
            {
                if (inStream.Length - inStream.Position < 1)
                {
                    // Buffer does not contain entire integer,
                    // reset reader index and return -1.
                    inStream.Position = startPosition;
                    return -1;
                }
                byte b = (byte)inStream.ReadByte();
                if (shift == 28 && (b & 0xF8) != 0)
                {
                    break;
                }
                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }
                shift += 7;
            }
            // Value exceeds int.MaxValue
            inStream.Position = startPosition;
            throw DECOMPRESSION_EXCEPTION;
        }
    }
}
