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
using System.Collections.Generic;
using System.Text;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Protocols.Http
{
    /// <summary>
    /// Represents a HTTP payload.
    /// </summary>
    public abstract class HttpPayload
    {
        public const string LineEnding = "\r\n";

        private static readonly ASCIIEncoding HeaderEncoding = new ASCIIEncoding();

        /// <summary>
        /// The command line (first line in a HTTP message)
        /// </summary>
        public abstract string CommandLine { get; }

        /// <summary>
        /// Internal header representation.
        /// </summary>
        internal Dictionary<string, List<string>> headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// External header view and manipulator that deals with single-value headers.
        /// </summary>
        public class SingleValueHeaderView
        {
            private HttpPayload parent;

            internal SingleValueHeaderView(HttpPayload parent)
            {
                this.parent = parent;
            }

            public ICollection<string> Names { get { return parent.headers.Keys; } }

            public void Remove(string name)
            {
                this[name] = null;
            }

            public string this[string name]
            {
                get
                {
                    lock (parent.headers)
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
                            List<string> values = new List<string>();
                            values.Add(value);
                            parent.headers[name] = values;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// External header view and manipulator that deals with multi-value headers.
        /// </summary>
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

            public ICollection<string> Names { get { return parent.headers.Keys; } }

            public string[] this[string name]
            {
                get
                {
                    lock (parent.headers)
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
                            List<string> values;

                            if (!parent.headers.TryGetValue(name, out values))
                            {
                                values = new List<string>(value);
                            }
                            else
                            {
                                values.AddRange(value);
                            }
                        }
                    }
                }
            }
        }

        internal readonly MultiValueHeaderView _multiValueHeaderView;
        /// <summary>
        /// Multi-value view and manipulation of headers.
        /// </summary>
        public MultiValueHeaderView Headers { get { return _multiValueHeaderView; } }

        internal readonly SingleValueHeaderView _singleValueHeaderView;
        /// <summary>
        /// Single-value view and manipulation of headers.
        /// </summary>
        public SingleValueHeaderView Header { get { return _singleValueHeaderView; } }

        /// <summary>
        /// Returns true if this payload is chunked.
        /// </summary>
        public bool IsChunked { private set; get; }

        /// <summary>
        /// Returns or sets the raw stream of the body.
        /// </summary>
        public ChunkedBuffer Body { set; get; }

        /// <summary>
        /// Sets the number bytes read or to write.
        /// </summary>
        public int BodySize { get { return (int)Body.AvailableBytesToRead; } }

        /// <summary>
        /// Creates a HTTP payload.
        /// </summary>
        public HttpPayload(ObjectPool<byte[]> bufferPool)
        {
            _multiValueHeaderView = new MultiValueHeaderView(this);
            _singleValueHeaderView = new SingleValueHeaderView(this);

            IsChunked = false;
            Body = new ChunkedBuffer(bufferPool);
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
            StreamWriter headerWriter = new StreamWriter(stream, HeaderEncoding);

            headerWriter.Write(CommandLine);
            headerWriter.Write(LineEnding);

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

                headerWriter.Write(header.Key);
                headerWriter.Write(": ");
                headerWriter.Write(value.ToString());
                headerWriter.Write(LineEnding);
            }

            headerWriter.Write(LineEnding);

            headerWriter.Flush();

            Body.ReadPosition = 0;
            Copy(Body.Stream, stream, BodySize);

            if (closeStream)
            {
                headerWriter.Close();
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

                    while (ReadLine(stream, HeaderEncoding, out line))
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

                        if (IsChunked || (transferEncoding != null && "chunked".Equals(transferEncoding.Trim())))
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
                                int actuallyRead = Copy(stream, Body.Stream, bodySize);

                                if (actuallyRead != bodySize || !ReadLine(stream, Encoding.ASCII, out length))
                                {
                                    Body.ReadPosition = 0;
                                    stream.Position = bodyStartPosition;
                                }

                                return false;
                            }
                        }
                        else if (!string.IsNullOrEmpty(contentLength))
                        {
                            int bodySize = Convert.ToInt32(contentLength, 10);

                            int actuallyRead = Copy(stream, Body.Stream, bodySize);

                            if (actuallyRead != bodySize)
                            {
                                Body.ReadPosition = 0;
                                stream.Position = startingPosition;

                                return false;
                            }
                            else
                            {
                                return true;
                            }
                        }
                        else if (isClosed)
                        {
                            Copy(stream, Body.Stream, int.MaxValue);

                            return true;
                        }

                        goto default;
                    default:
                        stream.Position = startingPosition;

                        return false;
                }
            } 
            catch (Exception)
            {
                stream.Position = startingPosition;

                return false;
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
                    if (reader.PeekChar() == '\n')
                    {
                        reader.ReadChar();

                        line = builder.ToString();
                        return true;
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
        private static bool CharArraysEqual(char[] b1, char[] b2)
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
