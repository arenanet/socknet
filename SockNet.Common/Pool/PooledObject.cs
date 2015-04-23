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
using System.Text;
using System.Threading;

namespace ArenaNet.SockNet.Common.Pool
{
    /// <summary>
    /// A pooled object.
    /// </summary>
    /// <typeparam name="T">the type of the object</typeparam>
    public class PooledObject<T> : IDisposable
    {
        /// <summary>
        /// The pool where this object lives.
        /// </summary>
        public ObjectPool<T> Pool { get; internal set; }

        /// <summary>
        /// The value of this pooled object.
        /// </summary>
        public T Value { get; internal set; }

        /// <summary>
        /// External refcount setter.
        /// </summary>
        public class RefCountValue
        {
            private int value = 0;
            public int Value
            {
                get 
                {
                    lock (this)
                    {
                        return value;
                    }
                }
                internal set 
                {
                    lock (this)
                    {
                        this.value = value;
                    }
                }
            }

            public int Increment()
            {
                lock (this)
                {
                    value++;
                    return value;
                }
            }

            public int Decrement()
            {
                lock (this)
                {
                    value--;
                    return value;
                }
            }
        }
        public RefCountValue RefCount
        {
            get;
            internal set;
        }

        /// <summary>
        /// This value is owned by the Pool - not this object
        /// </summary>
        internal bool Pooled { set; get; }

        /// <summary>
        /// Creates a new PooledObject with the given pool and value.
        /// </summary>
        /// <param name="pool"></param>
        /// <param name="value"></param>
        internal PooledObject(ObjectPool<T> pool, T value)
        {
            this.Pool = pool;
            this.Value = value;
            this.RefCount = new RefCountValue();
        }

        /// <summary>
        /// Returns this object to the pool.
        /// </summary>
        public void Return()
        {
            if (Pool == null)
            {
                throw new InvalidOperationException("This pooled object is not attached to a pool - it may have been evicted.");
            }

            Pool.Return(this);
        }

        /// <summary>
        /// Disposes this pooled object and will not be usable again.
        /// </summary>
        public void Dispose()
        {
            if (Pool != null)
            {
                Pool.totalPoolSize--;
                Pool = null;
            }
        }
    }
}
