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
using System.Text;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using SecureString = System.Security.SecureString;
using RuntimeHelpers = System.Runtime.CompilerServices.RuntimeHelpers;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Server;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Common.Pool;
using ArenaNet.SockNet.Client;
using ArenaNet.SockNet.Protocols.Http;

namespace ArenaNet.SockNet.Protocols.WebSocket
{
    [TestClass]
    public class WebSockeClientSockNetChannelModuleTest
    {
        private const int DEFAULT_ASYNC_TIMEOUT = 5000;

        public class WebSocketEchoServer
        {
            private ServerSockNetChannel server;

            public IPEndPoint Endpoint { get { return new IPEndPoint(GetLocalIpAddress(), server == null ? -1 : server.LocalEndpoint.Port); } }

            public void Start(bool isTls = false)
            {
                server = SockNetServer.Create(GetLocalIpAddress(), 0);

                try
                {
                    server.AddModule(new WebSocketServerSockNetChannelModule("/", "localhost"));

                    if (isTls)
                    {
                        byte[] rawCert = Certificate.CreateSelfSignCertificatePfx("CN=\"test\"; C=\"USA\"", DateTime.Today.AddDays(-10), DateTime.Today.AddDays(+10));

                        server.BindWithTLS(new X509Certificate2(rawCert),
                            (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; }).WaitForValue(TimeSpan.FromSeconds(5));
                    }
                    else
                    {
                        server.Bind().WaitForValue(TimeSpan.FromSeconds(5));
                    }

                    Assert.IsTrue(server.IsActive);

                    server.Pipe.AddIncomingFirst<WebSocketFrame>((ISockNetChannel channel, ref WebSocketFrame data) =>
                    {
                        channel.Send(data);
                    });
                }
                catch (Exception)
                {
                    Stop();
                }
            }

            public void Stop()
            {
                if (server != null)
                {
                    server.Close();
                    server = null;
                }
            }

            private static IPAddress GetLocalIpAddress()
            {
                IPAddress response = null;

                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        response = address;
                        break;
                    }
                }

