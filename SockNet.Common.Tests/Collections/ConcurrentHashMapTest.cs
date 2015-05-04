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
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArenaNet.SockNet.Common.Collections
{
    [TestClass]
    public class ConcurrentHashMapTest
    {
        [TestMethod]
        public void TestSingleThreaded()
        {
            TestDictionaryWithSingleThread(new Dictionary<string, string>(), 10000);
            TestDictionaryWithSingleThread(new ConcurrentDictionary<string, string>(), 10000);
            TestDictionaryWithSingleThread(new ConcurrentHashMap<string, string>(StringComparer.Ordinal, 1024, 128), 10000);
        }

        [TestMethod]
        public void TestMultiThreaded()
        {
            TestDictionaryWithMultipleThreads(new ConcurrentDictionary<string, string>(), 10000);
            TestDictionaryWithMultipleThreads(new ConcurrentHashMap<string, string>(StringComparer.Ordinal, 1024, 128), 10000);
        }

        private static void TestDictionaryWithSingleThread(IDictionary<string, string> dictionary, int size)
        {
            GC.Collect();
            GC.WaitForFullGCComplete();
            GC.WaitForPendingFinalizers();

            ///>- PUT -<///
            DateTime startTime = DateTime.Now;

            for (int i = 0; i < size; i++)
            {
                dictionary["testKey" + i] = "testValue" + i;
            }

            TimeSpan timeTook = DateTime.Now.Subtract(startTime);

            Console.WriteLine(dictionary.GetType().Name + ":\t" + size + " puts took: {0:g}", timeTook);

            Assert.AreEqual(size, dictionary.Count);

            string[] getValues = new string[size];

            ///>- GET -<///
            startTime = DateTime.Now;

            for (int i = 0; i < size; i++)
            {
                dictionary.TryGetValue("testKey" + i, out getValues[i]);
            }

            timeTook = DateTime.Now.Subtract(startTime);

            Console.WriteLine(dictionary.GetType().Name + ":\t" + size + " gets took: {0:g}", timeTook);

            Assert.AreEqual(size, dictionary.Count);

            for (int i = 0; i < size; i++)
            {
                Assert.AreEqual("testValue" + i, getValues[i]);
            }

            ///>- UPDATE -<///
            startTime = DateTime.Now;

            for (int i = 0; i < size; i++)
            {
                dictionary["testKey" + i] = "testValue" + (size - i);
            }

            timeTook = DateTime.Now.Subtract(startTime);

            Console.WriteLine(dictionary.GetType().Name + ":\t" + size + " updates took: {0:g}", timeTook);

            Assert.AreEqual(size, dictionary.Count);

            ///>- REMOVE -<///
            startTime = DateTime.Now;

            for (int i = 0; i < size; i++)
            {
                dictionary.Remove("testKey" + i);
            }

            timeTook = DateTime.Now.Subtract(startTime);

            Console.WriteLine(dictionary.GetType().Name + ":\t" + size + " removes took: {0:g}", timeTook);

            Assert.AreEqual(0, dictionary.Count);

            Console.WriteLine();
        }

        private static void TestDictionaryWithMultipleThreads(IDictionary<string, string> dictionary, int size)
        {
            GC.Collect();
            GC.WaitForFullGCComplete();
            GC.WaitForPendingFinalizers();

            ///>- PUT -<///
            DateTime startTime = DateTime.Now;

            int completedCount = 0;

            for (int i = 0; i < size; i++)
            {
                ThreadPool.QueueUserWorkItem((object state) => 
                { 
                    dictionary["testKey" + (int)state] = "testValue" + (int)state;
                    Interlocked.Increment(ref completedCount);
                }, i);
            }

            TimeSpan timeTook;

            while ((timeTook = DateTime.Now.Subtract(startTime)).Seconds < 5 && completedCount < size)
            {
                // do stuff
            }

            Assert.AreEqual(size, dictionary.Count);

            Console.WriteLine(dictionary.GetType().Name + ":\t" + size + " puts took: {0:g}", timeTook);

            string[] getValues = new string[size];

            ///>- GET -<///
            completedCount = 0;
            startTime = DateTime.Now;

            for (int i = 0; i < size; i++)
            {
                ThreadPool.QueueUserWorkItem((object state) =>
                {
                    dictionary.TryGetValue("testKey" + (int)state, out getValues[(int)state]);
                    Interlocked.Increment(ref completedCount);
                }, i);
            }

            while ((timeTook = DateTime.Now.Subtract(startTime)).Seconds < 5 && completedCount < size)
            {
                // do stuff
            }

            Console.WriteLine(dictionary.GetType().Name + ":\t" + size + " gets took: {0:g}", timeTook);

            Assert.AreEqual(size, dictionary.Count);

            for (int i = 0; i < size; i++)
            {
                Assert.AreEqual("testValue" + i, getValues[i]);
            }

            ///>- UPDATE -<///
            completedCount = 0;
            startTime = DateTime.Now;

            for (int i = 0; i < size; i++)
            {
                ThreadPool.QueueUserWorkItem((object state) =>
                {
                    dictionary["testKey" + (int)state] = "testValue" + (int)state;
                    Interlocked.Increment(ref completedCount);
                }, i);
            }

            while ((timeTook = DateTime.Now.Subtract(startTime)).Seconds < 5 && completedCount < size)
            {
                // do stuff
            }

            Console.WriteLine(dictionary.GetType().Name + ":\t" + size + " updates took: {0:g}", timeTook);

            Assert.AreEqual(size, dictionary.Count);

            ///>- REMOVE -<///
            completedCount = 0;
            startTime = DateTime.Now;

            for (int i = 0; i < size; i++)
            {
                ThreadPool.QueueUserWorkItem((object state) =>
                {
                    dictionary.Remove("testKey" + (int)state);
                    Interlocked.Increment(ref completedCount);
                }, i);
            }

            while ((timeTook = DateTime.Now.Subtract(startTime)).Seconds < 5 && completedCount < size)
            {
                // do stuff
            }

            Console.WriteLine(dictionary.GetType().Name + ":\t" + size + " removes took: {0:g}", timeTook);

            Assert.AreEqual(0, dictionary.Count);
            
            Console.WriteLine();
        }
    }
}
