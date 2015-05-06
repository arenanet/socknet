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
using System.IO;
using System.Net;
using System.Text;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.Medley.Pool;

namespace ArenaNet.SockNet.Protocols.WebSocket
{
    /// <summary>
    /// A WebSocket frame representation.
    /// </summary>
    public class WebSocketFrame
    {
        /// <summary>
        /// A global random that can be used for generating random numbers.
        /// </summary>
        private static readonly Random GlobalRandom;

        /// <summary>
        /// The standard encoding for WebSocket Text messages is UTF-8.
        /// </summary>
        private static readonly UTF8Encoding UTF8;

        /// <summary>
        /// If this frame is the last frame.
        /// </summary>
        public bool IsFinished { get; private set; }

        /// <summary>
        /// Reserved bit field 1.
        /// </summary>
        public bool Reserved1 { get; private set; }

        /// <summary>
        /// Reserved bit field 2.
        /// </summary>
        public bool Reserved2 { get; private set; }

        /// <summary>
        /// Reserved bit field 3.
        /// </summary>
        public bool Reserved3 { get; private set; }

        /// <summary>
        /// The operation of this frame.
        /// </summary>
        public WebSocketFrame.WebSocketFrameOperation Operation { get; private set; }

        /// <summary>
        /// The raw data of this frame.
        /// </summary>
        public byte[] Data { get; private set; }

        /// <summary>
        /// The data as a string.
        /// </summary>
        public string DataAsString { 
            get 
            { 
                byte[] data  = Data; 

                if (data == null)
                {
                    return null;
                }

                return UTF8.GetString(data);
            } 
        }

        /// <summary>
        /// The mask of this frame.
        /// </summary>
        public byte[] Mask { get; private set; }

        /// <summary>
        /// Static initializer.
        /// </summary>
        static WebSocketFrame()
        {
            DateTime now = DateTime.Now;

            WebSocketFrame.GlobalRandom = new Random(now.Second * now.Minute + now.Millisecond);
            WebSocketFrame.UTF8 = new UTF8Encoding(false);
        }

        /// <summary>
        /// Creates a websocket frame.
        /// </summary>
        private WebSocketFrame()
        {
        }

        /// <summary>
        /// Writes the current WebSocketFrame into the given stream.
        /// </summary>
        /// <param name="stream"></param>
        public void Write(Stream stream, bool applyMaskIfSet = true)
        {
            BinaryWriter binaryWriter = new BinaryWriter(stream, (Encoding)WebSocketFrame.UTF8);

            byte finRsvAndOp = (byte)0;

            if (this.IsFinished)
            {
                finRsvAndOp |= (byte)128;
            }
            if (this.Reserved1)
            {
                finRsvAndOp |= (byte)64;
            }
            if (this.Reserved2)
            {
                finRsvAndOp |= (byte)32;
            }
            if (this.Reserved3)
            {
                finRsvAndOp |= (byte)16;
            }

            finRsvAndOp |= (byte)this.Operation;

            binaryWriter.Write(finRsvAndOp);

            byte maskAndLength = (byte)0;

            if (this.Mask != null)
            {
                maskAndLength |= (byte)128;
            }

            bool? isShortLength = null;

            if (this.Data != null)
            {
                if (this.Data.Length < 126)
                {
                    maskAndLength |= (byte)this.Data.Length;
                    isShortLength = null;
                }
                else if (this.Data.Length < (int)ushort.MaxValue)
                {
                    maskAndLength |= (byte)126;
                    isShortLength = true;
                }
                else if ((ulong)this.Data.Length < ulong.MaxValue)
                {
                    maskAndLength |= (byte)127;
                    isShortLength = false;
                }
            }

            binaryWriter.Write(maskAndLength);

            if (isShortLength.HasValue)
            {
                if (isShortLength.Value)
                {
                    binaryWriter.Write((ushort)IPAddress.HostToNetworkOrder((short)this.Data.Length));
                }
                else
                {
                    binaryWriter.Write((ulong)IPAddress.HostToNetworkOrder((long)this.Data.Length));
                }
            }

            if (this.Mask != null)
            {
                binaryWriter.Write(this.Mask);
            }

            if (this.Data != null)
            {
                byte[] data = this.Data;

                if (this.Mask != null && applyMaskIfSet)
                {
                    data = new byte[this.Data.Length];

                    for (int i = 0; i < this.Data.Length; i++)
                    {
                        data[i] = (byte)(this.Data[i] ^ this.Mask[i % 4]);
                    }
                }

                binaryWriter.Write(data);
            }

            binaryWriter.Flush();
        }