                return response;
            }
        }

        public class DummySockNetChannel : ISockNetChannel
        {
            public string Id { get { return "1"; } }

            public SockNetChannelPipe Pipe
            {
                get;
                set;
            }

            public ObjectPool<byte[]> BufferPool
            {
                get;
                set;
            }

            public IPEndPoint RemoteEndpoint
            {
                get;
                set;
            }

            public IPEndPoint LocalEndpoint
            {
                get;
                set;
            }

            public ISockNetChannel AddModule(ISockNetChannelModule module)
            {
                module.Install(this);

                return this;
            }

            public ISockNetChannel RemoveModule(ISockNetChannelModule module)
            {
                module.Uninstall(this);

                return this;
            }

            public bool HasModule(ISockNetChannelModule module)
            {
                return false;
            }

            public bool IsActive
            {
                get;
                set;
            }

            public Enum State
            {
                get;
                set;
            }

            public void Connect()
            {
                Pipe.HandleOpened();
            }

            public void Disconnect()
            {
                Pipe.HandleClosed();
            }

            public void Receive(ref object data)
            {
                Pipe.HandleIncomingData(ref data);
            }

            public Promise<ISockNetChannel> Send(object data)
            {
                Pipe.HandleOutgoingData(ref data);

                if (data is ChunkedBuffer)
                {
                    ((ChunkedBuffer)data).Close();
                }

                return new Promise<ISockNetChannel>(this);
            }

            public Promise<ISockNetChannel> Close()
            {
                return new Promise<ISockNetChannel>(this);
            }

            public SockNetChannelProtocol Protocol
            {
                get { return SockNetChannelProtocol.Tcp; }
            }
        }

        [TestMethod]
        public void TestContinuation()
        {
            DummySockNetChannel channel = new DummySockNetChannel()
            {
                State = null,
                IsActive = true,
                BufferPool = SockNetChannelGlobals.GlobalBufferPool
            };
            channel.Pipe = new SockNetChannelPipe(channel);

            WebSocketClientSockNetChannelModule module = new WebSocketClientSockNetChannelModule("/test", "test", null);
            channel.AddModule(module);
            channel.Connect();

            HttpResponse handshakeResponse = new HttpResponse(channel.BufferPool) { Code = "200", Reason = "OK", Version = "HTTP/1.1" };
            handshakeResponse.Header[WebSocketUtil.WebSocketAcceptHeader] = module.ExpectedAccept;
            object receiveResponse = handshakeResponse;
            channel.Receive(ref receiveResponse);

            WebSocketFrame continuation1 = WebSocketFrame.CreateTextFrame("This ", false, false, false);
            ChunkedBuffer buffer = ToBuffer(continuation1);
            receiveResponse = buffer;
            channel.Receive(ref receiveResponse);
            buffer.Close();

            Assert.IsTrue(receiveResponse is ChunkedBuffer);

            WebSocketFrame continuation2 = WebSocketFrame.CreateTextFrame("is ", false, true, false);
            buffer = ToBuffer(continuation2);
            receiveResponse = buffer;
            channel.Receive(ref receiveResponse);
            buffer.Close();

            Assert.IsTrue(receiveResponse is ChunkedBuffer);

            WebSocketFrame continuation3 = WebSocketFrame.CreateTextFrame("awesome!", false, true, true);
            buffer = ToBuffer(continuation3);
            receiveResponse = buffer;
            channel.Receive(ref receiveResponse);
            buffer.Close();

            Assert.IsTrue(receiveResponse is WebSocketFrame);
            Assert.AreEqual("This is awesome!", ((WebSocketFrame)receiveResponse).DataAsString);

            Console.WriteLine("Pool stats: " + SockNetChannelGlobals.GlobalBufferPool.ObjectsInPool + "/" + SockNetChannelGlobals.GlobalBufferPool.TotalNumberOfObjects);
        }

        [TestMethod]
        public void TestLargeMessage()
        {
            DummySockNetChannel channel = new DummySockNetChannel()
            {
                State = null,
                IsActive = true,
                BufferPool = SockNetChannelGlobals.GlobalBufferPool
            };
            channel.Pipe = new SockNetChannelPipe(channel);

            WebSocketClientSockNetChannelModule module = new WebSocketClientSockNetChannelModule("/test", "test", null);
            channel.AddModule(module);
            channel.Connect();

            HttpResponse handshakeResponse = new HttpResponse(null) { Code = "200", Reason = "OK", Version = "HTTP/1.1" };
            handshakeResponse.Header[WebSocketUtil.WebSocketAcceptHeader] = module.ExpectedAccept;
            object receiveResponse = handshakeResponse;
            channel.Receive(ref receiveResponse);

            Random random = new Random(this.GetHashCode() ^ (int)DateTime.Now.Subtract(new DateTime(2000, 1, 1)).TotalMilliseconds);

            for (int n = 0; n < 100; n++)
            {
                byte[] data = new byte[random.Next(50000, 150000)];
                random.NextBytes(data);

                WebSocketFrame frame = WebSocketFrame.CreateBinaryFrame(data, false);
                ChunkedBuffer buffer = ToBuffer(frame);
                receiveResponse = buffer;
                channel.Receive(ref receiveResponse);

                Assert.IsTrue(receiveResponse is WebSocketFrame);
                Assert.AreEqual(data.Length, ((WebSocketFrame)receiveResponse).Data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    Assert.AreEqual(data[i], ((WebSocketFrame)receiveResponse).Data[i]);
                }
                buffer.Close();
            }

            Console.WriteLine("Pool stats: " + SockNetChannelGlobals.GlobalBufferPool.ObjectsInPool + "/" + SockNetChannelGlobals.GlobalBufferPool.TotalNumberOfObjects);
        }

        public ChunkedBuffer ToBuffer(WebSocketFrame frame)
        {
            ChunkedBuffer buffer = new ChunkedBuffer(SockNetChannelGlobals.GlobalBufferPool);
            frame.Write(buffer.Stream, true);
            return buffer;
        }

        [TestMethod]
        public void TestEchoWithMask()
        {
            WebSocketEchoServer server = new WebSocketEchoServer();

            try
            {
                server.Start();

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint)
                    .AddModule(new WebSocketClientSockNetChannelModule("/", "localhost", (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); }));

                client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue((bool)currentObject);

                client.Pipe.AddIncomingLast<WebSocketFrame>((ISockNetChannel sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

                client.Send(WebSocketFrame.CreateTextFrame("some test", true));

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue(currentObject is WebSocketFrame);

                Assert.AreEqual("some test", ((WebSocketFrame)currentObject).DataAsString);

                Console.WriteLine("Got response: \n" + ((WebSocketFrame)currentObject).DataAsString);

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void TestEchoWithMaskWithSsl()
        {
            WebSocketEchoServer server = new WebSocketEchoServer();

            try
            {
                server.Start(true);

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint)
                    .AddModule(new WebSocketClientSockNetChannelModule("/", "localhost", (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); }));

                client.ConnectWithTLS((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; })
                    .WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue((bool)currentObject);

                client.Pipe.AddIncomingLast<WebSocketFrame>((ISockNetChannel sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

                client.Send(WebSocketFrame.CreateTextFrame("some test", true));

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue(currentObject is WebSocketFrame);

                Assert.AreEqual("some test", ((WebSocketFrame)currentObject).DataAsString);

                Console.WriteLine("Got response: \n" + ((WebSocketFrame)currentObject).DataAsString);

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void TestEchoWithoutMask()
        {
            WebSocketEchoServer server = new WebSocketEchoServer();

            try
            {
                server.Start();

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint)
                    .AddModule(new WebSocketClientSockNetChannelModule("/", "localhost", (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); }));

                client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue((bool)currentObject);

                client.Pipe.AddIncomingLast<WebSocketFrame>((ISockNetChannel sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

                client.Send(WebSocketFrame.CreateTextFrame("some test", true));

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue(currentObject is WebSocketFrame);

                Assert.AreEqual("some test", ((WebSocketFrame)currentObject).DataAsString);

                Console.WriteLine("Got response: \n" + ((WebSocketFrame)currentObject).DataAsString);

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void TestLotsOfMessages()
        {
            WebSocketEchoServer server = new WebSocketEchoServer();

            try
            {
                server.Start();

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint)
                    .AddModule(new WebSocketClientSockNetChannelModule("/", "localhost", (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); }));

                client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue((bool)currentObject);

                client.Pipe.AddIncomingLast<WebSocketFrame>((ISockNetChannel sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

                byte[] randomData = new byte[2000]; 
                new Random(GetHashCode() ^ DateTime.Now.Millisecond).NextBytes(randomData);

                for (int i = 0; i < 1000; i++)
                {
                    client.Send(WebSocketFrame.CreateBinaryFrame(randomData, false));
                }

                int receivedMessages = 0;

                for (int i = 0; i < 1000; i++)
                {
                    if (receivedMessages < 1000 && blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT))
                    {
                        receivedMessages++;
                    }
                    else
                    {
                        break;
                    }
                }

                Assert.AreEqual(1000, receivedMessages);

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }

        internal class Certificate
        {
            public static byte[] CreateSelfSignCertificatePfx(
                string x500,
                DateTime startTime,
                DateTime endTime)
            {
                byte[] pfxData = CreateSelfSignCertificatePfx(
                    x500,
                    startTime,
                    endTime,
                    (SecureString)null);
                return pfxData;
            }

            public static byte[] CreateSelfSignCertificatePfx(
                string x500,
                DateTime startTime,
                DateTime endTime,
                string insecurePassword)
            {
                byte[] pfxData;
                SecureString password = null;

                try
                {
                    if (!string.IsNullOrEmpty(insecurePassword))
                    {
                        password = new SecureString();
                        foreach (char ch in insecurePassword)
                        {
                            password.AppendChar(ch);
                        }

                        password.MakeReadOnly();
                    }

                    pfxData = CreateSelfSignCertificatePfx(
                        x500,
                        startTime,
                        endTime,
                        password);
                }
                finally
                {
                    if (password != null)
                    {
                        password.Dispose();
                    }
                }

                return pfxData;
            }

            public static byte[] CreateSelfSignCertificatePfx(
                string x500,
                DateTime startTime,
                DateTime endTime,
                SecureString password)
            {
                byte[] pfxData;

                if (x500 == null)
                {
                    x500 = "";
                }

                SystemTime startSystemTime = ToSystemTime(startTime);
                SystemTime endSystemTime = ToSystemTime(endTime);
                string containerName = Guid.NewGuid().ToString();

                GCHandle dataHandle = new GCHandle();
                IntPtr providerContext = IntPtr.Zero;
                IntPtr cryptKey = IntPtr.Zero;
                IntPtr certContext = IntPtr.Zero;
                IntPtr certStore = IntPtr.Zero;
                IntPtr storeCertContext = IntPtr.Zero;
                IntPtr passwordPtr = IntPtr.Zero;
                RuntimeHelpers.PrepareConstrainedRegions();
                try
                {
                    Check(NativeMethods.CryptAcquireContextW(
                        out providerContext,
                        containerName,
                        null,
                        1, // PROV_RSA_FULL
                        8)); // CRYPT_NEWKEYSET

                    Check(NativeMethods.CryptGenKey(
                        providerContext,
                        1, // AT_KEYEXCHANGE
                        1, // CRYPT_EXPORTABLE
                        out cryptKey));

                    IntPtr errorStringPtr;
                    int nameDataLength = 0;
                    byte[] nameData;

                    // errorStringPtr gets a pointer into the middle of the x500 string,
                    // so x500 needs to be pinned until after we've copied the value
                    // of errorStringPtr.
                    dataHandle = GCHandle.Alloc(x500, GCHandleType.Pinned);

                    if (!NativeMethods.CertStrToNameW(
                        0x00010001, // X509_ASN_ENCODING | PKCS_7_ASN_ENCODING
                        dataHandle.AddrOfPinnedObject(),
                        3, // CERT_X500_NAME_STR = 3
                        IntPtr.Zero,
                        null,
                        ref nameDataLength,
                        out errorStringPtr))
                    {
                        string error = Marshal.PtrToStringUni(errorStringPtr);
                        throw new ArgumentException(error);
                    }

                    nameData = new byte[nameDataLength];

                    if (!NativeMethods.CertStrToNameW(
                        0x00010001, // X509_ASN_ENCODING | PKCS_7_ASN_ENCODING
                        dataHandle.AddrOfPinnedObject(),
                        3, // CERT_X500_NAME_STR = 3
                        IntPtr.Zero,
                        nameData,
                        ref nameDataLength,
                        out errorStringPtr))
                    {
                        string error = Marshal.PtrToStringUni(errorStringPtr);
                        throw new ArgumentException(error);
                    }

                    dataHandle.Free();

                    dataHandle = GCHandle.Alloc(nameData, GCHandleType.Pinned);
                    CryptoApiBlob nameBlob = new CryptoApiBlob(
                        nameData.Length,
                        dataHandle.AddrOfPinnedObject());

                    CryptKeyProviderInformation kpi = new CryptKeyProviderInformation();
                    kpi.ContainerName = containerName;
                    kpi.ProviderType = 1; // PROV_RSA_FULL
                    kpi.KeySpec = 1; // AT_KEYEXCHANGE

                    certContext = NativeMethods.CertCreateSelfSignCertificate(
                        providerContext,
                        ref nameBlob,
                        0,
                        ref kpi,
                        IntPtr.Zero, // default = SHA1RSA
                        ref startSystemTime,
                        ref endSystemTime,
                        IntPtr.Zero);
                    Check(certContext != IntPtr.Zero);
                    dataHandle.Free();

                    certStore = NativeMethods.CertOpenStore(
                        "Memory", // sz_CERT_STORE_PROV_MEMORY
                        0,
                        IntPtr.Zero,
                        0x2000, // CERT_STORE_CREATE_NEW_FLAG
                        IntPtr.Zero);
                    Check(certStore != IntPtr.Zero);

                    Check(NativeMethods.CertAddCertificateContextToStore(
                        certStore,
                        certContext,
                        1, // CERT_STORE_ADD_NEW
                        out storeCertContext));

                    NativeMethods.CertSetCertificateContextProperty(
                        storeCertContext,
                        2, // CERT_KEY_PROV_INFO_PROP_ID
                        0,
                        ref kpi);

                    if (password != null)
                    {
                        passwordPtr = Marshal.SecureStringToCoTaskMemUnicode(password);
                    }

                    CryptoApiBlob pfxBlob = new CryptoApiBlob();
                    Check(NativeMethods.PFXExportCertStoreEx(
                        certStore,
                        ref pfxBlob,
                        passwordPtr,
                        IntPtr.Zero,
                        7)); // EXPORT_PRIVATE_KEYS | REPORT_NO_PRIVATE_KEY | REPORT_NOT_ABLE_TO_EXPORT_PRIVATE_KEY

                    pfxData = new byte[pfxBlob.DataLength];
                    dataHandle = GCHandle.Alloc(pfxData, GCHandleType.Pinned);
                    pfxBlob.Data = dataHandle.AddrOfPinnedObject();
                    Check(NativeMethods.PFXExportCertStoreEx(
                        certStore,
                        ref pfxBlob,
                        passwordPtr,
                        IntPtr.Zero,
                        7)); // EXPORT_PRIVATE_KEYS | REPORT_NO_PRIVATE_KEY | REPORT_NOT_ABLE_TO_EXPORT_PRIVATE_KEY
                    dataHandle.Free();
                }
                finally
                {
                    if (passwordPtr != IntPtr.Zero)
                    {
                        Marshal.ZeroFreeCoTaskMemUnicode(passwordPtr);
                    }

                    if (dataHandle.IsAllocated)
                    {
                        dataHandle.Free();
                    }

                    if (certContext != IntPtr.Zero)
                    {
                        NativeMethods.CertFreeCertificateContext(certContext);
                    }

                    if (storeCertContext != IntPtr.Zero)
                    {
                        NativeMethods.CertFreeCertificateContext(storeCertContext);
                    }

                    if (certStore != IntPtr.Zero)
                    {
                        NativeMethods.CertCloseStore(certStore, 0);
                    }

                    if (cryptKey != IntPtr.Zero)
                    {
                        NativeMethods.CryptDestroyKey(cryptKey);
                    }

                    if (providerContext != IntPtr.Zero)
                    {
                        NativeMethods.CryptReleaseContext(providerContext, 0);
                        NativeMethods.CryptAcquireContextW(
                            out providerContext,
                            containerName,
                            null,
                            1, // PROV_RSA_FULL
                            0x10); // CRYPT_DELETEKEYSET
                    }
                }

                return pfxData;
            }

            private static SystemTime ToSystemTime(DateTime dateTime)
            {
                long fileTime = dateTime.ToFileTime();
                SystemTime systemTime;
                Check(NativeMethods.FileTimeToSystemTime(ref fileTime, out systemTime));
                return systemTime;
            }

            private static void Check(bool nativeCallSucceeded)
            {
                if (!nativeCallSucceeded)
                {
                    int error = Marshal.GetHRForLastWin32Error();
                    Marshal.ThrowExceptionForHR(error);
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct SystemTime
            {
                public short Year;
                public short Month;
                public short DayOfWeek;
                public short Day;
                public short Hour;
                public short Minute;
                public short Second;
                public short Milliseconds;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct CryptoApiBlob
            {
                public int DataLength;
                public IntPtr Data;

                public CryptoApiBlob(int dataLength, IntPtr data)
                {
                    this.DataLength = dataLength;
                    this.Data = data;
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct CryptKeyProviderInformation
            {
                [MarshalAs(UnmanagedType.LPWStr)]
                public string ContainerName;
                [MarshalAs(UnmanagedType.LPWStr)]
                public string ProviderName;
                public int ProviderType;
                public int Flags;
                public int ProviderParameterCount;
                public IntPtr ProviderParameters; // PCRYPT_KEY_PROV_PARAM
                public int KeySpec;
            }

            private static class NativeMethods
            {
                [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool FileTimeToSystemTime(
                    [In] ref long fileTime,
                    out SystemTime systemTime);

                [DllImport("AdvApi32.dll", SetLastError = true, ExactSpelling = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool CryptAcquireContextW(
                    out IntPtr providerContext,
                    [MarshalAs(UnmanagedType.LPWStr)] string container,
                    [MarshalAs(UnmanagedType.LPWStr)] string provider,
                    int providerType,
                    int flags);

                [DllImport("AdvApi32.dll", SetLastError = true, ExactSpelling = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool CryptReleaseContext(
                    IntPtr providerContext,
                    int flags);

                [DllImport("AdvApi32.dll", SetLastError = true, ExactSpelling = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool CryptGenKey(
                    IntPtr providerContext,
                    int algorithmId,
                    int flags,
                    out IntPtr cryptKeyHandle);

                [DllImport("AdvApi32.dll", SetLastError = true, ExactSpelling = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool CryptDestroyKey(
                    IntPtr cryptKeyHandle);

                [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool CertStrToNameW(
                    int certificateEncodingType,
                    IntPtr x500,
                    int strType,
                    IntPtr reserved,
                    [MarshalAs(UnmanagedType.LPArray)] [Out] byte[] encoded,
                    ref int encodedLength,
                    out IntPtr errorString);

                [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
                public static extern IntPtr CertCreateSelfSignCertificate(
                    IntPtr providerHandle,
                    [In] ref CryptoApiBlob subjectIssuerBlob,
                    int flags,
                    [In] ref CryptKeyProviderInformation keyProviderInformation,
                    IntPtr signatureAlgorithm,
                    [In] ref SystemTime startTime,
                    [In] ref SystemTime endTime,
                    IntPtr extensions);

                [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool CertFreeCertificateContext(
                    IntPtr certificateContext);

                [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
                public static extern IntPtr CertOpenStore(
                    [MarshalAs(UnmanagedType.LPStr)] string storeProvider,
                    int messageAndCertificateEncodingType,
                    IntPtr cryptProvHandle,
                    int flags,
                    IntPtr parameters);

                [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool CertCloseStore(
                    IntPtr certificateStoreHandle,
                    int flags);

                [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool CertAddCertificateContextToStore(
                    IntPtr certificateStoreHandle,
                    IntPtr certificateContext,
                    int addDisposition,
                    out IntPtr storeContextPtr);

                [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool CertSetCertificateContextProperty(
                    IntPtr certificateContext,
                    int propertyId,
                    int flags,
                    [In] ref CryptKeyProviderInformation data);

                [DllImport("Crypt32.dll", SetLastError = true, ExactSpelling = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool PFXExportCertStoreEx(
                    IntPtr certificateStoreHandle,
                    ref CryptoApiBlob pfxBlob,
                    IntPtr password,
                    IntPtr reserved,
                    int flags);
            }
        }
    }
}
