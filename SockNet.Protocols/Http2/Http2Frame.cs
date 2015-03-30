using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Protocols.Http2
{
    public enum Http2FrameType : byte
    {
        DATA = 0x0,
        HEADERS = 0x1,
        PRIORITY = 0x2,
        RST_STREAM = 0x3,
        SETTINGS = 0x4,
        PUSH_PROMISE = 0x5,
        PING = 0x6,
        GOAWAY = 0x7,
        WINDOW_UPDATE = 0x8,
        CONTINUATION = 0x9
    }

    public abstract class Http2Frame
    {
        private static readonly Dictionary<Http2FrameType, Type> TypeMapping = new Dictionary<Http2FrameType, Type>();
        public static Http2Frame NewHttp2Frame(Http2FrameType frameType, byte flags, uint streamIdentifier)
        {
            Http2Frame frame = null;

            switch (frameType)
            {
                case Http2FrameType.SETTINGS:
                    frame = new SettingsHttp2Frame() { Flags = flags, StreamIdentifier = streamIdentifier };
                    break;
            };

            return frame;
        }

        public byte Flags { internal set; get; }

        public uint StreamIdentifier { internal set; get; }

        public abstract Http2FrameType Type { get; }

        public void Write(Stream stream)
        {
            stream.Position += 9;
            long startBodyPosition = stream.Position;

            WritePayload(stream);

            ushort length = (ushort)(stream.Position - startBodyPosition);

            stream.Position = startBodyPosition - 9;

            WriteUint24(stream, (uint)IPAddress.HostToNetworkOrder((int)length));
            stream.WriteByte((byte)Type);
            stream.WriteByte(Flags);
            WriteUint32(stream, (uint)IPAddress.HostToNetworkOrder((int)StreamIdentifier) & 0x7FFFFFFF);

            stream.Position += length;
        }

        public static Http2Frame Read(Stream stream)
        {
            uint length = (uint)IPAddress.NetworkToHostOrder((int)ReadUInt24(stream));

            Http2Frame frame = Http2Frame.NewHttp2Frame((Http2FrameType)stream.ReadByte(), (byte)stream.ReadByte(), (uint)IPAddress.NetworkToHostOrder((int)ReadUInt32(stream)));

            frame.ReadPayload(length, stream);

            return frame;
        }

        private static uint ReadUInt24(Stream stream)
        {
            try
            {
                byte b1 = (byte)stream.ReadByte();
                byte b2 = (byte)stream.ReadByte();
                byte b3 = (byte)stream.ReadByte();
                return
                    (((uint)b1) << 16) |
                    (((uint)b2) << 8) |
                    ((uint)b3);
            }
            catch
            {
                return 0u;
            }
        }

        private static uint ReadUInt32(Stream stream)
        {
            try
            {
                byte b1 = (byte)stream.ReadByte();
                byte b2 = (byte)stream.ReadByte();
                byte b3 = (byte)stream.ReadByte();
                byte b4 = (byte)stream.ReadByte();
                return
                    (((uint)b1) << 24) |
                    (((uint)b2) << 16) |
                    (((uint)b3) << 8) |
                    ((uint)b4);
            }
            catch
            {
                return 0u;
            }
        }

        private static void WriteUint24(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)(((value >> 8) & 0xFF)));
            stream.WriteByte((byte)(((value >> 16) & 0xFF)));
        }

        private static void WriteUint32(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)(((value >> 8) & 0xFF)));
            stream.WriteByte((byte)(((value >> 16) & 0xFF)));
            stream.WriteByte((byte)(((value >> 24) & 0xFF)));
        }

        public abstract void WritePayload(Stream stream);

        public abstract void ReadPayload(uint length, Stream stream);
    }
}