        /// <summary>
        /// Creates a TEXT WebSocketFrame.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="mask"></param>
        /// <returns></returns>
        public static WebSocketFrame CreateTextFrame(string text, bool mask = true, bool continuation = false, bool isFinished = true)
        {
            byte[] maskData = null;

            if (mask)
            {
                maskData = new byte[4]
                {
                  (byte) WebSocketFrame.GlobalRandom.Next(byte.MaxValue),
                  (byte) WebSocketFrame.GlobalRandom.Next(byte.MaxValue),
                  (byte) WebSocketFrame.GlobalRandom.Next(byte.MaxValue),
                  (byte) WebSocketFrame.GlobalRandom.Next(byte.MaxValue)
                };
            }

            return new WebSocketFrame()
            {
                IsFinished = isFinished,
                Reserved1 = false,
                Reserved2 = false,
                Reserved3 = false,
                Operation = continuation ? WebSocketFrameOperation.Continuation : WebSocketFrameOperation.TextFrame,
                Mask = maskData,
                Data = UTF8.GetBytes(text)
            };
        }

        /// <summary>
        /// Creates a TEXT WebSocketFrame.
        /// </summary>
        /// <param name="rawText"></param>
        /// <param name="mask"></param>
        /// <returns></returns>
        public static WebSocketFrame CreateTextFrame(byte[] rawText, bool mask = true, bool continuation = false, bool isFinished = true)
        {
            byte[] maskData = null;

            if (mask)
            {
                maskData = new byte[4]
                {
                  (byte) WebSocketFrame.GlobalRandom.Next(byte.MaxValue),
                  (byte) WebSocketFrame.GlobalRandom.Next(byte.MaxValue),
                  (byte) WebSocketFrame.GlobalRandom.Next(byte.MaxValue),
                  (byte) WebSocketFrame.GlobalRandom.Next(byte.MaxValue)
                };
            }

            return new WebSocketFrame()
            {
                IsFinished = isFinished,
                Reserved1 = false,
                Reserved2 = false,
                Reserved3 = false,
                Operation = continuation ? WebSocketFrameOperation.Continuation : WebSocketFrameOperation.TextFrame,
                Mask = maskData,
                Data = rawText
            };
        }

        /// <summary>
        /// Creates a Binary WebSocketFrame.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="mask"></param>
        /// <returns></returns>
        public static WebSocketFrame CreateBinaryFrame(byte[] data, bool mask = true, bool continuation = false)
        {
            byte[] maskData = null;

            if (mask)
            {
                maskData = new byte[4]
                {
                  (byte) WebSocketFrame.GlobalRandom.Next(byte.MaxValue),
                  (byte) WebSocketFrame.GlobalRandom.Next(byte.MaxValue),
                  (byte) WebSocketFrame.GlobalRandom.Next(byte.MaxValue),
                  (byte) WebSocketFrame.GlobalRandom.Next(byte.MaxValue)
                };
            }

            return new WebSocketFrame()
            {
                IsFinished = true,
                Reserved1 = false,
                Reserved2 = false,
                Reserved3 = false,
                Operation = continuation ? WebSocketFrameOperation.Continuation : WebSocketFrameOperation.BinaryFrame,
                Mask = maskData,
                Data = data
            };
        }

        /// <summary>
        /// Parses a frame from the given stream.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static WebSocketFrame ParseFrame(Stream stream)
        {
            WebSocketFrame frame = new WebSocketFrame();
            BinaryReader binaryReader = new BinaryReader(stream, (Encoding)WebSocketFrame.UTF8);

            byte finRsvAndOp = binaryReader.ReadByte();
            if (!Enum.IsDefined(typeof(WebSocketFrame.WebSocketFrameOperation), (byte)((int)finRsvAndOp & 15)))
            {
                throw new ArgumentException("Invalid operation: " + ((int)finRsvAndOp & 15));
            }
            
            frame.IsFinished = ((int)finRsvAndOp & 128) != 0;
            frame.Reserved1 = ((int)finRsvAndOp & 64) != 0;
            frame.Reserved2 = ((int)finRsvAndOp & 32) != 0;
            frame.Reserved3 = ((int)finRsvAndOp & 16) != 0;
            frame.Operation = (WebSocketFrame.WebSocketFrameOperation)(byte)((int)finRsvAndOp & 15);

            byte maskAndLength = binaryReader.ReadByte();
            bool isMasked = (maskAndLength & 128) != 0;

            int length = maskAndLength & 127;
            switch (length)
            {
                case 126:
                {
                    length = (int)(ushort)IPAddress.HostToNetworkOrder((short)binaryReader.ReadUInt16());
                    break;
                }
                case 127:
                {
                    length = Convert.ToInt32((ulong)IPAddress.HostToNetworkOrder((long)binaryReader.ReadUInt64()));
                    break;
                }
            }

            if (isMasked)
            {
                frame.Mask = binaryReader.ReadBytes(4);
            }

            frame.Data = binaryReader.ReadBytes(length);

            if (frame.Data.Length != length)
            {
                throw new EndOfStreamException();
            }

            if (isMasked)
            {
                for (int i = 0; i < frame.Data.Length; i++)
                {
                    frame.Data[i] = (byte)(frame.Data[i] ^ frame.Mask[i % 4]);
                }
            }

            return frame;
        }

        /// <summary>
        /// WebSocket operation codes.
        /// </summary>
        public enum WebSocketFrameOperation : byte
        {
            Continuation = 0,
            TextFrame = 1,
            BinaryFrame = 2,
            ConnectionClose = 8,
            Ping = 9,
            Pong = 10,
        }
    }
}
