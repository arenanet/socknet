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
using System.IO;

namespace ArenaNet.SockNet.Common.IO
{
    /// <summary>
    /// A stream wrapper around the ChunkedBuffer.
    /// </summary>
    public class ChunkedBufferStream : Stream
    {
        private ChunkedBuffer chunkedBuffer;

        /// <summary>
        /// Returns true if this stream is readable
        /// </summary>
        public override bool CanRead
        {
            get { return !chunkedBuffer.IsClosed; }
        }

        /// <summary>
        /// Returns true if this stream can stream.
        /// </summary>
        public override bool CanSeek
        {
            get { return !chunkedBuffer.IsClosed; }
        }

        /// <summary>
        /// Returns true if this stream is writable.
        /// </summary>
        public override bool CanWrite
        {
            get { return !chunkedBuffer.IsClosed; }
        }

        /// <summary>
        /// The length of data in this tream
        /// </summary>
        public override long Length
        {
            get { return chunkedBuffer.WritePosition; }
        }

        /// <summary>
        /// The position the stream is in
        /// </summary>
        public override long Position
        {
            get
            {
                return chunkedBuffer.ReadPosition;
            }
            set
            {
                chunkedBuffer.ReadPosition = value;
            }
        }

        /// <summary>
        /// Creates a chunked buffer stream.
        /// </summary>
        /// <param name="chunkedBuffer"></param>
        public ChunkedBufferStream(ChunkedBuffer chunkedBuffer)
        {
            this.chunkedBuffer = chunkedBuffer;
        }

        /// <summary>
        /// Closes this stream and returns all pooled memory chunks into the pool.
        /// </summary>
        public override void Close()
        {
            base.Close();

            chunkedBuffer.Close();
        }

        /// <summary>
        /// Flushes this stream and clears any read pooled memory chunks.
        /// </summary>
        public override void Flush()
        {
            chunkedBuffer.Flush();
        }

        /// <summary>
        /// Reads data into the given buffer from the current position.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return chunkedBuffer.Read(buffer, offset, count);
        }

        /// <summary>
        /// Seeks the current position
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="origin"></param>
        /// <returns></returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            lock (chunkedBuffer)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        chunkedBuffer.ReadPosition = offset;
                        break;
                    case SeekOrigin.Current:
                        chunkedBuffer.ReadPosition += offset;
                        break;
                    case SeekOrigin.End:
                        long newPosition = Length + offset;

                        if (newPosition > Length)
                        {
                            throw new Exception("Applied offset to position exceeds length.");
                        }

                        chunkedBuffer.ReadPosition = newPosition;
                        break;
                }
            }

            return Position;
        }

        /// <summary>
        /// Sets the length of the stream. Note: Truncation is not supported.
        /// </summary>
        /// <param name="value"></param>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Writes the given data into this stream.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            chunkedBuffer.Write(buffer, offset, count);
        }
    }
}
