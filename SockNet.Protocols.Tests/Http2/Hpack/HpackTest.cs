using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArenaNet.SockNet.Protocols.Http2.Hpack
{
    [TestClass]
    public class HpackTest
    {
        const int maxHeaderSize = 4096;
        const int maxHeaderTableSize = 4096;

        [TestMethod]
        public void TestSingleEncodingNotSensitive()
        {
            HpackHeader outHeader = new HpackHeader("someName", "someValue");
            bool outSensitive = false;

            HpackHeader inHeader = null;
            bool? inSensitive = null;

            MemoryStream stream = new MemoryStream();
            HpackEncoder encoder = new HpackEncoder(maxHeaderTableSize);
            encoder.EncodeHeader(stream, outHeader, outSensitive);

            stream.Position = 0;

            HpackDecoder decoder = new HpackDecoder(maxHeaderSize, maxHeaderTableSize);
            decoder.Decode(stream, (HpackHeader header, bool sensitive) => { inHeader = header; inSensitive = sensitive; });
            decoder.EndHeaderBlock();

            Assert.IsNotNull(inHeader);
            Assert.IsNotNull(inSensitive);

            Console.WriteLine("outsize: " + outHeader.Size);
            Console.WriteLine("insize: " + inHeader.Size);

            Console.WriteLine("out: " + outHeader);
            Console.WriteLine("in: " + inHeader);

            Assert.AreEqual(outHeader, inHeader);
            Assert.AreEqual(outSensitive, inSensitive);
        }

        [TestMethod]
        public void TestSingleEncodingSensitive()
        {
            HpackHeader outHeader = new HpackHeader("someName", "someValue");
            bool outSensitive = true;

            HpackHeader inHeader = null;
            bool? inSensitive = null;

            MemoryStream stream = new MemoryStream();
            HpackEncoder encoder = new HpackEncoder(maxHeaderTableSize);
            encoder.EncodeHeader(stream, outHeader, outSensitive);

            stream.Position = 0;

            HpackDecoder decoder = new HpackDecoder(maxHeaderSize, maxHeaderTableSize);
            decoder.Decode(stream, (HpackHeader header, bool sensitive) => { inHeader = header; inSensitive = sensitive; });
            decoder.EndHeaderBlock();

            Assert.IsNotNull(inHeader);
            Assert.IsNotNull(inSensitive);

            Console.WriteLine("outsize: " + outHeader.Size);
            Console.WriteLine("insize: " + inHeader.Size);

            Console.WriteLine("out: " + outHeader);
            Console.WriteLine("in: " + inHeader);

            Assert.AreEqual(outHeader, inHeader);
            Assert.AreEqual(outSensitive, inSensitive);
        }

        [TestMethod]
        public void TestMultipleEncoding()
        {
            HpackHeader outHeader1 = new HpackHeader("someName1", "someValue1");
            HpackHeader outHeader2 = new HpackHeader("someName2", "someValue2");
            HpackHeader outHeader3 = new HpackHeader("someName3", "someValue3");

            Dictionary<HpackHeader, bool> inHeaders = new Dictionary<HpackHeader, bool>();

            MemoryStream stream = new MemoryStream();
            HpackEncoder encoder = new HpackEncoder(maxHeaderTableSize);
            encoder.EncodeHeader(stream, outHeader1, false);
            encoder.EncodeHeader(stream, outHeader2, false);
            encoder.EncodeHeader(stream, outHeader3, false);

            stream.Position = 0;

            HpackDecoder decoder = new HpackDecoder(maxHeaderSize, maxHeaderTableSize);
            decoder.Decode(stream, (HpackHeader header, bool sensitive) => { inHeaders[header] = true; });
            decoder.EndHeaderBlock();

            Console.WriteLine("inheaders: " + inHeaders.Count);

            foreach (HpackHeader header in inHeaders.Keys)
            {
                Console.WriteLine("inheader: " + header);
            }

            Assert.IsTrue(inHeaders[outHeader1]);
            Assert.IsTrue(inHeaders[outHeader2]);
            Assert.IsTrue(inHeaders[outHeader3]);
        }
    }
}
