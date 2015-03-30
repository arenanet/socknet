using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace ArenaNet.SockNet.Protocols.Http2
{
    public class SettingsHttp2Frame : Http2Frame
    {
        [Flags]
        private enum FrameFlags : byte
        {
            ACK = 0x1
        }

        public enum Parameter : ushort
        {
            SETTINGS_HEADER_TABLE_SIZE = 0x1,
            SETTINGS_ENABLE_PUSH = 0x2,
            SETTINGS_MAX_CONCURRENT_STREAMS = 0x3,
            SETTINGS_INITIAL_WINDOW_SIZE = 0x4,
            SETTINGS_MAX_FRAME_SIZE = 0x5,
            SETTINGS_MAX_HEADER_LIST_SIZE = 0x6
        }

        public static Dictionary<Parameter, uint> ParameterDefaults
        {
            get
            {
                return new Dictionary<Parameter, uint>() 
                {
                    { Parameter.SETTINGS_HEADER_TABLE_SIZE, 4096 },
                    { Parameter.SETTINGS_ENABLE_PUSH, 1},
                    { Parameter.SETTINGS_INITIAL_WINDOW_SIZE, 65535 },
                    { Parameter.SETTINGS_MAX_FRAME_SIZE, 16384 }
                };
            }
        }

        public bool IsAck
        {
            get
            {
                return ((FrameFlags)Flags & FrameFlags.ACK) == FrameFlags.ACK;
            }
            set
            {
                Flags = (byte)((FrameFlags)Flags & FrameFlags.ACK);
            }
        }

        public override Http2FrameType Type { get { return Http2FrameType.SETTINGS; } }

        private Dictionary<Parameter, uint> settings = new Dictionary<Parameter, uint>();

        public void Set(Dictionary<Parameter, uint> values)
        {
            foreach (KeyValuePair<Parameter, uint> value in values)
            {
                Set(value.Key, value.Value);
            }
        }

        public void Set(Parameter parameter, uint value)
        {
            settings[parameter] = value;
        }

        public bool TryGetSetting(Parameter parameter, out uint value)
        {
            return settings.TryGetValue(parameter, out value);
        }

        public override void WritePayload(Stream stream)
        {
            BinaryWriter writer = new BinaryWriter(stream);

            foreach (KeyValuePair<Parameter, uint> kvp in  settings)
            {
                writer.Write((ushort)IPAddress.HostToNetworkOrder((short)kvp.Key));
                writer.Write((uint)IPAddress.HostToNetworkOrder((int)kvp.Value));
            }

            writer.Flush();
        }

        public override void ReadPayload(uint length, Stream stream)
        {
            uint numberOfSettings = length / 48;

            BinaryReader reader = new BinaryReader(stream);

            for (uint i = 0; i < numberOfSettings; i++)
            {
                settings[(Parameter)(ushort)IPAddress.NetworkToHostOrder((short)reader.ReadUInt16())] = (uint)IPAddress.NetworkToHostOrder((int)reader.ReadUInt32());
            }
        }
    }
}
