using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace ArenaNet.SockNet.Collections
{
    /// <summary>
    /// TAKEN FROM MICROSOFT'S .NET IMPLEMENTATION
    /// </summary>
    #region ConditionalWeakTable
    [System.Runtime.InteropServices.ComVisible(false)]
    public sealed class ConditionalWeakTable<TKey, TValue>
        where TKey : class
        where TValue : class
    {
 
        #region Constructors
        [System.Security.SecuritySafeCritical]
        public ConditionalWeakTable()
        {
            _buckets = new int[0];
            _entries = new Entry[0];
            _freeList = -1;
            _lock = new Object();
 
            Resize();   // Resize at once (so won't need "if initialized" checks all over)
        }
        #endregion
 
        #region Public Members
        //--------------------------------------------------------------------------------------------
        // key:   key of the value to find. Cannot be null.
        // value: if the key is found, contains the value associated with the key upon method return.
        //        if the key is not found, contains default(TValue).
        //
        // Method returns "true" if key was found, "false" otherwise.
        //
        // Note: The key may get garbaged collected during the TryGetValue operation. If so, TryGetValue
        // may at its discretion, return "false" and set "value" to the default (as if the key was not present.)
        //--------------------------------------------------------------------------------------------
        [System.Security.SecuritySafeCritical]
        public bool TryGetValue(TKey key, out TValue value)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            lock(_lock)
            {
                VerifyIntegrity();
                return TryGetValueWorker(key, out value);
            }
        }
 
        //--------------------------------------------------------------------------------------------
        // key: key to add. May not be null.
        // value: value to associate with key.
        //
        // If the key is already entered into the dictionary, this method throws an exception.
        //
        // Note: The key may get garbage collected during the Add() operation. If so, Add()
        // has the right to consider any prior entries successfully removed and add a new entry without
        // throwing an exception.
        //--------------------------------------------------------------------------------------------
        [System.Security.SecuritySafeCritical]
        public void Add(TKey key, TValue value)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
 
            lock(_lock)
            {
                VerifyIntegrity();
                _invalid = true;
 
                int entryIndex = FindEntry(key);
                if (entryIndex != -1)
                {
                    _invalid = false;
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_AddingDuplicate);
                }
 
                CreateEntry(key, value);
                _invalid = false;
            }
 
        }
 
        //--------------------------------------------------------------------------------------------
        // key: key to remove. May not be null.
        //
        // Returns true if the key is found and removed. Returns false if the key was not in the dictionary.
        //
        // Note: The key may get garbage collected during the Remove() operation. If so,
        // Remove() will not fail or throw, however, the return value can be either true or false
        // depending on who wins the ----.
        //--------------------------------------------------------------------------------------------
        [System.Security.SecuritySafeCritical]
        public bool Remove(TKey key)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
 
            lock(_lock)
            {
                VerifyIntegrity();
                _invalid = true;
 
                int hashCode = RuntimeHelpers.GetHashCode(key) & Int32.MaxValue;
                int bucket = hashCode % _buckets.Length;
                int last = -1;
                for (int entriesIndex = _buckets[bucket]; entriesIndex != -1; entriesIndex = _entries[entriesIndex].next)
                {
                    if (_entries[entriesIndex].hashCode == hashCode && _entries[entriesIndex].depHnd.GetPrimary() == key)
                    {
                        if (last == -1)
                        {
                            _buckets[bucket] = _entries[entriesIndex].next;
                        }
                        else
                        {
                            _entries[last].next = _entries[entriesIndex].next;
                        }
    
                        _entries[entriesIndex].depHnd.Free();
                        _entries[entriesIndex].next = _freeList;
 
                        _freeList = entriesIndex;
 
                        _invalid = false;
                        return true;
                        
                    }
                    last = entriesIndex;
                }
                _invalid = false;
                return false;
            }
        }
 
 
        //--------------------------------------------------------------------------------------------
        // key:                 key of the value to find. Cannot be null.
        // createValueCallback: callback that creates value for key. Cannot be null.
        //
        // Atomically tests if key exists in table. If so, returns corresponding value. If not,
        // invokes createValueCallback() passing it the key. The returned value is bound to the key in the table
        // and returned as the result of GetValue().
        //
        // If multiple threads ---- to initialize the same key, the table may invoke createValueCallback
        // multiple times with the same key. Exactly one of these calls will "win the ----" and the returned
        // value of that call will be the one added to the table and returned by all the racing GetValue() calls.
        // 
        // This rule permits the table to invoke createValueCallback outside the internal table lock
        // to prevent deadlocks.
        //--------------------------------------------------------------------------------------------
        [System.Security.SecuritySafeCritical]
        public TValue GetValue(TKey key, CreateValueCallback createValueCallback)
        {
            // Our call to TryGetValue() validates key so no need for us to.
            //
            //  if (key == null)
            //  {
            //      ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            //  }
 
            if (createValueCallback == null)
            {
                throw new ArgumentNullException("createValueCallback");
            }
 
            TValue existingValue;
            if (TryGetValue(key, out existingValue))
            {
                return existingValue;
            }
 
            // If we got here, the key is not currently in table. Invoke the callback (outside the lock)
            // to generate the new value for the key. 
            TValue newValue = createValueCallback(key);
 
            lock(_lock)
            {
                VerifyIntegrity();
                _invalid = true;
                
                // Now that we've retaken the lock, must recheck in case we lost a ---- to add the key.
                if (TryGetValueWorker(key, out existingValue))
                {
                    _invalid = false;
                    return existingValue;
                }
                else
                {
                    // Verified in-lock that we won the ---- to add the key. Add it now.
                    CreateEntry(key, newValue);
                    _invalid = false;
                    return newValue;
                }
            }
        }
 
        //--------------------------------------------------------------------------------------------
        // key:                 key of the value to find. Cannot be null.
        //
        // Helper method to call GetValue without passing a creation delegate.  Uses Activator.CreateInstance
        // to create new instances as needed.  If TValue does not have a default constructor, this will
        // throw.
        //--------------------------------------------------------------------------------------------
        public TValue GetOrCreateValue(TKey key)
        {
            return GetValue(key, k => Activator.CreateInstance<TValue>());
        }
 
        public delegate TValue CreateValueCallback(TKey key);
        
        #endregion
 
        #region internal members
        
        //--------------------------------------------------------------------------------------------
        // Find a key that equals (value equality) with the given key - don't use in perf critical path
        // Note that it calls out to Object.Equals which may calls the override version of Equals
        // and that may take locks and leads to deadlock
        // Currently it is only used by WinRT event code and you should only use this function
        // if you know for sure that either you won't run into dead locks or you need to live with the
        // possiblity
        //--------------------------------------------------------------------------------------------
        [System.Security.SecuritySafeCritical]
        internal TKey FindEquivalentKeyUnsafe(TKey key, out TValue value)
        {
            lock (_lock)
            {
                for (int bucket = 0; bucket < _buckets.Length; ++bucket)
                {
                    for (int entriesIndex = _buckets[bucket]; entriesIndex != -1; entriesIndex = _entries[entriesIndex].next)
                    {
                        object thisKey, thisValue;
                        _entries[entriesIndex].depHnd.GetPrimaryAndSecondary(out thisKey, out thisValue);
                        if (Object.Equals(thisKey, key))
                        {
                            value = (TValue) thisValue;
                            return (TKey) thisKey;
                        }
                    }
                }
            }
            
            value = default(TValue);
            return null;        
        }
        
        //--------------------------------------------------------------------------------------------
        // Returns a collection of keys - don't use in perf critical path
        //--------------------------------------------------------------------------------------------
        internal ICollection<TKey> Keys
        {
            [System.Security.SecuritySafeCritical]
            get
            {
                List<TKey> list = new List<TKey>();
                lock (_lock)
                {
                    for (int bucket = 0; bucket < _buckets.Length; ++bucket)
                    {
                        for (int entriesIndex = _buckets[bucket]; entriesIndex != -1; entriesIndex = _entries[entriesIndex].next)
                        {
                            TKey thisKey = (TKey) _entries[entriesIndex].depHnd.GetPrimary();
                            if (thisKey != null)
                            {
                                list.Add(thisKey);
                            }
                        }
                    }
                }
                
                return list;        
            }
        }
 
        //--------------------------------------------------------------------------------------------
        // Returns a collection of values - don't use in perf critical path
        //--------------------------------------------------------------------------------------------
        internal ICollection<TValue> Values
        {
            [System.Security.SecuritySafeCritical]
            get
            {
                List<TValue> list = new List<TValue>();
                lock (_lock)
                {
                    for (int bucket = 0; bucket < _buckets.Length; ++bucket)
                    {
                        for (int entriesIndex = _buckets[bucket]; entriesIndex != -1; entriesIndex = _entries[entriesIndex].next)
                        {
                            Object primary = null;
                            Object secondary = null;
 
                            _entries[entriesIndex].depHnd.GetPrimaryAndSecondary(out primary, out secondary);
                            
                            // Now that we've secured a strong reference to the secondary, must check the primary again
                            // to ensure it didn't expire (otherwise, we open a ---- where TryGetValue misreports an
                            // expired key as a live key with a null value.)
                            if (primary != null)
                            {
                                list.Add((TValue)secondary);
                            }
                        }
                    }
                }
                
                return list;        
            }
        }
        
        //--------------------------------------------------------------------------------------------
        // Clear all the key/value pairs
        //--------------------------------------------------------------------------------------------
        [System.Security.SecuritySafeCritical]
        internal void Clear()
        {
            lock (_lock)
            {
                // Clear the buckets
                for (int bucketIndex = 0; bucketIndex < _buckets.Length; bucketIndex++)
                {
                    _buckets[bucketIndex] = -1;
                }
 
                // Clear the entries and link them backwards together as part of free list
                int entriesIndex;
                for (entriesIndex = 0; entriesIndex < _entries.Length; entriesIndex++)
                {
                    if (_entries[entriesIndex].depHnd.IsAllocated)
                    {
                        _entries[entriesIndex].depHnd.Free();
                    }
 
                    // Link back wards as free list
                    _entries[entriesIndex].next = entriesIndex - 1;
                }
 
                _freeList = entriesIndex - 1;
            }            
        }
 
        #endregion
        
        #region Private Members
        [System.Security.SecurityCritical]
        //----------------------------------------------------------------------------------------
        // Worker for finding a key/value pair
        //
        // Preconditions:
        //     Must hold _lock.
        //     Key already validated as non-null
        //----------------------------------------------------------------------------------------
        private bool TryGetValueWorker(TKey key, out TValue value)
        {
            int entryIndex = FindEntry(key);
            if (entryIndex != -1)
            {
                Object primary = null;
                Object secondary = null;
                _entries[entryIndex].depHnd.GetPrimaryAndSecondary(out primary, out secondary);
                // Now that we've secured a strong reference to the secondary, must check the primary again
                // to ensure it didn't expire (otherwise, we open a ---- where TryGetValue misreports an
                // expired key as a live key with a null value.)
                if (primary != null)
                {
                    value = (TValue)secondary;
                    return true;
                }
            }
 
            value = default(TValue);
            return false;
        }
 
        //----------------------------------------------------------------------------------------
        // Worker for adding a new key/value pair.
        //
        // Preconditions:
        //     Must hold _lock.
        //     Key already validated as non-null and not already in table.
        //----------------------------------------------------------------------------------------
        [System.Security.SecurityCritical]
        private void CreateEntry(TKey key, TValue value)
        {
            if (_freeList == -1)
            {
                Resize();
            }
 
            int hashCode = RuntimeHelpers.GetHashCode(key) & Int32.MaxValue;
            int bucket = hashCode % _buckets.Length;
 
            int newEntry = _freeList;
            _freeList = _entries[newEntry].next;
 
            _entries[newEntry].hashCode = hashCode;
            _entries[newEntry].depHnd = new DependentHandle(key, value);
            _entries[newEntry].next = _buckets[bucket];
 
            _buckets[bucket] = newEntry;
 
        }
 
        //----------------------------------------------------------------------------------------
        // This does two things: resize and scrub expired keys off bucket lists.
        //
        // Precondition:
        //      Must hold _lock.
        //
        // Postcondition:
        //      _freeList is non-empty on exit.
        //----------------------------------------------------------------------------------------
        [System.Security.SecurityCritical]
        private void Resize()
        {
            // Start by assuming we won't resize.
            int newSize = _buckets.Length;
 
            // If any expired keys exist, we won't resize.
            bool hasExpiredEntries = false;
            int entriesIndex;
            for (entriesIndex = 0; entriesIndex < _entries.Length; entriesIndex++)
            {
                if ( _entries[entriesIndex].depHnd.IsAllocated && _entries[entriesIndex].depHnd.GetPrimary() == null)
                {
                    hasExpiredEntries = true;
                    break;
                }
            }
 
            if (!hasExpiredEntries)
            {
                newSize = ArenaNet.SockNet.Collections.HashHelpers.GetPrime(_buckets.Length == 0 ? _initialCapacity + 1 : _buckets.Length * 2);
            }
 
 
            // Reallocate both buckets and entries and rebuild the bucket and freelists from scratch.
            // This serves both to scrub entries with expired keys and to put the new entries in the proper bucket.
            int newFreeList = -1;
            int[] newBuckets = new int[newSize];
            for (int bucketIndex = 0; bucketIndex < newSize; bucketIndex++)
            {
                newBuckets[bucketIndex] = -1;
            }
            Entry[] newEntries = new Entry[newSize];
 
            // Migrate existing entries to the new table.
            for (entriesIndex = 0; entriesIndex < _entries.Length; entriesIndex++)
            {
                DependentHandle depHnd = _entries[entriesIndex].depHnd;
                if (depHnd.IsAllocated && depHnd.GetPrimary() != null)
                {
                    // Entry is used and has not expired. Link it into the appropriate bucket list.
                    int bucket = _entries[entriesIndex].hashCode % newSize;
                    newEntries[entriesIndex].depHnd = depHnd;
                    newEntries[entriesIndex].hashCode = _entries[entriesIndex].hashCode;
                    newEntries[entriesIndex].next = newBuckets[bucket];
                    newBuckets[bucket] = entriesIndex;
                }
                else
                {
                    // Entry has either expired or was on the freelist to begin with. Either way
                    // insert it on the new freelist.
                    _entries[entriesIndex].depHnd.Free();
                    newEntries[entriesIndex].depHnd = new DependentHandle();
                    newEntries[entriesIndex].next = newFreeList;
                    newFreeList = entriesIndex;
                }
            }
 
            // Add remaining entries to freelist.
            while (entriesIndex != newEntries.Length)
            {
                newEntries[entriesIndex].depHnd = new DependentHandle();
                newEntries[entriesIndex].next = newFreeList;
                newFreeList = entriesIndex;
                entriesIndex++;
            }
 
            _buckets = newBuckets;
            _entries = newEntries;
            _freeList = newFreeList;
        }
 
        //----------------------------------------------------------------------------------------
        // Returns -1 if not found (if key expires during FindEntry, this can be treated as "not found.")
        //
        // Preconditions:
        //     Must hold _lock.
        //     Key already validated as non-null.
        //----------------------------------------------------------------------------------------
        [System.Security.SecurityCritical]
        private int FindEntry(TKey key)
        {
            int hashCode = RuntimeHelpers.GetHashCode(key) & Int32.MaxValue;
            for (int entriesIndex = _buckets[hashCode % _buckets.Length]; entriesIndex != -1; entriesIndex = _entries[entriesIndex].next)
            {
                if (_entries[entriesIndex].hashCode == hashCode && _entries[entriesIndex].depHnd.GetPrimary() == key)
                {
                    return entriesIndex;
                }
            }
            return -1;
        }
 
        //----------------------------------------------------------------------------------------
        // Precondition:
        //     Must hold _lock.
        //----------------------------------------------------------------------------------------
        private void VerifyIntegrity()
        {
            if (_invalid)
            {
                throw new InvalidOperationException("CollectionCorrupted");
            }
        }
 
        //----------------------------------------------------------------------------------------
        // Finalizer.
        //----------------------------------------------------------------------------------------
        [System.Security.SecuritySafeCritical]
        ~ConditionalWeakTable()
        {
 
            // We're just freeing per-appdomain unmanaged handles here. If we're already shutting down the AD,
            // don't bother.
            //
            // (Despite its name, Environment.HasShutdownStart also returns true if the current AD is finalizing.)
            if (Environment.HasShutdownStarted)
            {
                return;
            }
 
            if (_lock != null)
            {
                lock(_lock)
                {
                    if (_invalid)
                    {
                        return;
                    }
                    Entry[] entries = _entries;
 
                    // Make sure anyone sneaking into the table post-resurrection
                    // gets booted before they can damage the native handle table.
                    _invalid = true;
                    _entries = null;
                    _buckets = null;
 
                    for (int entriesIndex = 0; entriesIndex < entries.Length; entriesIndex++)
                    {
                        entries[entriesIndex].depHnd.Free();
                    }
                }
            }
        }
        #endregion
 
        #region Private Data Members
        //--------------------------------------------------------------------------------------------
        // Entry can be in one of three states:
        //
        //    - Linked into the freeList (_freeList points to first entry)
        //         depHnd.IsAllocated == false
        //         hashCode == <dontcare>
        //         next links to next Entry on freelist)
        //
        //    - Used with live key (linked into a bucket list where _buckets[hashCode % _buckets.Length] points to first entry)
        //         depHnd.IsAllocated == true, depHnd.GetPrimary() != null
        //         hashCode == RuntimeHelpers.GetHashCode(depHnd.GetPrimary()) & Int32.MaxValue
        //         next links to next Entry in bucket. 
        //                          
        //    - Used with dead key (linked into a bucket list where _buckets[hashCode % _buckets.Length] points to first entry)
        //         depHnd.IsAllocated == true, depHnd.GetPrimary() == null
        //         hashCode == <notcare> 
        //         next links to next Entry in bucket. 
        //
        // The only difference between "used with live key" and "used with dead key" is that
        // depHnd.GetPrimary() returns null. The transition from "used with live key" to "used with dead key"
        // happens asynchronously as a result of normal garbage collection. The dictionary itself
        // receives no notification when this happens.
        //
        // When the dictionary grows the _entries table, it scours it for expired keys and puts those
        // entries back on the freelist.
        //--------------------------------------------------------------------------------------------
        private struct Entry
        {
            public DependentHandle depHnd;      // Holds key and value using a weak reference for the key and a strong reference
                                                // for the value that is traversed only if the key is reachable without going through the value.
            public int             hashCode;    // Cached copy of key's hashcode
            public int             next;        // Index of next entry, -1 if last
        }
 
        private int[]     _buckets;             // _buckets[hashcode & _buckets.Length] contains index of first entry in bucket (-1 if empty)
        private Entry[]   _entries;              
        private int       _freeList;            // -1 = empty, else index of first unused Entry
        private const int _initialCapacity = 5;
        private readonly Object _lock;          // this could be a ReaderWriterLock but CoreCLR does not support RWLocks.
        private bool      _invalid;             // flag detects if OOM or other background exception threw us out of the lock.
        #endregion
    }
    #endregion
 
 
 
 
    #region DependentHandle
    //=========================================================================================
    // This struct collects all operations on native DependentHandles. The DependentHandle
    // merely wraps an IntPtr so this struct serves mainly as a "managed typedef."
    //
    // DependentHandles exist in one of two states:
    //
    //    IsAllocated == false
    //        No actual handle is allocated underneath. Illegal to call GetPrimary
    //        or GetPrimaryAndSecondary(). Ok to call Free().
    //
    //        Initializing a DependentHandle using the nullary ctor creates a DependentHandle
    //        that's in the !IsAllocated state.
    //        (! Right now, we get this guarantee for free because (IntPtr)0 == NULL unmanaged handle.
    //         ! If that assertion ever becomes false, we'll have to add an _isAllocated field
    //         ! to compensate.)
    //        
    //
    //    IsAllocated == true
    //        There's a handle allocated underneath. You must call Free() on this eventually
    //        or you cause a native handle table leak.
    //
    // This struct intentionally does no self-synchronization. It's up to the caller to
    // to use DependentHandles in a thread-safe way.
    //=========================================================================================
    [ComVisible(false)]
    struct DependentHandle
    {
        #region Constructors
        #if FEATURE_CORECLR
        [System.Security.SecuritySafeCritical] // auto-generated
        #else
        [System.Security.SecurityCritical]
        #endif
        public DependentHandle(Object primary, Object secondary)
        {
            IntPtr handle = (IntPtr)0;
            nInitialize(primary, secondary, out handle);
            // no need to check for null result: nInitialize expected to throw OOM.
            _handle = handle;
        }
        #endregion
 
        #region Public Members
        public bool IsAllocated
        {
            get
            {
                return _handle != (IntPtr)0;
            }
        }
 
        // Getting the secondary object is more expensive than getting the first so
        // we provide a separate primary-only accessor for those times we only want the
        // primary.
        #if FEATURE_CORECLR
        [System.Security.SecuritySafeCritical] // auto-generated
        #else
        [System.Security.SecurityCritical]
        #endif
        public Object GetPrimary()
        {
            Object primary;
            nGetPrimary(_handle, out primary);
            return primary;
        }
 
        #if FEATURE_CORECLR
        [System.Security.SecuritySafeCritical] // auto-generated
        #else
        [System.Security.SecurityCritical]
        #endif
        public void GetPrimaryAndSecondary(out Object primary, out Object secondary)
        {
            nGetPrimaryAndSecondary(_handle, out primary, out secondary);
        }
 
        //----------------------------------------------------------------------
        // Forces dependentHandle back to non-allocated state (if not already there)
        // and frees the handle if needed.
        //----------------------------------------------------------------------
        [System.Security.SecurityCritical]
        public void Free()
        {
            if (_handle != (IntPtr)0)
            {
                IntPtr handle = _handle;
                _handle = (IntPtr)0;
                 nFree(handle);
            }
        }
        #endregion
 
        #region Private Members
        [System.Security.SecurityCritical]
        [ResourceExposure(ResourceScope.AppDomain)]
        private static extern void nInitialize(Object primary, Object secondary, out IntPtr dependentHandle);
 
        [System.Security.SecurityCritical]
        [ResourceExposure(ResourceScope.None)]
        private static extern void nGetPrimary(IntPtr dependentHandle, out Object primary);
 
        [System.Security.SecurityCritical]
        [ResourceExposure(ResourceScope.None)]
        private static extern void nGetPrimaryAndSecondary(IntPtr dependentHandle, out Object primary, out Object secondary);
 
        [System.Security.SecurityCritical]
        [ResourceExposure(ResourceScope.None)]
        private static extern void nFree(IntPtr dependentHandle);
        #endregion
 
        #region Private Data Member
        private IntPtr _handle;
        #endregion
 
    } // struct DependentHandle
    #endregion

    internal static class ThrowHelper
    {
        internal static void ThrowArgumentOutOfRangeException()
        {
            throw new ArgumentOutOfRangeException(
        GetArgumentName(ExceptionArgument.index), GetResourceName(ExceptionResource.ArgumentOutOfRange_Index));
        }

        internal static void ThrowWrongKeyTypeArgumentException(object key, Type targetType)
        {
            throw new ArgumentException("key");
        }

        internal static void ThrowWrongValueTypeArgumentException(object value, Type targetType)
        {
            throw new ArgumentException("value");
        }

        internal static void ThrowKeyNotFoundException()
        {
            throw new System.Collections.Generic.KeyNotFoundException();
        }

        internal static void ThrowArgumentException(ExceptionResource resource)
        {
            throw new ArgumentException(GetResourceName(resource));
        }

        internal static void ThrowArgumentException(ExceptionResource resource, ExceptionArgument argument)
        {
            throw new ArgumentException(GetResourceName(resource), GetArgumentName(argument));
        }

        internal static void ThrowArgumentNullException(ExceptionArgument argument)
        {
            throw new ArgumentNullException(GetArgumentName(argument));
        }

        internal static void ThrowArgumentOutOfRangeException(ExceptionArgument argument)
        {
            throw new ArgumentOutOfRangeException(GetArgumentName(argument));
        }

        internal static void ThrowArgumentOutOfRangeException(ExceptionArgument argument, ExceptionResource resource)
        {
            throw new ArgumentOutOfRangeException(GetArgumentName(argument), GetResourceName(resource));
        }

        internal static void ThrowInvalidOperationException(ExceptionResource resource)
        {
            throw new InvalidOperationException(GetResourceName(resource));
        }

        internal static void ThrowSecurityException(ExceptionResource resource)
        {
            throw new System.Security.SecurityException(GetResourceName(resource));
        }

        internal static void ThrowNotSupportedException(ExceptionResource resource)
        {
            throw new NotSupportedException(GetResourceName(resource));
        }

        internal static void ThrowUnauthorizedAccessException(ExceptionResource resource)
        {
            throw new UnauthorizedAccessException(GetResourceName(resource));
        }

        internal static void ThrowObjectDisposedException(string objectName, ExceptionResource resource)
        {
            throw new ObjectDisposedException(objectName, GetResourceName(resource));
        }

        // Allow nulls for reference types and Nullable<U>, but not for value types.
        internal static void IfNullAndNullsAreIllegalThenThrow<T>(object value, ExceptionArgument argName)
        {
            // Note that default(T) is not equal to null for value types except when T is Nullable<U>. 
            if (value == null && !(default(T) == null))
                ThrowHelper.ThrowArgumentNullException(argName);
        }

        //
        // This function will convert an ExceptionArgument enum value to the argument name string.
        //
        internal static string GetArgumentName(ExceptionArgument argument)
        {
            string argumentName = null;

            switch (argument)
            {
                case ExceptionArgument.array:
                    argumentName = "array";
                    break;

                case ExceptionArgument.arrayIndex:
                    argumentName = "arrayIndex";
                    break;

                case ExceptionArgument.capacity:
                    argumentName = "capacity";
                    break;

                case ExceptionArgument.collection:
                    argumentName = "collection";
                    break;

                case ExceptionArgument.list:
                    argumentName = "list";
                    break;

                case ExceptionArgument.converter:
                    argumentName = "converter";
                    break;

                case ExceptionArgument.count:
                    argumentName = "count";
                    break;

                case ExceptionArgument.dictionary:
                    argumentName = "dictionary";
                    break;

                case ExceptionArgument.dictionaryCreationThreshold:
                    argumentName = "dictionaryCreationThreshold";
                    break;

                case ExceptionArgument.index:
                    argumentName = "index";
                    break;

                case ExceptionArgument.info:
                    argumentName = "info";
                    break;

                case ExceptionArgument.key:
                    argumentName = "key";
                    break;

                case ExceptionArgument.match:
                    argumentName = "match";
                    break;

                case ExceptionArgument.obj:
                    argumentName = "obj";
                    break;

                case ExceptionArgument.queue:
                    argumentName = "queue";
                    break;

                case ExceptionArgument.stack:
                    argumentName = "stack";
                    break;

                case ExceptionArgument.startIndex:
                    argumentName = "startIndex";
                    break;

                case ExceptionArgument.value:
                    argumentName = "value";
                    break;

                case ExceptionArgument.name:
                    argumentName = "name";
                    break;

                case ExceptionArgument.mode:
                    argumentName = "mode";
                    break;

                case ExceptionArgument.item:
                    argumentName = "item";
                    break;

                case ExceptionArgument.options:
                    argumentName = "options";
                    break;

                case ExceptionArgument.view:
                    argumentName = "view";
                    break;

                default:
                    throw new InvalidOperationException("The enum value is not defined, please checked ExceptionArgumentName Enum.");
            }

            return argumentName;
        }

        //
        // This function will convert an ExceptionResource enum value to the resource string.
        //
        internal static string GetResourceName(ExceptionResource resource)
        {
            string resourceName = null;

            switch (resource)
            {
                case ExceptionResource.Argument_ImplementIComparable:
                    resourceName = "Argument_ImplementIComparable";
                    break;

                case ExceptionResource.Argument_AddingDuplicate:
                    resourceName = "Argument_AddingDuplicate";
                    break;

                case ExceptionResource.ArgumentOutOfRange_BiggerThanCollection:
                    resourceName = "ArgumentOutOfRange_BiggerThanCollection";
                    break;

                case ExceptionResource.ArgumentOutOfRange_Count:
                    resourceName = "ArgumentOutOfRange_Count";
                    break;

                case ExceptionResource.ArgumentOutOfRange_Index:
                    resourceName = "ArgumentOutOfRange_Index";
                    break;

                case ExceptionResource.ArgumentOutOfRange_InvalidThreshold:
                    resourceName = "ArgumentOutOfRange_InvalidThreshold";
                    break;

                case ExceptionResource.ArgumentOutOfRange_ListInsert:
                    resourceName = "ArgumentOutOfRange_ListInsert";
                    break;

                case ExceptionResource.ArgumentOutOfRange_NeedNonNegNum:
                    resourceName = "ArgumentOutOfRange_NeedNonNegNum";
                    break;

                case ExceptionResource.ArgumentOutOfRange_SmallCapacity:
                    resourceName = "ArgumentOutOfRange_SmallCapacity";
                    break;

                case ExceptionResource.Arg_ArrayPlusOffTooSmall:
                    resourceName = "Arg_ArrayPlusOffTooSmall";
                    break;

                case ExceptionResource.Arg_RankMultiDimNotSupported:
                    resourceName = "Arg_RankMultiDimNotSupported";
                    break;

                case ExceptionResource.Arg_NonZeroLowerBound:
                    resourceName = "Arg_NonZeroLowerBound";
                    break;

                case ExceptionResource.Argument_InvalidArrayType:
                    resourceName = "Argument_InvalidArrayType";
                    break;

                case ExceptionResource.Argument_InvalidOffLen:
                    resourceName = "Argument_InvalidOffLen";
                    break;

                case ExceptionResource.Argument_ItemNotExist:
                    resourceName = "Argument_ItemNotExist";
                    break;

                case ExceptionResource.InvalidOperation_CannotRemoveFromStackOrQueue:
                    resourceName = "InvalidOperation_CannotRemoveFromStackOrQueue";
                    break;

                case ExceptionResource.InvalidOperation_EmptyQueue:
                    resourceName = "InvalidOperation_EmptyQueue";
                    break;

                case ExceptionResource.InvalidOperation_EnumOpCantHappen:
                    resourceName = "InvalidOperation_EnumOpCantHappen";
                    break;

                case ExceptionResource.InvalidOperation_EnumFailedVersion:
                    resourceName = "InvalidOperation_EnumFailedVersion";
                    break;

                case ExceptionResource.InvalidOperation_EmptyStack:
                    resourceName = "InvalidOperation_EmptyStack";
                    break;

                case ExceptionResource.InvalidOperation_EnumNotStarted:
                    resourceName = "InvalidOperation_EnumNotStarted";
                    break;

                case ExceptionResource.InvalidOperation_EnumEnded:
                    resourceName = "InvalidOperation_EnumEnded";
                    break;

                case ExceptionResource.NotSupported_KeyCollectionSet:
                    resourceName = "NotSupported_KeyCollectionSet";
                    break;

                case ExceptionResource.NotSupported_ReadOnlyCollection:
                    resourceName = "NotSupported_ReadOnlyCollection";
                    break;

                case ExceptionResource.NotSupported_ValueCollectionSet:
                    resourceName = "NotSupported_ValueCollectionSet";
                    break;


                case ExceptionResource.NotSupported_SortedListNestedWrite:
                    resourceName = "NotSupported_SortedListNestedWrite";
                    break;


                case ExceptionResource.Serialization_InvalidOnDeser:
                    resourceName = "Serialization_InvalidOnDeser";
                    break;

                case ExceptionResource.Serialization_MissingKeys:
                    resourceName = "Serialization_MissingKeys";
                    break;

                case ExceptionResource.Serialization_NullKey:
                    resourceName = "Serialization_NullKey";
                    break;

                case ExceptionResource.Argument_InvalidType:
                    resourceName = "Argument_InvalidType";
                    break;

                case ExceptionResource.Argument_InvalidArgumentForComparison:
                    resourceName = "Argument_InvalidArgumentForComparison";
                    break;

                case ExceptionResource.InvalidOperation_NoValue:
                    resourceName = "InvalidOperation_NoValue";
                    break;

                case ExceptionResource.InvalidOperation_RegRemoveSubKey:
                    resourceName = "InvalidOperation_RegRemoveSubKey";
                    break;

                case ExceptionResource.Arg_RegSubKeyAbsent:
                    resourceName = "Arg_RegSubKeyAbsent";
                    break;

                case ExceptionResource.Arg_RegSubKeyValueAbsent:
                    resourceName = "Arg_RegSubKeyValueAbsent";
                    break;

                case ExceptionResource.Arg_RegKeyDelHive:
                    resourceName = "Arg_RegKeyDelHive";
                    break;

                case ExceptionResource.Security_RegistryPermission:
                    resourceName = "Security_RegistryPermission";
                    break;

                case ExceptionResource.Arg_RegSetStrArrNull:
                    resourceName = "Arg_RegSetStrArrNull";
                    break;

                case ExceptionResource.Arg_RegSetMismatchedKind:
                    resourceName = "Arg_RegSetMismatchedKind";
                    break;

                case ExceptionResource.UnauthorizedAccess_RegistryNoWrite:
                    resourceName = "UnauthorizedAccess_RegistryNoWrite";
                    break;

                case ExceptionResource.ObjectDisposed_RegKeyClosed:
                    resourceName = "ObjectDisposed_RegKeyClosed";
                    break;

                case ExceptionResource.Arg_RegKeyStrLenBug:
                    resourceName = "Arg_RegKeyStrLenBug";
                    break;

                case ExceptionResource.Argument_InvalidRegistryKeyPermissionCheck:
                    resourceName = "Argument_InvalidRegistryKeyPermissionCheck";
                    break;

                case ExceptionResource.NotSupported_InComparableType:
                    resourceName = "NotSupported_InComparableType";
                    break;

                case ExceptionResource.Argument_InvalidRegistryOptionsCheck:
                    resourceName = "Argument_InvalidRegistryOptionsCheck";
                    break;

                case ExceptionResource.Argument_InvalidRegistryViewCheck:
                    resourceName = "Argument_InvalidRegistryViewCheck";
                    break;

                default:
                    throw new InvalidOperationException("The enum value is not defined, please checked ExceptionArgumentName Enum.");
            }

            return resourceName;
        }

    }

    //
    // The convention for this enum is using the argument name as the enum name
    // 
    internal enum ExceptionArgument
    {
        obj,
        dictionary,
        dictionaryCreationThreshold,
        array,
        info,
        key,
        collection,
        list,
        match,
        converter,
        queue,
        stack,
        capacity,
        index,
        startIndex,
        value,
        count,
        arrayIndex,
        name,
        mode,
        item,
        options,
        view,
    }

    //
    // The convention for this enum is using the resource name as the enum name
    // 
    internal enum ExceptionResource
    {
        Argument_ImplementIComparable,
        Argument_InvalidType,
        Argument_InvalidArgumentForComparison,
        Argument_InvalidRegistryKeyPermissionCheck,
        ArgumentOutOfRange_NeedNonNegNum,

        Arg_ArrayPlusOffTooSmall,
        Arg_NonZeroLowerBound,
        Arg_RankMultiDimNotSupported,
        Arg_RegKeyDelHive,
        Arg_RegKeyStrLenBug,
        Arg_RegSetStrArrNull,
        Arg_RegSetMismatchedKind,
        Arg_RegSubKeyAbsent,
        Arg_RegSubKeyValueAbsent,

        Argument_AddingDuplicate,
        Serialization_InvalidOnDeser,
        Serialization_MissingKeys,
        Serialization_NullKey,
        Argument_InvalidArrayType,
        NotSupported_KeyCollectionSet,
        NotSupported_ValueCollectionSet,
        ArgumentOutOfRange_SmallCapacity,
        ArgumentOutOfRange_Index,
        Argument_InvalidOffLen,
        Argument_ItemNotExist,
        ArgumentOutOfRange_Count,
        ArgumentOutOfRange_InvalidThreshold,
        ArgumentOutOfRange_ListInsert,
        NotSupported_ReadOnlyCollection,
        InvalidOperation_CannotRemoveFromStackOrQueue,
        InvalidOperation_EmptyQueue,
        InvalidOperation_EnumOpCantHappen,
        InvalidOperation_EnumFailedVersion,
        InvalidOperation_EmptyStack,
        ArgumentOutOfRange_BiggerThanCollection,
        InvalidOperation_EnumNotStarted,
        InvalidOperation_EnumEnded,
        NotSupported_SortedListNestedWrite,
        InvalidOperation_NoValue,
        InvalidOperation_RegRemoveSubKey,
        Security_RegistryPermission,
        UnauthorizedAccess_RegistryNoWrite,
        ObjectDisposed_RegKeyClosed,
        NotSupported_InComparableType,
        Argument_InvalidRegistryOptionsCheck,
        Argument_InvalidRegistryViewCheck
    }


}
