using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Protocols.Http
{
    /// <summary>
    /// Represents a HTTP payload.
    /// </summary>
    public abstract class HttpPayload
    {
        public abstract string CommandLine { get; }

        private static readonly char[] LineEnding = new char[] { '\r', '\n' };

        internal Dictionary<string, List<string>> headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public class SingleValueHeaderView
        {
            private HttpPayload parent;

            internal SingleValueHeaderView(HttpPayload parent)
            {
                this.parent = parent;
            }

            public void Remove(string name)
            {
                this[name] = null;
            }

            public string this[string name]
            {
                get
                {
                    List<string> values;

                    if (parent.headers.TryGetValue(name, out values) && values.Count > 0)
                    {
                        return values[0];
                    }
                    else
                    {
                        return null;
                    }
                }
                set
                {
                    if (value == null)
                    {
                        parent.headers.Remove(name);
                    }
                    else
                    {
                        List<string> values;

                        if (!parent.headers.TryGetValue(name, out values))
                        {
                            values = new List<string>();
                            parent.headers[name] = values;
                        }

                        values.Add(value);
                    }
                }
            }
        }
        public class MultiValueHeaderView
        {
            private HttpPayload parent;

            internal MultiValueHeaderView(HttpPayload parent)
            {
                this.parent = parent;
            }

            public void Remove(string name)
            {
                this[name] = null;
            }

            public string[] this[string name]
            {
                get
                {
                    List<string> values;

                    if (parent.headers.TryGetValue(name, out values) && values.Count > 0)
                    {
                        return values.ToArray();
                    }
                    else
                    {
                        return new string[0];
                    }
                }
                set
                {
                    if (value == null)
                    {
                        parent.headers.Remove(name);
                    }
                    else
                    {
                        List<string> values;

                        if (!parent.headers.TryGetValue(name, out values))
                        {
                            values = new List<string>(value);
                        }

                        values.AddRange(value);
                    }
                }
            }
        }

        internal readonly MultiValueHeaderView _multiValueHeaderView;
        public MultiValueHeaderView Headers { get { return _multiValueHeaderView; } }

        internal readonly SingleValueHeaderView _singleValueHeaderView;
        public SingleValueHeaderView Header { get { return _singleValueHeaderView; } }

        public bool IsChunked { private set; get; }

        public Stream Body { private set; get; }

        public int BodySize { set; get; }

        /// <summary>
        /// Creates a HTTP payload.
        /// </summary>
        public HttpPayload()
        {
            _multiValueHeaderView = new MultiValueHeaderView(this);
            _singleValueHeaderView = new SingleValueHeaderView(this);

            IsChunked = false;
            Body = new MemoryStream();
        }

        enum ParseState
        {
            NONE,
            COMMAND_LINE,
            HEADERS,
            BODY,
            ERROR
        }

        /// <summary>
        /// Writes the current payload to the stream.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="closeStream"></param>
        /// <returns></returns>
        public void Write(Stream stream, bool closeStream = false)
        {
            StreamWriter writer = new StreamWriter(stream);

            writer.Write(CommandLine);
            writer.Write(LineEnding);

            foreach (KeyValuePair<string, List<string>> header in headers)
            {
                StringBuilder value = new StringBuilder();

                for (int i = 0; i < header.Value.Count; i++)
                {
                    value.Append(header.Value[i]);

                    if (i < header.Value.Count - 1)
                    {
                        value.Append(",");
                    }
                }

                writer.Write(header.Key);
                writer.Write(": ");
                writer.Write(value.ToString());
                writer.Write(LineEnding);
            }

            writer.Write(LineEnding);

            writer.Flush();

            Body.Position = 0;
            Copy(Body, stream, BodySize);

            if (closeStream)
            {
                writer.Close();
            }
        }

        /// <summary>
        /// Parses a stream.
        /// </summary>
        /// <param name="stream"></param>
        public bool Parse(Stream stream, bool isClosed = false)
        {
            long startingPosition = stream.Position;

            try
            {
                ParseState state = ParseState.COMMAND_LINE;

                if (!IsChunked)
                {
                    // clear stale headers
                    headers.Clear();

                    // parse the header
                    string line = null;

                    while (ReadLine(stream, Encoding.ASCII, out line))
                    {
                        switch (state)
                        {
                            case ParseState.COMMAND_LINE:
                                line = line.Trim();

                                if (!HandleCommandLine(line))
                                {
                                    state = ParseState.ERROR;
                                }
                                else
                                {
                                    state = ParseState.HEADERS;
                                }
                                break;
                            case ParseState.HEADERS:
                                line = line.Trim();

                                if ("".Equals(line))
                                {
                                    state = ParseState.BODY;
                                }
                                else
                                {
                                    KeyValuePair<string, List<string>> header;

                                    if (HandleHeaderLine(line, out header))
                                    {
                                        List<string> values;

                                        if (headers.TryGetValue(header.Key, out values))
                                        {
                                            values.AddRange(header.Value);
                                        }
                                        else
                                        {
                                            headers[header.Key] = header.Value;
                                        }
                                    }
                                    else
                                    {
                                        state = ParseState.ERROR;
                                    }
                                }
                                break;
                        }

                        // if the next start is error or parsing body, then get out of the header loop
                        if (state == ParseState.ERROR || state == ParseState.BODY)
                        {
                            break;
                        }
                    }
                } else
                {
                    state = ParseState.BODY;
                }

                long bodyStartPosition = stream.Position;

                // handle body content
                switch (state)
                {
                    case ParseState.BODY:
                        string transferEncoding = Header["transfer-encoding"];
                        string contentLength = Header["content-length"];
                        if (contentLength == null)
                        {
                            contentLength = Header["l"];
                        }

                        if (IsChunked || transferEncoding != null && "chunked".Equals(transferEncoding.Trim()))
                        {
                            IsChunked = true;

                            string length;
                            
                            if (!ReadLine(stream, Encoding.ASCII, out length))
                            {
                                return false;
                            }

                            int bodySize = Convert.ToInt32(length.Split(';')[0], 16);

                            if (bodySize == 0)
                            {
                                return true;
                            }
                            else
                            {
                                int actuallyRead = Copy(stream, Body, bodySize);

                                if (actuallyRead != bodySize || !ReadLine(stream, Encoding.ASCII, out length))
                                {
                                    Body.Position = 0;
                                    stream.Position = bodyStartPosition;
                                }
                                else
                                {
                                    BodySize += actuallyRead;
                                }

                                return false;
                            }
                        }
                        else if (!string.IsNullOrEmpty(contentLength))
                        {
                            int bodySize = Convert.ToInt32(contentLength, 10);

                            int actuallyRead = Copy(stream, Body, bodySize);

                            if (actuallyRead != bodySize)
                            {
                                Body.Position = 0;
                                stream.Position = startingPosition;

                                return false;
                            }
                            else
                            {
                                BodySize = bodySize;

                                return true;
                            }
                        }
                        else if (isClosed)
                        {
                            BodySize = Copy(stream, Body, int.MaxValue);

                            return true;
                        }

                        goto default;
                    default:
                        stream.Position = startingPosition;

                        return false;
                }
            } 
            catch (Exception e)
            {
                stream.Position = startingPosition;

                throw e;
            }
        }

        /// <summary>
        /// Handles the command line.
        /// </summary>
        /// <param name="commandLine"></param>
        protected abstract bool HandleCommandLine(string commandLine);

        /// <summary>
        /// Handles a header line.
        /// </summary>
        /// <param name="headerLine"></param>
        /// <param name="header"></param>
        /// <returns></returns>
        protected bool HandleHeaderLine(string headerLine, out KeyValuePair<string, List<string>> header)
        {
            string[] split = headerLine.Split(new string[] { ":" }, 2, StringSplitOptions.None);

            if (split.Length == 2)
            {
                string[] values = split[1].Split(new string[] { "," }, StringSplitOptions.None);
                List<string> headerValuesList = new List<string>();

                if (values != null && values.Length > 0)
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        headerValuesList.Add(values[i].Trim());
                    }
                }

                header = new KeyValuePair<string, List<string>>(split[0], headerValuesList);

                return true;
            }
            else
            {
                header = new KeyValuePair<string,List<string>>();

                return false;
            }
        }

        /// <summary>
        /// Reads a line until it hits the following line ending.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="lineEnding"></param>
        /// <returns></returns>
        private static bool ReadLine(Stream input, Encoding encoding, out string line)
        {
            StringBuilder builder = new StringBuilder();
            BinaryReader reader = new BinaryReader(input, encoding);

            while (reader.PeekChar() != -1)
            {
                char currChar = reader.ReadChar();

                if (currChar == '\r')
                {
                    if (reader.PeekChar() != -1)
                    {
                        char nextChar = reader.ReadChar();

                        if (nextChar == '\n')
                        {
                            line = builder.ToString();
                            return true;
                        }
                    }
                } else if (currChar == '\n')
                {
                    line = builder.ToString();
                    return true;
                }

                builder.Append(currChar);
            }

            line = null;
            return false;
        }

        /// <summary>
        /// Copies a stream to another.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="output"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private static int Copy(Stream input, Stream output, int count)
        {
            byte[] buffer = new byte[1024];
            int totalRead = 0;
            int read;

            while (totalRead < count && (read = input.Read(buffer, 0, Math.Min(buffer.Length, count - totalRead))) > 0)
            {
                output.Write(buffer, 0, read);
                totalRead += read;
            }

            return totalRead;
        }

        /// <summary>
        /// Returns true if the two char arrays equal.
        /// </summary>
        /// <param name="b1"></param>
        /// <param name="b2"></param>
        /// <returns></returns>
        public static bool CharArraysEqual(char[] b1, char[] b2)
        {
            bool equal = true;

            if (b1 != b2) // if this is the same array then we don't need to compare them
            {
                if (b1 != null && b2 != null && b1.Length == b2.Length)
                {
                    for (int i = 0; i < b1.Length; i++)
                    {
                        if (b1[i] != b2[i])
                        {
                            equal = false;
                            break;
                        }
                    }
                }
                else
                {
                    equal = false;
                }
            }

            return equal;
        }
    }
}
