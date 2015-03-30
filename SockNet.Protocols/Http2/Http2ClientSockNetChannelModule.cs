using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Protocols.Http;

namespace ArenaNet.SockNet.Protocols.Http2
{
    /// <summary>
    /// A HTTP/2 module.
    /// </summary>
    public class Http2ClientSockNetChannelModule : ISockNetChannelModule
    {
        public ParsingMode Mode { private set; get; }
        public enum ParsingMode
        {
            Client,
            Server
        }

        /// <summary>
        /// A delegates that is used for http2 establishment notifications.
        /// </summary>
        /// <param name="channel"></param>
        public delegate void OnHttp2EstablishedDelegate(ISockNetChannel channel);

        private HttpSockNetChannelModule httpModule = new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client);

        public Dictionary<SettingsHttp2Frame.Parameter, uint> Settings
        {
            private set;
            get;
        }

        public string Path
        {
            private set;
            get;
        }

        public string Host
        {
            private set;
            get;
        }

        public OnHttp2EstablishedDelegate OnEstablishedDelegate
        {
            set;
            get;
        }

        /// <summary>
        /// Creates the http2 client module.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="host"></param>
        /// <param name="onEstablishedDelegate"></param>
        /// <param name="settings"></param>
        public Http2ClientSockNetChannelModule(
            string path,
            string host, 
            OnHttp2EstablishedDelegate onEstablishedDelegate = null,
            Dictionary<SettingsHttp2Frame.Parameter, uint> settings = null)
        {
            this.Path = path;
            this.Host = host;
            this.OnEstablishedDelegate = onEstablishedDelegate;
            this.Settings = settings;

            if (Settings == null)
            {
                Settings = new Dictionary<SettingsHttp2Frame.Parameter, uint>();
            }
        }

        /// <summary>
        /// Installs this module.
        /// </summary>
        /// <param name="channel"></param>
        public void Install(ISockNetChannel channel)
        {
            channel.AddModule(httpModule);
            channel.Pipe.AddOpenedLast(OnConnected);
        }

        /// <summary>
        /// Uninstalls this module.
        /// </summary>
        /// <param name="channel"></param>
        public void Uninstall(ISockNetChannel channel)
        {
            channel.RemoveModule(httpModule);
        }

        /// <summary>
        /// Invoked on channel connect.
        /// </summary>
        /// <param name="channel"></param>
        public void OnConnected(ISockNetChannel channel)
        {
            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Sending HTTP/2 upgrade request.");

            SettingsHttp2Frame settingsFrame = new SettingsHttp2Frame();
            settingsFrame.Set(Settings);
            settingsFrame.IsAck = false;
            settingsFrame.StreamIdentifier = 1;

            HttpRequest request;

            if (channel.IsConnectionEncrypted)
            {
                channel.Pipe.AddIncomingLast<HttpResponse>(HandleHandshake);
                request = new HttpRequest()
                {
                    Action = "PRI",
                    Path = "*",
                    Version = "HTTP/2.0"
                };

                byte[] twoOhBody = Encoding.ASCII.GetBytes("SM\r\n\r\n");
                long position = request.Body.Position;
                request.Body.Write(twoOhBody, 0, twoOhBody.Length);
                settingsFrame.Write(request.Body);
                position = request.Body.Position - position;
                request.BodySize = (int)position;

                channel.Send(request);
                
                //channel.Send(buffer);
            }
            else
            {
                channel.Pipe.AddIncomingLast<HttpResponse>(HandleHandshake);

                request = new HttpRequest()
                {
                    Action = "GET",
                    Path = Path,
                    Version = "HTTP/1.1"
                };
                request.Header["Host"] = Host;
                request.Header["Upgrade"] = channel.IsConnectionEncrypted ? "h2" : "h2c";
                request.Header["Connection"] = "Upgrade, HTTP2-Settings";
                request.Header["User-Agent"] = "SockNet";
                
                MemoryStream settingsStream = new MemoryStream();
                long position = settingsStream.Position;
                settingsFrame.WritePayload(settingsStream);
                position = settingsStream.Position - position;
                request.Header["HTTP2-Settings"] = Convert.ToBase64String(settingsStream.GetBuffer(), 0, (int)position).TrimEnd('=');
            }

            MemoryStream str = new MemoryStream();
            request.Write(str);
            str.Position = 0;
            using (StreamReader reader = new StreamReader(str))
            {
                Console.WriteLine(reader.ReadToEnd());
            }
        }

        /// <summary>
        /// Handles the WebSocket handshake.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="data"></param>
        private void HandleHandshake(ISockNetChannel channel, ref HttpResponse data)
        {
            MemoryStream str = new MemoryStream();
            data.Write(str);
            str.Position = 0;
            using (StreamReader reader = new StreamReader(str))
            {
                Console.WriteLine(reader.ReadToEnd());
            }

            if ("h2".Equals(data.Header["Upgrade"]) || "h2c".Equals(data.Header["Upgrade"]))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Established HTTP/2 connection.");
                channel.Pipe.RemoveIncoming<HttpResponse>(HandleHandshake);
                channel.Pipe.AddIncomingFirst<object>(HandleIncomingFrames);
                channel.Pipe.AddOutgoingLast<object>(HandleOutgoingFrames);

                if (OnEstablishedDelegate != null)
                {
                    OnEstablishedDelegate(channel);
                }
            }
            else
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, "HTTP/2 handshake incomplete.");

                channel.Close();
            }
        }

        /// <summary>
        /// Handles incoming raw frames and translates them into WebSocketFrame(s)
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="data"></param>
        private void HandleIncomingFrames(ISockNetChannel channel, ref object data)
        {
            if (!(data is ChunkedBuffer))
            {
                return;
            }

            ChunkedBuffer stream = (ChunkedBuffer)data;
            long startingPosition = stream.ReadPosition;

            try
            {
                data = Http2Frame.Read(stream.Stream);
            }
            catch (ArgumentOutOfRangeException)
            {
                // websocket frame isn't done
                stream.ReadPosition = startingPosition;
            }
            catch (Exception e)
            {
                // otherwise we can't recover
                SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, "Unexpected error: {0}", e.Message);

                channel.Close();
            }
        }

        /// <summary>
        /// Handles WebSocketFrame(s) and translates them into raw frames.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="data"></param>
        private void HandleOutgoingFrames(ISockNetChannel channel, ref object data)
        {
            if (!(data is Http2Frame))
            {
                return;
            }

            Http2Frame frame = (Http2Frame)data;
            ChunkedBuffer buffer = new ChunkedBuffer(channel.BufferPool);
            frame.Write(buffer.Stream);
            data = buffer;
        }
    }
}
