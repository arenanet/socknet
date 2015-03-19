using System;
using System.Threading;
using System.Collections.Generic;
using ArenaNet.SockNet.Common.Collections;

namespace ArenaNet.SockNet.Common.Pool
{
    /// <summary>
    /// A pool of objects.
    /// </summary>
    public class ObjectPool<T>
    {
        public delegate T OnNewObjectDelegate();
        public delegate T OnUpdateObjectDelegate(T obj);
        public delegate T OnDestroyedObjectDelegate(T obj);

        /// <summary>
        /// Number of objects in pool
        /// </summary>
        public int ObjectsInPool { get { return pool.Count; } }

        /// <summary>
        /// Number of total objects
        /// </summary>
        public int TotalNumberOfObjects { get { return totalPoolSize; } }

        private Queue<PooledObject<T>> pool = new Queue<PooledObject<T>>();
        private int totalPoolSize = 0;

        private OnNewObjectDelegate onNewObject;
        private OnUpdateObjectDelegate onUpdateObject;
        private OnDestroyedObjectDelegate onDestroyObject;

        /// <summary>
        /// Creates a pool with the given constructors, updators, and destructors.
        /// </summary>
        /// <param name="onNewObject"></param>
        /// <param name="onUpdateObject"></param>
        /// <param name="onDestroyObject"></param>
        public ObjectPool(OnNewObjectDelegate onNewObject, OnUpdateObjectDelegate onUpdateObject = null, OnDestroyedObjectDelegate onDestroyObject = null)
        {
            this.onNewObject = onNewObject;
            this.onUpdateObject = onUpdateObject;
            this.onDestroyObject = onDestroyObject;
        }

        /// <summary>
        /// Borrows an object from the pool.
        /// </summary>
        /// <returns></returns>
        public PooledObject<T> Borrow()
        {
            PooledObject<T> pooledObject = null;

            lock (pool)
            {
                if (pool.Count == 0)
                {
                    pooledObject = new PooledObject<T>(this, onNewObject());

                    totalPoolSize++;
                }
                else
                {
                    pooledObject = pool.Dequeue();

                    if (onUpdateObject != null)
                    {
                        pooledObject.Value = onUpdateObject(pooledObject.Value);
                    }
                }
            }

            return pooledObject;
        }

        /// <summary>
        /// Returns an object to the pool.
        /// </summary>
        /// <param name="pooledObject"></param>
        public void Return(PooledObject<T> pooledObject)
        {
            if (pooledObject == null)
            {
                return;
            }

            bool doEnqueue = false;

            if (!pooledObject.Pooled)
            {
                lock (pooledObject)
                {
                    if (!pooledObject.Pooled)
                    {
                        doEnqueue = true;
                        pooledObject.Pooled = true;
                    }
                }
            }

            if (doEnqueue)
            {
                lock (pool)
                {
                    if (onDestroyObject != null)
                    {
                        pooledObject.Value = onDestroyObject(pooledObject.Value);
                    }

                    pool.Enqueue(pooledObject);
                }
            }
        }
    }
}
