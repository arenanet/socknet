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
using System.Threading;

namespace ArenaNet.SockNet.Common.Collections
{
    /// <summary>
    /// A concurrent hashmap. It uses splicing to lock buckets.
    /// </summary>
    /// <typeparam name="K">the key type</typeparam>
    /// <typeparam name="V">the value type</typeparam>
    public class ConcurrentHashMap<K, V> : IDictionary<K, V>
    {
        #region Defaults
        public static readonly IEqualityComparer<K> DefaultComparer = EqualityComparer<K>.Default;
        public static readonly uint DefaultLookUpTableSize = 128;
        public static readonly uint DefaultLockCount = 16;
        #endregion Defaults

        /// <summary>
        /// The node implementation.
        /// </summary>
        internal class Node
        {
            internal KeyValuePair<K, V> kvp;
            internal Node next;

            internal Node(KeyValuePair<K, V> kvp, Node next = null)
            {
                this.kvp = kvp;
                this.next = next;
            }
        }

        private readonly IEqualityComparer<K> comparer;

        private Node[] buckets;
        private object[] locks;
        private int count;

        /// <summary>
        /// Returns the count (number of keyvaluepairs) in this hash map.
        /// </summary>
        public int Count
        {
            get { return count; }
        }

        /// <summary>
        /// Always returns false.
        /// </summary>
        public bool IsReadOnly
        {
            get { return false; }
        }

        /// <summary>
        /// Create a concurrent hashmap that stripes access to the undelying buckets in the lookup table.
        /// </summary>
        /// <param name="lookupTableSize"></param>
        /// <param name="lockCount"></param>
        public ConcurrentHashMap(IEqualityComparer<K> comparer = null, uint lookupTableSize = 128, uint lockCount = 16)
        {
            if (!IsPowerOfTwo(lookupTableSize))
            {
                throw new ArgumentException("'lookupTableSize' needs to be a power of two.");
            }

            if (!IsPowerOfTwo(lockCount))
            {
                throw new ArgumentException("''lockCount' needs to be a power of two.");
            }

            if (comparer == null)
            {
                this.comparer = EqualityComparer<K>.Default;
            }
            else
            {
                this.comparer = comparer;
            }

            this.buckets = new Node[lookupTableSize];
            this.locks = new object[lockCount];
            for (int i = 0; i < locks.Length; i++)
            {
                locks[i] = new object();
            }

            this.count = 0;
        }

        /// <summary>
        /// Returns all the keys in this hash map.
        /// </summary>
        public ICollection<K> Keys
        {
            get
            {
                List<K> keys = new List<K>();

                for (int i = 0; i < buckets.Length; i++)
                {
                    lock (GetMutexFor(i))
                    {
                        Node currentNode = buckets[i];

                        while (currentNode != null)
                        {
                            keys.Add(currentNode.kvp.Key);
                            currentNode = currentNode.next;
                        }
                    }
                }

                return keys;
            }
        }

        /// <summary>
        /// Returns are the values in this hash map.
        /// </summary>
        public ICollection<V> Values
        {
            get
            {
                List<V> values = new List<V>();

                for (int i = 0; i < buckets.Length; i++)
                {
                    lock (GetMutexFor(i))
                    {
                        Node currentNode = buckets[i];

                        while (currentNode != null)
                        {
                            values.Add(currentNode.kvp.Value);
                            currentNode = currentNode.next;
                        }
                    }
                }

                return values;
            }
        }

        /// <summary>
        /// Arrays accessors for this hash map.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public V this[K key]
        {
            get
            {
                V response;

                if (TryGetValue(key, out response))
                {
                    return response;
                }
                else
                {
                    throw new KeyNotFoundException("No value found for key: " + key);
                }
            }
            set
            {
                if (value == null)
                {
                    Remove(key);
                }
                else
                {
                    Add(key, value);
                }
            }
        }
        
        /// <summary>
        /// Adds a keyvaluepair to this hasm map.
        /// </summary>
        /// <param name="item"></param>
        public void Add(KeyValuePair<K, V> item)
        {
            Add(item.Key, item.Value);
        }

