using System;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Protocols.Http2.Hpack
{
    public class HpackDynamicTable
    {
        // a circular queue of header fields
        private HpackHeader[] headerFields;
        private int head;
        private int tail;
        private int size;
        private int capacity = -1; // ensure setCapacity creates the array

        /**
         * Creates a new dynamic table with the specified initial capacity.
         */
        public HpackDynamicTable(int initialCapacity)
        {
            SetCapacity(initialCapacity);
        }

        /**
         * Return the number of header fields in the dynamic table.
         */
        public int Length()
        {
            int length;
            if (head < tail)
            {
                length = headerFields.Length - tail + head;
            }
            else
            {
                length = head - tail;
            }
            return length;
        }

        /**
         * Return the current size of the dynamic table.
         * This is the sum of the size of the entries.
         */
        public int Size()
        {
            return size;
        }

        /**
         * Return the maximum allowable size of the dynamic table.
         */
        public int Capacity()
        {
            return capacity;
        }

        /**
         * Return the header field at the given index.
         * The first and newest entry is always at index 1,
         * and the oldest entry is at the index length().
         */
        public HpackHeader GetEntry(int index)
        {
            if (index <= 0 || index > Length())
            {
                throw new ArgumentOutOfRangeException();
            }
            int i = head - index;
            if (i < 0)
            {
                return headerFields[i + headerFields.Length];
            }
            else
            {
                return headerFields[i];
            }
        }

        /**
         * Add the header field to the header table.
         * Entries are evicted from the header table until the size of the table
         * and the new header field is less than or equal to the table's capacity.
         * If the size of the new entry is larger than the table's capacity,
         * the header table will be cleared.
         */
        public void Add(HpackHeader header)
        {
            int headerSize = header.Size;
            if (headerSize > capacity)
            {
                Clear();
                return;
            }
            while (size + headerSize > capacity)
            {
                Remove();
            }
            headerFields[head++] = header;
            size += header.Size;
            if (head == headerFields.Length)
            {
                head = 0;
            }
        }


        /**
         * Remove and return the oldest header field from the dynamic table.
         */
        public HpackHeader Remove()
        {
            HpackHeader removed = headerFields[tail];
            if (removed == null)
            {
                return null;
            }
            size -= removed.Size;
            headerFields[tail++] = null;
            if (tail == headerFields.Length)
            {
                tail = 0;
            }
            return removed;
        }

        /**
         * Remove all entries from the dynamic table.
         */
        public void Clear()
        {
            while (tail != head)
            {
                headerFields[tail++] = null;
                if (tail == headerFields.Length)
                {
                    tail = 0;
                }
            }
            head = 0;
            tail = 0;
            size = 0;
        }

        /**
         * Set the maximum size of the dynamic table.
         * Entries are evicted from the header table until the size of the table
         * is less than or equal to the maximum size.
         */
        public void SetCapacity(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentException("Illegal Capacity: " + capacity);
            }

            // initially capacity will be -1 so init won't return here
            if (this.capacity == capacity)
            {
                return;
            }
            this.capacity = capacity;

            // initially size will be 0 so remove won't be called
            if (capacity == 0)
            {
                Clear();
            }
            else
            {
                while (size > capacity)
                {
                    Remove();
                }
            }

            int maxEntries = capacity / HpackHeader.HEADER_ENTRY_OVERHEAD;
            if (capacity % HpackHeader.HEADER_ENTRY_OVERHEAD != 0)
            {
                maxEntries++;
            }

            // check if capacity change requires us to reallocate the array
            if (headerFields != null && headerFields.Length == maxEntries)
            {
                return;
            }

            HpackHeader[] tmp = new HpackHeader[maxEntries];

            // initially length will be 0 so there will be no copy
            int len = Length();
            int cursor = tail;
            for (int i = 0; i < len; i++)
            {
                HpackHeader entry = headerFields[cursor++];
                tmp[i] = entry;
                if (cursor == headerFields.Length)
                {
                    cursor = 0;
                }
            }

            this.tail = 0;
            this.head = tail + len;
            this.headerFields = tmp;
        }
    }
}
