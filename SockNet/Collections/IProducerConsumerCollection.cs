using System;
using System.Collections;
using System.Collections.Generic;

namespace ArenaNet.SockNet.Collections
{
    /// <summary>
    /// COPIED FROM MONO
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IProducerConsumerCollection<T> : IEnumerable<T>, ICollection, IEnumerable
    {
        bool TryAdd(T item);
        bool TryTake(out T item);
        T[] ToArray();
        void CopyTo(T[] array, int index);
    }
}
