using System;
using System.IO;
using System.Net;
using System.Text;

namespace ArenaNet.SockNet.WebSocket
{
    /// <summary>
    /// A WebSocket frame representation.
    /// </summary>
    public class WebSocketFrame
    {
        private static readonly Random GlobalRandom;

        private static readonly UTF8Encoding UTF8;

        public bool IsFinished { get; private set; }

        public bool Reserved1 { get; private set; }

        public bool Reserved2 { get; private set; }

        public bool Reserved3 { get; private set; }

        public WebSocketFrame.OperationCode Operation { get; private set; }

        public byte[] Data { get; private set; }

        public byte[] Mask { get; private set; }

        static WebSocketFrame()
        {
            DateTime now = DateTime.Now;

            WebSocketFrame.GlobalRandom = new Random(now.Second * now.Minute);
            WebSocketFrame.UTF8 = new UTF8Encoding(false);
        }

        private WebSocketFrame()
        {
        }

        public void Write(Stream stream)
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

                if (this.Mask != null)
                {
                    data = new byte[this.Data.Length];

                    for (int i = 0; i < this.Data.Length; ++i)
                    {
                        data[i] = (byte)(this.Data[i] ^ this.Mask[i % 4]);
                    }
                }

                binaryWriter.Write(data);
            }

            binaryWriter.Flush();
        }

        public static WebSocketFrame CreateTextFrame(string text, bool mask = true)
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
                Operation = OperationCode.TextFrame,
                Mask = maskData,
                Data = UTF8.GetBytes(text)
            };
        }

        public static WebSocketFrame CreateBinaryFrame(byte[] data, bool mask = true)
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
                Operation = OperationCode.BinaryFrame,
                Mask = maskData,
                Data = data
            };
        }

        public static WebSocketFrame ParseFrame(Stream stream)
        {
            WebSocketFrame frame = new WebSocketFrame();
            BinaryReader binaryReader = new BinaryReader(stream, (Encoding)WebSocketFrame.UTF8);

            byte finRsvAndOp = binaryReader.ReadByte();
            frame.IsFinished = ((int)finRsvAndOp & 128) != 0;
            frame.Reserved1 = ((int)finRsvAndOp & 64) != 0;
            frame.Reserved2 = ((int)finRsvAndOp & 32) != 0;
            frame.Reserved3 = ((int)finRsvAndOp & 16) != 0;
            frame.Operation = (WebSocketFrame.OperationCode)((int)finRsvAndOp & 15);

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

            return frame;
        }

        public enum OperationCode
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
