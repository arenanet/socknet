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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArenaNet.SockNet.Common.Pool
{
    [TestClass]
    public class ObjectPoolTest
    {
        [TestMethod]
        public void TestGarbageCollectedPooledObject()
        {
            ObjectPool<string> pool = new ObjectPool<string>(() => { return "hello"; });

            {
                PooledObject<string> pooledObject = pool.Borrow();

                Assert.AreEqual("hello", pooledObject.Value);
                Assert.AreEqual(0, pool.ObjectsInPool);
                Assert.AreEqual(1, pool.TotalNumberOfObjects);

                pooledObject = null;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();

            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(0, pool.TotalNumberOfObjects);
        }

        [TestMethod]
        public void TestBorrow()
        {
            ObjectPool<string> pool = new ObjectPool<string>(() => { return "hello"; });
            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(0, pool.TotalNumberOfObjects);

            PooledObject<string> pooledObj1 = pool.Borrow();
            PooledObject<string> pooledObj2 = pool.Borrow();

            Assert.AreEqual(pool, pooledObj1.Pool);
            Assert.AreEqual("hello", pooledObj1.Value);
            Assert.AreEqual(pool, pooledObj2.Pool);
            Assert.AreEqual("hello", pooledObj2.Value);
            
            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(2, pool.TotalNumberOfObjects);
        }

        [TestMethod]
        public void TestBorrowAndReturn()
        {
            ObjectPool<string> pool = new ObjectPool<string>(() => { return "hello"; });
            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(0, pool.TotalNumberOfObjects);

            PooledObject<string> pooledObj1 = pool.Borrow();
            PooledObject<string> pooledObj2 = pool.Borrow();

            Assert.AreEqual(pool, pooledObj1.Pool);
            Assert.AreEqual("hello", pooledObj1.Value);
            Assert.AreEqual(pool, pooledObj2.Pool);
            Assert.AreEqual("hello", pooledObj2.Value);

            pool.Return(pooledObj2);

            Assert.AreEqual(1, pool.ObjectsInPool);
            Assert.AreEqual(2, pool.TotalNumberOfObjects);
        }

        [TestMethod]
        public void TestBorrowAndPooledObjectReturn()
        {
            ObjectPool<string> pool = new ObjectPool<string>(() => { return "hello"; });
            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(0, pool.TotalNumberOfObjects);

            PooledObject<string> pooledObj1 = pool.Borrow();
            PooledObject<string> pooledObj2 = pool.Borrow();

            Assert.AreEqual(pool, pooledObj1.Pool);
            Assert.AreEqual("hello", pooledObj1.Value);
            Assert.AreEqual(pool, pooledObj2.Pool);
            Assert.AreEqual("hello", pooledObj2.Value);

            pooledObj1.Return();

            Assert.AreEqual(1, pool.ObjectsInPool);
            Assert.AreEqual(2, pool.TotalNumberOfObjects);
        }

        [TestMethod]
        public void TestBorrowAndReturnAndBorrow()
        {
            ObjectPool<string> pool = new ObjectPool<string>(() => { return "hello"; });
            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(0, pool.TotalNumberOfObjects);

            PooledObject<string> pooledObj1 = pool.Borrow();
            PooledObject<string> pooledObj2 = pool.Borrow();

            Assert.AreEqual(pool, pooledObj1.Pool);
            Assert.AreEqual("hello", pooledObj1.Value);
            Assert.AreEqual(pool, pooledObj2.Pool);
            Assert.AreEqual("hello", pooledObj2.Value);

            pooledObj1.Return();

            Assert.AreEqual(1, pool.ObjectsInPool);
            Assert.AreEqual(2, pool.TotalNumberOfObjects);

            PooledObject<string> pooledObj1Again = pool.Borrow();

            Assert.AreEqual(pooledObj1, pooledObj1Again);

            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(2, pool.TotalNumberOfObjects);
        }
    }
}
