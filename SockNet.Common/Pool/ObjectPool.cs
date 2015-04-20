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
using System.Collections.Generic;
using ArenaNet.SockNet.Common.Collections;

namespace ArenaNet.SockNet.Common.Pool
{
    /// <summary>
    /// A pool of objects.
    /// </summary>
    public class ObjectPool<T>
    {
        public const int DEFAULT_TRIM_PERCENTILE = 65;
        public const int DEFAULT_IDEAL_MINIMUM_POOL_SIZE = 10;

        public delegate T OnNewObjectDelegate();
        public delegate T OnUpdateObjectDelegate(T obj);
        public delegate T OnDestroyedObjectDelegate(T obj);

        /// <summary>
        /// Number of objects in pool
        /// </summary>
        public int ObjectsInPool { get { return availableObjects; } }

        /// <summary>
        /// Number of total objects
        /// </summary>
        public int TotalNumberOfObjects { get { return totalPoolSize; } }

        private readonly int trimPercentile;
        private readonly int idealMinimumPoolSize;

        private Queue<PooledObject<T>> pool = new Queue<PooledObject<T>>();
        private int availableObjects = 0;
        private int totalPoolSize = 0;

        private OnNewObjectDelegate onNewObject;
        private OnUpdateObjectDelegate onUpdateObject;
        private OnDestroyedObjectDelegate onDestroyObject;

        /// <summary>
        /// Creates an object pool with a trim percentile. (At which percentile this pool will start trimming.)
        /// </summary>
        /// <param name="trimPercentile"></param>
        public ObjectPool(int trimPercentile = DEFAULT_TRIM_PERCENTILE, int idealMinimumPoolSize = DEFAULT_IDEAL_MINIMUM_POOL_SIZE)
        {
            this.trimPercentile = trimPercentile;
            this.idealMinimumPoolSize = idealMinimumPoolSize;
        }

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
                if (availableObjects == 0)
                {
                    pooledObject = new PooledObject<T>(this, onNewObject());

                    totalPoolSize++;
                }
                else
                {
                    pooledObject = pool.Dequeue();
                    availableObjects--;

                    if (onUpdateObject != null)
                    {
                        pooledObject.Value = onUpdateObject(pooledObject.Value);
                    }
                }

                pooledObject.Pooled = false;
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
                throw new ArgumentNullException("PooledObject cannot be null.");
            }

            if (pooledObject.Pooled)
            {
                throw new ArgumentException("PooledObject is already pooled.");
            }

            if (pooledObject.Pool != this)
            {
                throw new ArgumentException("PooledObject does not belong to this pool.");
            }

            bool doEnqueue = false;

            if (!pooledObject.Pooled)
            {
                lock (pooledObject)
                {
                    if (!pooledObject.Pooled)
                    {
                        doEnqueue = true;
                    }
                }
            }

            if (doEnqueue)
            {
                lock (pool)
                {
                    int currentPercentile = (int)(((float)(availableObjects + 1) / (float)totalPoolSize) * 100f);

                    if (onDestroyObject != null)
                    {
                        pooledObject.Value = onDestroyObject(pooledObject.Value);
                    }

                    if (currentPercentile > trimPercentile || totalPoolSize <= idealMinimumPoolSize)
                    {
                        pool.Enqueue(pooledObject);
                        pooledObject.Pooled = true;

                        availableObjects++;
                    }
                    else
                    {
                        totalPoolSize--;

                        pooledObject.Pool = null;
                        pooledObject.Pooled = false;
                    }
                }
            }
        }
    }
}
