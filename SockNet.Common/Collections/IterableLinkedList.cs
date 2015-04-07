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
using System.Collections;
using System.Collections.Generic;

namespace ArenaNet.SockNet.Common.Collections
{
    /// <summary>
    /// A linked list that can be modified while being iterated upon.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class IterableLinkedList<T> : IEnumerable<T>, IEnumerable
    {
        internal IterableLinkedListNode<T> root = (IterableLinkedListNode<T>)null;
        internal IterableLinkedListNode<T> tail = (IterableLinkedListNode<T>)null;

        IEnumerator IEnumerable.GetEnumerator()
        {
            return (IEnumerator)new IterableLinkedListEnumerator<T>(this);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return (IEnumerator<T>)new IterableLinkedListEnumerator<T>(this);
        }

        /// <summary>
        /// Adds an item as the first element in this list.
        /// </summary>
        /// <param name="value"></param>
        public void AddFirst(T value)
        {
            if (value == null)
            {
                throw new ArgumentException("Value cannot be null.");
            }

            this.root = new IterableLinkedListNode<T>(value, this.root);

            if (this.tail == null)
            {
                this.tail = this.root;
            }
        }

        /// <summary>
        /// Adds an item as the last element in this list.
        /// </summary>
        /// <param name="value"></param>
        public void AddLast(T value)
        {
            if (value == null)
            {
                throw new ArgumentException("Value cannot be null.");
            }

            IterableLinkedListNode<T> iterableLinkedListNode = new IterableLinkedListNode<T>(value, null);

            if (this.root == null)
            {
                this.root = iterableLinkedListNode;
                this.tail = iterableLinkedListNode;
            }
            else
            {
                this.tail.Next = iterableLinkedListNode;
                this.tail = iterableLinkedListNode;
            }
        }

        /// <summary>
        /// Adds an item before the given item.
        /// </summary>
        /// <param name="pre"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool AddBefore(T pre, T value)
        {
            if (value == null)
            {
                throw new ArgumentException("Value cannot be null.");
            }

            IterableLinkedListNodeLink<T> node = this.FindNode(pre);

            if (node == null)
            {
                return false;
            }

            IterableLinkedListNode<T> iterableLinkedListNode = new IterableLinkedListNode<T>(value, node.Child);

            if (node.Parent == null)
            {
                this.root = iterableLinkedListNode;
            }
            else
            {
                node.Parent.Next = iterableLinkedListNode;
            }

            return true;
        }

        /// <summary>
        /// Adds an item after the given item.
        /// </summary>
        /// <param name="pre"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool AddAfter(T pre, T value)
        {
            if (value == null)
            {
                throw new ArgumentException("Value cannot be null.");
            }

            IterableLinkedListNodeLink<T> node = this.FindNode(pre);

            if (node == null)
            {
                return false;
            }

            IterableLinkedListNode<T> iterableLinkedListNode = new IterableLinkedListNode<T>(value, node.Child.Next);

            node.Child.Next = iterableLinkedListNode;

            if (node.Child == this.tail)
            {
                this.tail = iterableLinkedListNode;
            }

            return true;
        }

        /// <summary>
        /// Removes an item with the given value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool Remove(T value)
        {
            if (value == null)
            {
                throw new ArgumentException("Value cannot be null.");
            }

            IterableLinkedListNodeLink<T> node = this.FindNode(value);

            if (node == null)
            {
                return false;
            }

            if (root == node.Child)
            {
                this.root = node.Child.Next;

                if (this.root == null)
                {
                    this.tail = null;
                }
            }

            if (tail == node.Child)
            {
                this.tail = node.Parent;
            }

            if (node.Parent != null)
            {
                node.Parent.Next = node.Child.Next;
            }

            return true;
        }

        /// <summary>
        /// Finds a node with the given value and returns the link between that node and its parent.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private IterableLinkedListNodeLink<T> FindNode(T value)
        {
            IterableLinkedListNode<T> parent = (IterableLinkedListNode<T>)null;

            for (IterableLinkedListNode<T> child = this.root; child != null; child = child.Next)
            {
                if (child.Value.Equals((object)value))
                {
                    return new IterableLinkedListNodeLink<T>(parent, child);
                }

                parent = child;
            }

            return null;
        }
    }

    /// <summary>
    /// Represents a generic node in a linked list.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class IterableLinkedListNode<T>
    {
        public T Value { get; set; }

        public IterableLinkedListNode<T> Next { get; set; }

        public IterableLinkedListNode(T value, IterableLinkedListNode<T> next)
        {
            this.Value = value;
            this.Next = next;
        }
    }

    /// <summary>
    /// Represents a generic link between nodes in a linked list.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class IterableLinkedListNodeLink<T>
    {
        public IterableLinkedListNode<T> Parent { get; set; }

        public IterableLinkedListNode<T> Child { get; set; }

        public IterableLinkedListNodeLink(IterableLinkedListNode<T> parent, IterableLinkedListNode<T> child)
        {
            this.Parent = parent;
            this.Child = child;
        }
    }

    /// <summary>
    /// The enumerator of generic nodes in a linked list.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class IterableLinkedListEnumerator<T> : IEnumerator<T>, IEnumerator, IDisposable
    {
        private IterableLinkedListNode<T> currentNode = (IterableLinkedListNode<T>)null;
        private IterableLinkedList<T> sourceList = (IterableLinkedList<T>)null;

        T IEnumerator<T>.Current
        {
            get
            {
                return this.currentNode == null ? default(T) : this.currentNode.Value;
            }
        }

        object IEnumerator.Current
        {
            get
            {
                return (object)(this.currentNode == null ? default(T) : this.currentNode.Value);
            }
        }

        public IterableLinkedListEnumerator(IterableLinkedList<T> list)
        {
            this.sourceList = list;
        }

        public bool MoveNext()
        {
            this.currentNode = this.currentNode != null ? this.currentNode.Next : this.sourceList.root;

            return this.currentNode != null;
        }

        public void Reset()
        {
            this.currentNode = this.sourceList.root;
        }

        public void Dispose()
        {
            this.currentNode = null;
            this.sourceList = null;
        }
    }
}