        /// <summary>
        /// Adds a key and a value to this hash map.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void Add(K key, V value)
        {
            int hash = Smear(comparer.GetHashCode(key));
            int index = IndexFor(hash, buckets.Length);

            lock (GetMutexFor(index))
            {
                Node foundNode = buckets[index];

                if (foundNode == null)
                {
                    buckets[index] = new Node(new KeyValuePair<K, V>(key, value));
                    Interlocked.Increment(ref count);
                }
                else
                {
                    Node lastNode = null;

                    do
                    {
                        if (comparer.Equals(foundNode.kvp.Key, key))
                        {
                            break;
                        }

                        lastNode = foundNode;
                        foundNode = foundNode.next;
                    } while (foundNode != null);

                    if (foundNode != null)
                    {
                        foundNode.kvp = new KeyValuePair<K, V>(key, value);
                    }
                    else
                    {
                        lastNode.next = new Node(
                            new KeyValuePair<K, V>(key, value),
                            lastNode.next
                        );
                        Interlocked.Increment(ref count);
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if this hash map contains the given keyvaluepair.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Contains(KeyValuePair<K, V> item)
        {
            V response;

            if (TryGetValue(item.Key, out response))
            {
                return response.Equals(item.Value);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if the given key exists.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool ContainsKey(K key)
        {
            int hash = Smear(comparer.GetHashCode(key));
            int index = IndexFor(hash, buckets.Length);

            lock (GetMutexFor(index))
            {
                Node foundNode = buckets[index];

                if (foundNode == null)
                {
                    return false;
                }
                else
                {
                    do
                    {
                        if (comparer.Equals(foundNode.kvp.Key, key))
                        {
                            break;
                        }

                        foundNode = foundNode.next;
                    } while (foundNode != null);

                    if (foundNode != null)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Removes the given key with the given value from this hash map.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Remove(KeyValuePair<K, V> item)
        {
            return Remove(item.Key, item.Value);
        }

        /// <summary>
        /// Removes the given key from this hash map.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool Remove(K key)
        {
            int hash = Smear(comparer.GetHashCode(key));
            int index = IndexFor(hash, buckets.Length);

            lock (GetMutexFor(index))
            {
                Node foundNode = buckets[index];

                if (foundNode == null)
                {
                    return false;
                }
                else
                {
                    Node lastNode = null;

                    do
                    {
                        if (comparer.Equals(foundNode.kvp.Key, key))
                        {
                            break;
                        }

                        lastNode = foundNode;
                        foundNode = foundNode.next;
                    } while (foundNode != null);

                    if (foundNode != null)
                    {
                        if (lastNode != null)
                        {
                            lastNode.next = foundNode.next;
                        }
                        else
                        {
                            buckets[index] = foundNode.next;
                        }

                        Interlocked.Decrement(ref count);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Removes the given key with the given value from the hash map.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool Remove(K key, V value)
        {
            int hash = Smear(comparer.GetHashCode(key));
            int index = IndexFor(hash, buckets.Length);

            lock (GetMutexFor(index))
            {
                Node foundNode = buckets[index];

                if (foundNode == null)
                {
                    return false;
                }
                else
                {
                    Node lastNode = null;

                    do
                    {
                        if (comparer.Equals(foundNode.kvp.Key, key))
                        {
                            break;
                        }

                        lastNode = foundNode;
                        foundNode = foundNode.next;
                    } while (foundNode != null);

                    if (foundNode != null && foundNode.kvp.Value.Equals(value))
                    {
                        if (lastNode != null)
                        {
                            lastNode.next = foundNode.next;
                        }
                        else
                        {
                            buckets[index] = foundNode.next;
                        }

                        Interlocked.Decrement(ref count);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the value for the given key and returns true if found.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetValue(K key, out V value)
        {
            int hash = Smear(comparer.GetHashCode(key));
            int index = IndexFor(hash, buckets.Length);

            lock (GetMutexFor(index))
            {
                Node foundNode = buckets[index];

                if (foundNode == null)
                {
                    value = default(V);
                    return false;
                }
                else
                {
                    do
                    {
                        if (comparer.Equals(foundNode.kvp.Key, key))
                        {
                            break;
                        }

                        foundNode = foundNode.next;
                    } while (foundNode != null);

                    if (foundNode != null)
                    {
                        value = foundNode.kvp.Value;
                        return true;
                    }
                    else
                    {
                        value = default(V);
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Clears all keybaluepairs from this hash map.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                lock (GetMutexFor(i))
                {
                    while (buckets[i] != null)
                    {
                        buckets[i] = buckets[i].next;
                        Interlocked.Decrement(ref count);
                    }
                }
            }
        }

        /// <summary>
        /// Copies the contents of this hash map into the given array.
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayIndex"></param>
        public void CopyTo(KeyValuePair<K, V>[] array, int arrayIndex)
        {
            if (arrayIndex > array.Length)
            {
                return;
            }

            IEnumerator<KeyValuePair<K, V>> enumerator = GetEnumerator();

            for (int i = arrayIndex; i < array.Length; i++)
            {
                if (enumerator.MoveNext())
                {
                    array[i] = enumerator.Current;
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Gets the enumerator for this hash map.
        /// </summary>
        /// <returns></returns>
        public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
        {
            List<KeyValuePair<K, V>> kvps = new List<KeyValuePair<K, V>>();

            for (int i = 0; i < buckets.Length; i++)
            {
                lock (GetMutexFor(i))
                {
                    Node currentNode = buckets[i];

                    while (currentNode != null)
                    {
                        kvps.Add(currentNode.kvp);
                        currentNode = currentNode.next;
                    }
                }
            }

            return kvps.GetEnumerator();
        }

        /// <summary>
        /// Gets the enumerator for this hash map.
        /// </summary>
        /// <returns></returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Gets the mutex for the given index.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        private object GetMutexFor(int index)
        {
            return locks[IndexFor(index, locks.Length)];
        }

        /// <summary>
        /// Checks if the given number is a power of two.
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        private static bool IsPowerOfTwo(uint x)
        {
            return (x & (x - 1)) == 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        private static int IndexFor(int hash, int length)
        {
            return Abs(hash) & (length - 1);
        }

        /// <summary>
        /// Applies a supplemental hash function to a given hashCode, which
        /// defends against poor quality hash functions.  This is critical
        /// because HashMap uses power-of-two length hash tables, that
        /// otherwise encounter collisions for hashCodes that do not differ
        /// in lower bits. Note: Null keys always map to hash 0, thus index 0.
        /// </summary>
        /// <param name="hashCode"></param>
        /// <returns></returns>
        private static int Smear(int hashCode)
        {
            hashCode ^= (hashCode >> 20) ^ (hashCode >> 12);
            return hashCode ^ (hashCode >> 7) ^ (hashCode >> 4);
        }

        /// <summary>
        /// Gets the absolute value of the given integer.
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        private static int Abs(int x)
        {
            return (x + (x >> 31)) ^ (x >> 31);
        }
    }
}
