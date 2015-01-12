using System;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Buffer
{
    public class ByteBuffer
    {
        public int ReadPosition { set; get; }

        public int WritePosition { set; get; }

        public int RawBufferSize { private set; get; }

        public int WriteBytesLeft
        {
            get
            {
                return RawBufferSize - WritePosition;
            }
        }

        public int ReadBytesLeft
        {
            get
            {
                return WritePosition - ReadPosition;
            }
        }

        public Endian ByteOrder { private set; get; }

        public enum Endian
        {
            Little,
            Big
        }

        private readonly ByteBufferPool bufferPool;

        private readonly LinkedList<byte[]> buffers = new LinkedList<byte[]>();

        public ByteBuffer() : this(BitConverter.IsLittleEndian ? Endian.Little : Endian.Big, ByteBufferPool.DEFAULT)
        {

        }

        public ByteBuffer(Endian byteOrder)
            : this(byteOrder, ByteBufferPool.DEFAULT)
        {

        }

        public ByteBuffer(Endian byteOrder, ByteBufferPool bufferPool)
        {
            this.ByteOrder = byteOrder;
            this.bufferPool = bufferPool;
        }

        private void EnsureWriteSize(int size)
        {
            lock (buffers)
            {
                if (WriteBytesLeft < size)
                {
                    foreach (byte[] buffer in bufferPool.Retrieve(size - WriteBytesLeft))
                    {
                        RawBufferSize += buffer.Length;
                        buffers.AddLast(buffer);
                    }
                }
            }
        }

        public ByteBuffer WriteByte(byte byt)
        {
            lock (buffers)
            {
                EnsureWriteSize(sizeof(byte));

                WriteBytes(new byte[] { byt });
            }

            return this;
        }

        public ByteBuffer WriteChar(char character)
        {
            byte[] bytes = BitConverter.GetBytes(character);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            lock (buffers)
            {
                EnsureWriteSize(sizeof(char));

                WriteBytes(bytes);
            }

            return this;
        }

        public ByteBuffer WriteShort(short int16)
        {
            byte[] bytes = BitConverter.GetBytes(int16);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            lock (buffers)
            {
                EnsureWriteSize(sizeof(short));

                WriteBytes(bytes);
            }

            return this;
        }

        public ByteBuffer WriteUShort(ushort uint16)
        {
            byte[] bytes = BitConverter.GetBytes(uint16);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            lock (buffers)
            {
                EnsureWriteSize(sizeof(ushort));

                WriteBytes(bytes);
            }

            return this;
        }

        public ByteBuffer WriteInt(int int32)
        {
            byte[] bytes = BitConverter.GetBytes(int32);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            lock (buffers)
            {
                EnsureWriteSize(sizeof(int));

                WriteBytes(bytes);
            }

            return this;
        }

        public ByteBuffer WriteUInt(uint uint32)
        {
            byte[] bytes = BitConverter.GetBytes(uint32);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            lock (buffers)
            {
                EnsureWriteSize(sizeof(uint));

                WriteBytes(bytes);
            }

            return this;
        }

        public ByteBuffer WriteLong(long int64)
        {
            byte[] bytes = BitConverter.GetBytes(int64);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            lock (buffers)
            {
                EnsureWriteSize(sizeof(long));

                WriteBytes(bytes);
            }

            return this;
        }

        public ByteBuffer WriteULong(ulong uint64)
        {
            byte[] bytes = BitConverter.GetBytes(uint64);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            lock (buffers)
            {
                EnsureWriteSize(sizeof(ulong));

                WriteBytes(bytes);
            }

            return this;
        }

        public ByteBuffer WriteFloat(float single)
        {
            byte[] bytes = BitConverter.GetBytes(single);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            lock (buffers)
            {
                EnsureWriteSize(sizeof(float));

                WriteBytes(bytes);
            }

            return this;
        }

        public ByteBuffer WriteDouble(double dbl)
        {
            byte[] bytes = BitConverter.GetBytes(dbl);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            lock (buffers)
            {
                EnsureWriteSize(sizeof(double));

                WriteBytes(bytes);
            }

            return this;
        }

        public ByteBuffer WriteString(string str, System.Text.Encoding encoding)
        {
            byte[] bytes = encoding.GetBytes(str);

            lock (buffers)
            {
                EnsureWriteSize(bytes.Length);

                WriteBytes(bytes);
            }

            return this;
        }

        public ByteBuffer WriteBytes(byte[] bytes)
        {
            return WriteBytes(bytes, 0, bytes.Length);
        }

        public ByteBuffer WriteBytes(byte[] bytes, int offset, int length)
        {
            lock (buffers)
            {
                EnsureWriteSize(bytes.Length);

                int bytesWritten = 0;

                // find node that we can initially write to
                LinkedListNode<byte[]> writeNode = null;
                int bytesScanned = 0;

                // write sequentally to buffers
                while (bytesWritten < length - bytesWritten)
                {
                    FindWriteNode(ref writeNode, ref bytesScanned);

                    int bufferWritePosition = WritePosition - bytesScanned;
                    int bufferWriteLength = Math.Min(writeNode.Value.Length - bufferWritePosition, length - bytesWritten);

                    System.Buffer.BlockCopy(bytes, bytesWritten, writeNode.Value, bufferWritePosition, bufferWriteLength);

                    bytesWritten += bufferWriteLength;
                    WritePosition += bufferWriteLength;
                }
            }

            return this;
        }

        public byte ReadByte()
        {
            int size = sizeof(byte);

            byte[] data = new byte[size];

            ReadBytes(data, 0, size);

            return data[0];
        }

        public ByteBuffer ReadByte(out byte byt)
        {
            byt = ReadByte();

            return this;
        }

        public char ReadChar()
        {
            int size = sizeof(char);

            byte[] bytes = new byte[size];

            ReadBytes(bytes, 0, size);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToChar(bytes, 0);
        }

        public ByteBuffer ReadChar(out char character)
        {
            character = ReadChar();

            return this;
        }

        public short ReadShort()
        {
            int size = sizeof(short);

            byte[] bytes = new byte[size];

            ReadBytes(bytes, 0, size);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToInt16(bytes, 0);
        }

        public ByteBuffer ReadShort(out short int16)
        {
            int16 = ReadShort();

            return this;
        }

        public ushort ReadUShort()
        {
            int size = sizeof(ushort);

            byte[] bytes = new byte[size];

            ReadBytes(bytes, 0, size);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToUInt16(bytes, 0);
        }

        public ByteBuffer ReadUShort(out ushort uint16)
        {
            uint16 = ReadUShort();

            return this;
        }

        public int ReadInt()
        {
            int size = sizeof(int);

            byte[] bytes = new byte[size];

            ReadBytes(bytes, 0, size);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToInt32(bytes, 0);
        }

        public ByteBuffer ReadInt(out int int32)
        {
            int32 = ReadInt();

            return this;
        }

        public uint ReadUInt()
        {
            int size = sizeof(uint);

            byte[] bytes = new byte[size];

            ReadBytes(bytes, 0, size);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToUInt32(bytes, 0);
        }

        public ByteBuffer ReadUInt(out uint uint32)
        {
            uint32 = ReadUInt();

            return this;
        }

        public long ReadLong()
        {
            int size = sizeof(long);

            byte[] bytes = new byte[size];

            ReadBytes(bytes, 0, size);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToInt64(bytes, 0);
        }

        public ByteBuffer ReadLong(out long int64)
        {
            int64 = ReadLong();

            return this;
        }

        public ulong ReadULong()
        {
            int size = sizeof(ulong);

            byte[] bytes = new byte[size];

            ReadBytes(bytes, 0, size);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToUInt64(bytes, 0);
        }

        public ByteBuffer ReadULong(out ulong uint64)
        {
            uint64 = ReadULong();

            return this;
        }

        public float ReadFloat()
        {
            int size = sizeof(float);

            byte[] bytes = new byte[size];

            ReadBytes(bytes, 0, size);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToSingle(bytes, 0);
        }

        public ByteBuffer ReadFloat(out float single)
        {
            single = ReadFloat();

            return this;
        }

        public double ReadDouble()
        {
            int size = sizeof(double);

            byte[] bytes = new byte[size];

            ReadBytes(bytes, 0, size);

            if (BitConverter.IsLittleEndian && ByteOrder != Endian.Little)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToDouble(bytes, 0);
        }

        public ByteBuffer ReadDouble(out double dbl)
        {
            dbl = ReadDouble();

            return this;
        }

        public string ReadString(int length, System.Text.Encoding encoding)
        {
            byte[] bytes = new byte[length];

            ReadBytes(bytes);

            return encoding.GetString(bytes);
        }

        public ByteBuffer ReadString(out string str, int length, System.Text.Encoding encoding)
        {
            str = ReadString(length, encoding);

            return this;
        }

        public byte[] ReadBytes(int length)
        {
            byte[] bytes = new byte[length];

            ReadBytes(bytes);

            return bytes;
        }

        public ByteBuffer ReadBytes(byte[] bytes)
        {
            return ReadBytes(bytes, 0, bytes.Length);
        }

        public ByteBuffer ReadBytes(byte[] bytes, int offset, int length)
        {
            lock (buffers)
            {
                if (length > ReadBytesLeft)
                {
                    throw new ArgumentOutOfRangeException("The length exceeded bytes left.");
                }

                int bytesRead = 0;

                // find node that we can initially readFrom
                LinkedListNode<byte[]> readNode = null;
                int bytesScanned = 0;

                // read sequentally from buffers
                while (bytesRead < length)
                {
                    FindReadNode(ref readNode, ref bytesScanned);

                    int nodeOffset = ReadPosition - bytesScanned;
                    int bytesToRead = Math.Min(length - bytesRead, readNode.Value.Length - nodeOffset);

                    System.Buffer.BlockCopy(readNode.Value, nodeOffset, bytes, offset + bytesRead, bytesToRead);

                    bytesRead += bytesToRead;
                    ReadPosition += bytesToRead;
                }
            }

            return this;
        }

        public int SeekTo(byte[] bytes)
        {
            int position = -1;

            lock (buffers)
            {
                byte checkByte = 1;

                for (int i = 0; i < bytes.Length; i++)
                {
                    
                }
            }

            return position;
        }

        public void CleanUp()
        {
            lock (buffers)
            {
                LinkedListNode<byte[]> currentNode = null;
                int bytesScanned = 0;

                FindLeastNode(ref currentNode, ref bytesScanned);

                while (currentNode != null)
                {
                    bufferPool.Release(currentNode.Value);

                    LinkedListNode<byte[]> prevNode = currentNode;

                    currentNode = currentNode.Previous;

                    buffers.Remove(prevNode);
                }
            }
        }

        public void Reset()
        {
            lock (buffers)
            {
                bufferPool.Release(buffers);

                buffers.Clear();
            }
        }

        private void FindLeastNode(ref LinkedListNode<byte[]> leastNode, ref int bytesScanned)
        {
            leastNode = leastNode != null ? leastNode : buffers.First;

            int leastPosition = Math.Min(WritePosition, ReadPosition);

            while (leastNode != null)
            {
                if (leastPosition < bytesScanned + leastNode.Value.Length)
                {
                    break;
                }

                bytesScanned += leastNode.Value.Length;
                leastNode = leastNode.Next;
            }
        }

        private void FindWriteNode(ref LinkedListNode<byte[]> writeNode, ref int bytesScanned)
        {
            writeNode = writeNode != null ? writeNode : buffers.First;

            while (writeNode != null)
            {
                if (WritePosition < bytesScanned + writeNode.Value.Length)
                {
                    break;
                }

                bytesScanned += writeNode.Value.Length;
                writeNode = writeNode.Next;
            }
        }

        private void FindReadNode(ref LinkedListNode<byte[]> readNode, ref int bytesScanned)
        {
            readNode = readNode != null ? readNode : buffers.First;

            while (readNode != null)
            {
                if (ReadPosition < bytesScanned + readNode.Value.Length)
                {
                    break;
                }

                bytesScanned += readNode.Value.Length;
                readNode = readNode.Next;
            }
        }
    }
}
