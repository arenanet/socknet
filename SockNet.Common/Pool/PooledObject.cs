using System;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Common.Pool
{
    /// <summary>
    /// A pooled object.
    /// </summary>
    /// <typeparam name="T">the type of the object</typeparam>
    public class PooledObject<T>
    {
        /// <summary>
        /// The pool where this object lives.
        /// </summary>
        public ObjectPool<T> Pool { get; private set; }

        /// <summary>
        /// The value of this pooled object.
        /// </summary>
        public T Value { get; internal set; }

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
        }

        /// <summary>
        /// Returns this object to the pool.
        /// </summary>
        public void Return()
        {
            Pool.Return(this);
        }
    }
}
