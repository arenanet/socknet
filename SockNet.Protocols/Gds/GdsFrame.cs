/*
 * Copyright 2015 ArenaNet, LLC.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this 
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * 	 http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under 
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF 
 * ANY KIND, either express or implied. See the License for the specific language governing 
 * permissions and limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using Ionic.Zlib;

namespace ArenaNet.SockNet.Protocols.Gds
{
    /// <summary>
    /// Represents a Gds frame.
    /// 
    /// ===========================================================================
    /// GDS (Generic Data Stream) Application Protocol
    /// ---------------------------------------------------------------------------
    /// 
    /// +--------------------+
    /// | Frame Definition   |
    /// +--------------------+
    /// | Headers (Optional) |
    /// +--------------------+
    /// | Body (Optional)    |
    /// +--------------------+
    /// 
    /// The only part in the protocol that is required is the frame definition
    /// which is 32 bits. 
    /// 
    /// Stream
    /// ------
    /// All frames belong to a stream. Streams get created when a frame targeted
    /// towards a stream is sent across the wire. A connected system can send
    /// a "Close" frame to close a stream with a particular ID, at which point
    /// the Stream ID can be reused.
    /// 
    /// Fragmentation
    /// -------------
    /// Fragmentation is implemented by sending a 0 for the "Is Complete" field
    /// in the frame definition. The way that the body and headers behave is
    /// different while fragmented. Headers will be evaluated per fragment (which
    /// means that the headers have to be in a valid format per fragment). I.e. if
    /// fragment 1 sets the header "foo" to "bar" and fragment 2 sets "foo" to
    /// "bar2", then the value of foo will become "bar2". The body content is
    /// usually sliced between fragments - which means that the content doesn't
    /// have to be valid until the message is complete. The last fragmented
    /// frame of a message has the send a 1 for the "Is Complete" field.
    ///  
    /// Compression
    /// -----------
    /// Compression is applied to the headers using DEFLATE (or gzip) if the
    /// "Is Compressed" flag is set to 1 on the Headers block. The compression is
    /// applied to the entire Headers block (excluding the Flag and total Length) 
    /// field.
    /// 
    /// ===========================================================================
    /// Frame Definition
    /// ---------------------------------------------------------------------------
    /// 
    /// +-+-----+----+----------------------------+
    /// |C|Res  |Type| Stream Id (24)             |
    /// +-+-----+----+----------------------------+
    ///  
    /// 1) Is Complete (1-bit)
    /// Whether the message in the current stream is complete or chunked.
    ///  
    /// 2) Reserved (3-bit)
    /// Reserved for future flags.
    /// 
    /// 3) Type (4-bit)
    /// Valid types are:
    /// 0000 - RESERVED
    /// 0001 - Headers Only (No Body)
    /// 0010 - Body Only (No Headers)
    /// 0011 - Header and Body (Everything)
    /// 0100 - RESERVED
    /// 0101 - RESERVED
    /// 0110 - RESERVED
    /// 0111 - RESERVED
    /// 1000 - Ping (No Headers or Body)
    /// 1001 - Pong (No Headers or Body)
    /// 1010 - RESERVED
    /// 1011 - RESERVED
    /// 1100 - RESERVED
    /// 1101 - RESERVED
    /// 1110 - RESERVED
    /// 1111 - Close (No Headers or Body)
    /// 
    /// 4) Stream ID (24-bit)
    /// The stream identifier. Unique per connection.
    /// 
    /// ===========================================================================
    /// Headers
    /// ---------------------------------------------------------------------------
    /// 
    /// +-+---------------+
    /// |X| Count (15)    |
    /// +-+---------------+
    /// 
    /// +----------------+----------------+
    /// | Name Length    | Value Length   |
    /// +----------------+----------------+
    /// | Name                          ...
    /// +----------------------------------
    /// | Value                         ...
    /// +----------------------------------
    /// 
    /// 1) Is Compressed (1-bit)
    /// Whether the headers are compressed using DEFLATE.
    /// 
    /// 2) Count (15-bit)
    /// The number of the headers. The value is an unsigned short integer.
    ///
    /// 3) Name Length (16-bit)
    /// The length of the name field. The value is an unsigned short integer.
    ///
    /// 4) Value Length (16-bit)
    /// The length of the value field. The value is an unsigned short integer.
    ///
    /// 5) Name
    /// The name of the header.
    ///
    /// 6) Value
    /// The value of the header.
    ///
    /// ===========================================================================
    /// Body
    /// ---------------------------------------------------------------------------
    ///
    /// +--------------------------------+
    /// | Body Length (32)               |
    /// +--------------------------------+
    /// | Body                         ...
    /// +---------------------------------
    ///
    /// 1) Body Length (32-bit)
    /// The size of the body. The value is an unsigned integer.
    ///
    /// 2) Body
    /// The body.
    ///
    /// ===========================================================================
    /// 
    /// </summary>
    public class GdsFrame
    {
        // Masks and Shifts
        private static uint StreamIdMask = Convert.ToUInt32("00000000111111111111111111111111", 2);
        private static int StreamIdShift = 0;

        private static uint TypeMask = Convert.ToUInt32("00001111000000000000000000000000", 2);
        private static int TypeShift = 24;

        private static uint IsCompleteMask = Convert.ToUInt32("10000000000000000000000000000000", 2);
        private static int IsCompleteShift = 31;

        private static uint HeadersIsCompressedMask = Convert.ToUInt32("1000000000000000", 2);
        private static int HeadersIsCompressedShift = 15;

        private static uint HeadersLengthMask = Convert.ToUInt32("0111111111111111", 2);
        private static int HeadersLengthShift = 0;
        
        /// <summary>
        /// Whether or not this frame is complete.
        /// </summary>
        public bool IsComplete { private set; get; }

        /// <summary>
        /// The type of frame.
        /// </summary>
        public GdsFrameType Type { private set; get; }
        public enum GdsFrameType : byte
        {
            HeadersOnly = 1,
            BodyOnly = 2,
            Full = 3,
            Ping = 8,
            Pong = 9,
            Close = 15
        }

        /// <summary>
        /// The stream identifier.
        /// </summary>
        public uint StreamId { private set; get; }

        /// <summary>
        /// Internal headers.
        /// </summary>
        internal Dictionary<byte[], byte[]> headers = new Dictionary<byte[], byte[]>(new ByteArrayComparer());

        /// <summary>
        /// External header view and manipulator that deals with headers.
        /// </summary>
        public class HeaderView
        {
            private GdsFrame parent;

            internal HeaderView(GdsFrame parent)
            {
                this.parent = parent;
            }

            public void Remove(byte[] name)
            {
                this[name] = null;
            }

            public int Count { get { return parent.headers.Count; } }

            public byte[] this[byte[] name]
            {
                get
                {
                    lock (parent.headers)
                    {
                        byte[] value;

                        if (parent.headers.TryGetValue(name, out value))
                        {
                            return value;
                        }
                        else
                        {
                            return null;
                        }
                    }
                }
                set
                {
                    lock (parent.headers)
                    {
                        if (value == null)
                        {
                            parent.headers.Remove(name);
                        }
                        else
                        {
                            parent.headers[name] = value;
                        }
                    }
                }
            }
        }

        internal readonly HeaderView _headerView;
        /// <summary>
        /// View and manipulation of headers.
        /// </summary>
        public HeaderView Headers { get { return _headerView; } }

        /// <summary>
        /// Whether the headers are compressed.
        /// </summary>
        public bool AreHeadersCompressed { private set; get; }

        /// <summary>
        /// The body.
        /// </summary>
        public byte[] Body { private set; get; }

        /// <summary>
        /// Creates a Gds frame.
        /// </summary>
        private GdsFrame()
        {
            _headerView = new HeaderView(this);
        }

        /// <summary>
        /// Writes this frame into the stream.
        /// </summary>
        /// <param name="stream"></param>
        public void Write(Stream stream)
        {
            BinaryWriter writer = new BinaryWriter(stream);

            uint frameDefinition = 0;

            frameDefinition |= (uint)((uint)(IsComplete ? 1 : 0) << IsCompleteShift);
            frameDefinition |= (uint)((uint)(Type) << TypeShift);
            frameDefinition |= (uint)(StreamId << StreamIdShift);

            writer.Write((uint)IPAddress.HostToNetworkOrder((int)frameDefinition));

            if (Type == GdsFrameType.HeadersOnly || Type == GdsFrameType.Full)
            {
                ushort headerDefinition = 0;
                headerDefinition |= (ushort)((ushort)(AreHeadersCompressed ? 1 : 0) << HeadersIsCompressedShift);
                headerDefinition |= (ushort)((ushort)(headers.Count) << HeadersLengthShift);

                writer.Write((ushort)IPAddress.HostToNetworkOrder((short)(headerDefinition)));

                if (AreHeadersCompressed)
                {
                    using (DeflateStream compressedHeaderStream = new DeflateStream(stream, CompressionMode.Compress, true))
                    {
                        WriteHeadersToStream(compressedHeaderStream);
                    }
                }
                else
                {
                    WriteHeadersToStream(stream);
                }
            }

            if (Type == GdsFrameType.BodyOnly || Type == GdsFrameType.Full)
            {
                writer.Write(((uint)IPAddress.HostToNetworkOrder((int)Body.Length)));

                writer.Write(Body);
            }

            writer.Flush();
        }

        /// <summary>
        /// Writes the headers to a stream.
        /// </summary>
        /// <param name="stream"></param>
        private void WriteHeadersToStream(Stream stream)
        {
            BinaryWriter headerWriter = new BinaryWriter(stream);

            foreach (KeyValuePair<byte[], byte[]> kvp in headers)
            {
                headerWriter.Write(((ushort)IPAddress.HostToNetworkOrder((short)kvp.Key.Length)));
                headerWriter.Write(((ushort)IPAddress.HostToNetworkOrder((short)kvp.Value.Length)));
                headerWriter.Write(kvp.Key);
                headerWriter.Write(kvp.Value);
            }

            headerWriter.Flush();
        }

        /// <summary>
        /// Parses a frame from a stream.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static GdsFrame ParseFrame(Stream stream)
        {
            GdsFrame frame = new GdsFrame();

            BinaryReader reader = new BinaryReader(stream);

            uint frameDefinition = (uint)IPAddress.NetworkToHostOrder((int)reader.ReadUInt32());

            if (!Enum.IsDefined(typeof(GdsFrameType), (byte)((frameDefinition & (uint)TypeMask) >> TypeShift)))
            {
                throw new ArgumentException("Invalid type: " + (GdsFrameType)(byte)((frameDefinition & (uint)TypeMask) >> TypeShift));
            }

            frame.IsComplete = ((frameDefinition & IsCompleteMask) >> IsCompleteShift) == 1;
            frame.Type = (GdsFrameType)(byte)((frameDefinition & (uint)TypeMask) >> TypeShift);
            frame.StreamId = (frameDefinition & (uint)StreamIdMask) >> StreamIdShift;

            // parse the headers
            if (frame.Type == GdsFrameType.Full || frame.Type == GdsFrameType.HeadersOnly)
            {
                ushort headersDefinition = (ushort)IPAddress.NetworkToHostOrder((short)reader.ReadUInt16());

                frame.AreHeadersCompressed = ((headersDefinition & (ushort)HeadersIsCompressedMask) >> HeadersIsCompressedShift) == 1;
                ushort count = (ushort)((headersDefinition & HeadersLengthMask) >> HeadersLengthShift);

                if (frame.AreHeadersCompressed)
                {
                    long movePosition = stream.Position;

                    using (DeflateStream compressedHeaderStream = new DeflateStream(stream, CompressionMode.Decompress, true))
                    {
                        ReadHeadersFromStream(compressedHeaderStream, count, frame);

                        movePosition += compressedHeaderStream.TotalIn;
                    }

                    stream.Position = movePosition; // we need to fix the position since the DeflateStream buffers the base stream
                }
                else
                {
                    ReadHeadersFromStream(stream, count, frame);
                }
            }

            // read the body
            if (frame.Type == GdsFrameType.Full || frame.Type == GdsFrameType.BodyOnly)
            {
                uint length = (uint)IPAddress.NetworkToHostOrder((int)reader.ReadUInt32());

                frame.Body = reader.ReadBytes((int)length);

                if (frame.Body.Length != length)
                {
                    throw new EndOfStreamException();
                }
            }

            return frame;
        }

        /// <summary>
        /// Reads headers from a stream into the given frame.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="headerCount"></param>
        /// <param name="frame"></param>
        private static void ReadHeadersFromStream(Stream stream, ushort headerCount, GdsFrame frame)
        {
            BinaryReader reader = new BinaryReader(stream);

            for (int i = 0; i < headerCount; i++)
            {
                ushort keyLength = (ushort)IPAddress.NetworkToHostOrder((short)reader.ReadUInt16());
                ushort valueLength = (ushort)IPAddress.NetworkToHostOrder((short)reader.ReadUInt16());

                byte[] key = reader.ReadBytes(keyLength);

                if (key.Length != keyLength)
                {
                    throw new EndOfStreamException();
                }

                byte[] value = reader.ReadBytes(valueLength);

                if (value.Length != valueLength)
                {
                    throw new EndOfStreamException();
                }

                frame.headers[key] = value;
            }
        }

        /// <summary>
        /// Creates a new content frame where the frame is a headers only, body only, or a full frame.
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="headers"></param>
        /// <param name="areHeadersCompressed"></param>
        /// <param name="body"></param>
        /// <param name="isComplete"></param>
        /// <returns></returns>
        public static GdsFrame NewContentFrame(uint streamId, Dictionary<byte[], byte[]> headers = null, bool areHeadersCompressed = false, byte[] body = null, bool isComplete = true)
        {
            GdsFrameType type = GdsFrameType.Full;

            if (body == null)
            {
                type = GdsFrameType.HeadersOnly;
            }
            else if (headers == null)
            {
                type = GdsFrameType.BodyOnly;
            }

            GdsFrame frame = new GdsFrame()
            {
                IsComplete = isComplete,
                Type = type,
                StreamId = streamId,
                AreHeadersCompressed = areHeadersCompressed
            };

            if (headers != null)
            {
                foreach (KeyValuePair<byte[], byte[]> kvp in headers)
                {
                    frame.headers[kvp.Key] = kvp.Value;
                }
            }
            
            if (body != null)
            {
                frame.Body = body;
            }

            return frame;
        }

        /// <summary>
        /// Creates a new ping frame.
        /// </summary>
        /// <returns></returns>
        public static GdsFrame NewPingFrame(uint streamId)
        {
            return new GdsFrame()
            {
                IsComplete = true,
                Type = GdsFrameType.Ping,
                StreamId = streamId
            };
        }

        /// <summary>
        /// Creates a new pong frame.
        /// </summary>
        /// <returns></returns>
        public static GdsFrame NewPongFrame(uint streamId)
        {
            return new GdsFrame()
            {
                IsComplete = true,
                Type = GdsFrameType.Pong,
                StreamId = streamId
            };
        }

        /// <summary>
        /// Creates a new close frame.
        /// </summary>
        /// <returns></returns>
        public static GdsFrame NewCloseFrame(uint streamId)
        {
            return new GdsFrame()
            {
                IsComplete = true,
                Type = GdsFrameType.Close,
                StreamId = streamId
            };
        }

        /// <summary>
        /// Compares byte arrays for use in dictionaries.
        /// </summary>
        public class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            /// <summary>
            /// Compares two byte arrays.
            /// </summary>
            /// <param name="left"></param>
            /// <param name="right"></param>
            /// <returns></returns>
            public bool Equals(byte[] left, byte[] right)
            {
                if (left == null || right == null)
                {
                    return left == right;
                }
                if (left.Length != right.Length)
                {
                    return false;
                }
                for (int i = 0; i < left.Length; i++)
                {
                    if (left[i] != right[i])
                    {
                        return false;
                    }
                }
                return true;
            }

            /// <summary>
            /// Generates a hash code from a byte array. We need to improve this for large arrays.
            /// </summary>
            /// <param name="key"></param>
            /// <returns></returns>
            public int GetHashCode(byte[] key)
            {
                if (key == null)
                    throw new ArgumentNullException("key");
                int sum = 0;
                foreach (byte cur in key)
                {
                    sum += cur;
                }
                return sum;
            }
        }
    }
}
