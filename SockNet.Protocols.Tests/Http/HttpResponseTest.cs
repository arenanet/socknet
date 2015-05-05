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
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.Medley.Pool;
using ArenaNet.Medley.Concurrent;

namespace ArenaNet.SockNet.Protocols.Http
{
    [TestClass]
    public class HttpResponseTest
    {
        private ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[100]; });

        [TestMethod]
        public void TestSimpleClosed()
        {
            string sampleRequest = "HTTP/1.0 200 OK\r\nHost: localhost\r\n\r\n";

            ChunkedBuffer buffer = new ChunkedBuffer(pool);
            buffer.Write(Encoding.ASCII.GetBytes(sampleRequest), 0, Encoding.ASCII.GetByteCount(sampleRequest));

            HttpResponse response = new HttpResponse(pool);
            Assert.IsTrue(response.Parse(buffer.Stream, true));

            Assert.AreEqual("HTTP/1.0", response.Version);
            Assert.AreEqual("200", response.Code);
            Assert.AreEqual("OK", response.Reason);
            Assert.AreEqual("HTTP/1.0 200 OK", response.CommandLine);
            Assert.AreEqual("localhost", response.Header["Host"]);
            Assert.AreEqual(0, response.BodySize);
            Assert.AreEqual(0, response.Body.WritePosition);

            MemoryStream stream = new MemoryStream();
            response.Write(stream, false);
            stream.Position = 0;
            
            using (StreamReader reader = new StreamReader(stream))
            {
                Assert.AreEqual(sampleRequest, reader.ReadToEnd());
            }
        }

        [TestMethod]
        public void TestContentLengthNotClosed()
        {
            string sampleContent = "<test><val>hello</val></test>";
            int sampleContentLength = Encoding.UTF8.GetByteCount(sampleContent);
            string sampleRequest = "HTTP/1.0 200 OK\r\nHost: localhost\r\nContent-Length: " + sampleContentLength + "\r\n\r\n" + sampleContent;

            ChunkedBuffer buffer = new ChunkedBuffer(pool);
            buffer.Write(Encoding.ASCII.GetBytes(sampleRequest), 0, Encoding.ASCII.GetByteCount(sampleRequest));

            HttpResponse response = new HttpResponse(pool);
            Assert.IsTrue(response.Parse(buffer.Stream, false));

            Assert.AreEqual("HTTP/1.0", response.Version);
            Assert.AreEqual("200", response.Code);
            Assert.AreEqual("OK", response.Reason);
            Assert.AreEqual("HTTP/1.0 200 OK", response.CommandLine);
            Assert.AreEqual("localhost", response.Header["Host"]);
            Assert.AreEqual(sampleContentLength, response.BodySize);
            Assert.AreEqual(sampleContentLength, response.Body.WritePosition);

            MemoryStream stream = new MemoryStream();
            response.Write(stream, false);
            stream.Position = 0;

            using (StreamReader reader = new StreamReader(stream))
            {
                Assert.AreEqual(sampleRequest, reader.ReadToEnd());
            }
        }

        [TestMethod]
        public void TestContentLengthPartial()
        {
            string sampleContent = "<test><val>hello</val></test>";
            int sampleContentLength = Encoding.UTF8.GetByteCount(sampleContent);
            string sampleRequest = "HTTP/1.0 200 OK\r\nHost: localhost\r\nContent-Length: " + sampleContentLength + "\r\n\r\n" + sampleContent;

            int partialSize = sampleRequest.Length / 3;
            string sampleRequest1 = sampleRequest.Substring(0, partialSize);
            string sampleRequest2 = sampleRequest.Substring(partialSize, partialSize);
            string sampleRequest3 = sampleRequest.Substring(partialSize * 2, sampleRequest.Length - (partialSize * 2));

            ChunkedBuffer buffer = new ChunkedBuffer(pool);
            buffer.Write(Encoding.ASCII.GetBytes(sampleRequest1), 0, Encoding.ASCII.GetByteCount(sampleRequest1));

            HttpResponse response = new HttpResponse(pool);
            Assert.IsFalse(response.Parse(buffer.Stream, false));

            buffer.Write(Encoding.ASCII.GetBytes(sampleRequest2), 0, Encoding.ASCII.GetByteCount(sampleRequest2));
            Assert.IsFalse(response.Parse(buffer.Stream, false));

            buffer.Write(Encoding.ASCII.GetBytes(sampleRequest3), 0, Encoding.ASCII.GetByteCount(sampleRequest3));
            Assert.IsTrue(response.Parse(buffer.Stream, false));

            Assert.AreEqual("HTTP/1.0", response.Version);
            Assert.AreEqual("200", response.Code);
            Assert.AreEqual("OK", response.Reason);
            Assert.AreEqual("HTTP/1.0 200 OK", response.CommandLine);
            Assert.AreEqual("localhost", response.Header["Host"]);
            Assert.AreEqual(sampleContentLength, response.BodySize);
            Assert.AreEqual(sampleContentLength, response.Body.WritePosition);

            MemoryStream stream = new MemoryStream();
            response.Write(stream, false);
            stream.Position = 0;

            using (StreamReader reader = new StreamReader(stream))
            {
                Assert.AreEqual(sampleRequest, reader.ReadToEnd());
            }
        }

        [TestMethod]
        public void TestContentLengthClosed()
        {
            string sampleContent = "<test><val>hello</val></test>";
            int sampleContentLength = Encoding.UTF8.GetByteCount(sampleContent);
            string sampleRequest = "HTTP/1.0 200 OK\r\nHost: localhost\r\nContent-Length: " + sampleContentLength + "\r\n\r\n" + sampleContent;

            ChunkedBuffer buffer = new ChunkedBuffer(pool);
            buffer.Write(Encoding.ASCII.GetBytes(sampleRequest), 0, Encoding.ASCII.GetByteCount(sampleRequest));

            HttpResponse response = new HttpResponse(pool);
            Assert.IsTrue(response.Parse(buffer.Stream, true));

            Assert.AreEqual("HTTP/1.0", response.Version);
            Assert.AreEqual("200", response.Code);
            Assert.AreEqual("OK", response.Reason);
            Assert.AreEqual("HTTP/1.0 200 OK", response.CommandLine);
            Assert.AreEqual("localhost", response.Header["Host"]);
            Assert.AreEqual(sampleContentLength, response.BodySize);
            Assert.AreEqual(sampleContentLength, response.Body.WritePosition);

            MemoryStream stream = new MemoryStream();
            response.Write(stream, false);
            stream.Position = 0;

            using (StreamReader reader = new StreamReader(stream))
            {
                Assert.AreEqual(sampleRequest, reader.ReadToEnd());
            }
        }

        [TestMethod]
        public void TestChunked()
        {
            string sampleContent = "<test><val>hello</val></test>";

            int sampleContentLength = Encoding.UTF8.GetByteCount(sampleContent);

            string chunk1Content = "<test><val>";
            string chunk2Content = "hello</val>";
            string chunk3Content = "</test>";

            int chunk1ContentLength = Encoding.UTF8.GetByteCount(chunk1Content);
            int chunk2ContentLength = Encoding.UTF8.GetByteCount(chunk2Content);
            int chunk3ContentLength = Encoding.UTF8.GetByteCount(chunk3Content);

            string chunk1Request = "HTTP/1.0 200 OK\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n" + string.Format("{0:X}", chunk1ContentLength) + "\r\n" + chunk1Content + "\r\n";
            string chunk2Request = string.Format("{0:X}", chunk2ContentLength) + "\r\n" + chunk2Content + "\r\n";
            string chunk3Request = string.Format("{0:X}", chunk3ContentLength) + "\r\n" + chunk3Content + "\r\n";
            string chunk4Request = "0\r\n\r\n";

            ChunkedBuffer buffer1 = new ChunkedBuffer(pool);
            buffer1.Write(Encoding.ASCII.GetBytes(chunk1Request), 0, Encoding.ASCII.GetByteCount(chunk1Request));

            ChunkedBuffer buffer2 = new ChunkedBuffer(pool);
            buffer2.Write(Encoding.ASCII.GetBytes(chunk2Request), 0, Encoding.ASCII.GetByteCount(chunk2Request));
            
            ChunkedBuffer buffer3 = new ChunkedBuffer(pool);
            buffer3.Write(Encoding.ASCII.GetBytes(chunk3Request), 0, Encoding.ASCII.GetByteCount(chunk3Request));
            
            ChunkedBuffer buffer4 = new ChunkedBuffer(pool);
            buffer4.Write(Encoding.ASCII.GetBytes(chunk4Request), 0, Encoding.ASCII.GetByteCount(chunk4Request));

            HttpResponse response = new HttpResponse(pool);
            Assert.IsFalse(response.IsChunked);
            Assert.IsFalse(response.Parse(buffer1.Stream, false));
            Assert.IsTrue(response.IsChunked);
            Assert.IsFalse(response.Parse(buffer2.Stream, false));
            Assert.IsTrue(response.IsChunked);
            Assert.IsFalse(response.Parse(buffer3.Stream, false));
            Assert.IsTrue(response.IsChunked);
            Assert.IsTrue(response.Parse(buffer4.Stream, false));
            Assert.IsTrue(response.IsChunked);

            Assert.AreEqual("HTTP/1.0", response.Version);
            Assert.AreEqual("200", response.Code);
            Assert.AreEqual("OK", response.Reason);
            Assert.AreEqual("HTTP/1.0 200 OK", response.CommandLine);
            Assert.AreEqual("localhost", response.Header["Host"]);
            Assert.AreEqual(sampleContentLength, response.BodySize);
            Assert.AreEqual(sampleContentLength, response.Body.WritePosition);

            MemoryStream stream = new MemoryStream();
            response.Write(stream, false);
            stream.Position = 0;

            using (StreamReader reader = new StreamReader(stream))
            {
                Assert.AreEqual("HTTP/1.0 200 OK\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n" + sampleContent, reader.ReadToEnd());
            }
        }
    }
}
