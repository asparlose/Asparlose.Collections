using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Asparlose.Collections
{    
    public class WeakCollection<T> : ICollection<T> where T : class
    {
        readonly ReaderWriterLockSlim mutex = new ReaderWriterLockSlim();
        ConditionalWeakTable<T, Node> weakTable = new ConditionalWeakTable<T, Node>();

        Node firstNode, lastNode;

        int collectionCount = -1;
        int old_count = -1;

        void RemoveNode(Node node)
        {
            if (node.NextNode != null)
                node.NextNode.PrevNode = node.PrevNode;
            else
                lastNode = node.PrevNode;

            if (node.PrevNode != null)
                node.PrevNode.NextNode = node.NextNode;
            else
                firstNode = node.NextNode;

            if (node.TryGetValue(out var value))
                weakTable.Remove(value);
        }

        int Cleanup()
        {
            mutex.EnterWriteLock();
            try
            {
                var cc = GC.CollectionCount(0);
                if (cc != collectionCount || old_count < 0)
                {
                    collectionCount = cc;

                    var node = firstNode;
                    int count = 0;
                    while (node != null)
                    {
                        if (node.TryGetValue(out var _))
                            count++;
                        else
                            RemoveNode(node);
                        node = node.NextNode;
                    }

                    old_count = count;
                    return count;
                }
                else
                {
                    return old_count;
                }
            }
            finally
            {
                mutex.ExitWriteLock();
            }
        }

        public int Count => Cleanup();

        bool ICollection<T>.IsReadOnly => false;

        public void Add(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            Cleanup();

            mutex.EnterWriteLock();
            try
            {
                if (lastNode == null)
                {
                    firstNode = lastNode = new Node(item);
                }
                else
                {
                    lastNode = lastNode.NextNode = new Node(item) { PrevNode = lastNode };
                }
                weakTable.Add(item, lastNode);
            }
            finally
            {
                mutex.ExitWriteLock();
            }
        }

        public void Clear()
        {
            mutex.EnterWriteLock();
            try
            {
                weakTable = new ConditionalWeakTable<T, Node>();
                firstNode = lastNode = null;
            }
            finally
            {
                mutex.ExitWriteLock();
            }
        }

        public bool Contains(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            Cleanup();
            return weakTable.TryGetValue(item, out var _);
        }

        void ICollection<T>.CopyTo(T[] array, int arrayIndex)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));

            var list = new List<T>(this);
            list.CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            Cleanup();
            mutex.EnterWriteLock();
            try
            {
                if (weakTable.TryGetValue(item, out var node))
                {
                    RemoveNode(node);
                    return true;
                }
                else
                {
                    return false;
                }

            }
            finally
            {
                mutex.ExitWriteLock();
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            Cleanup();
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public IReadOnlyCollection<T> AsReadonly() => new Readonly(this);

        class Readonly : IReadOnlyCollection<T>
        {
            readonly WeakCollection<T> parent;
            public Readonly(WeakCollection<T> parent)
            {
                this.parent = parent;
            }

            public int Count => parent.Count;

            public IEnumerator<T> GetEnumerator()
                => parent.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator()
                => GetEnumerator();
        }

        class Enumerator : IEnumerator<T>
        {
            readonly WeakCollection<T> list;
            readonly Node firstNode, lastNode;
            readonly ReaderWriterLockSlim mutex;
            Node currentNode = null;

            public Enumerator(WeakCollection<T> list)
            {
                mutex = list.mutex;
                this.list = list;

                mutex.EnterReadLock();
                try
                {
                    firstNode = list.firstNode;
                    lastNode = list.lastNode;
                }
                finally
                {
                    mutex.ExitReadLock();
                }
            }

            public T Current { get; private set; }

            object IEnumerator.Current => Current;

            bool isEnd;

            public bool MoveNext()
            {
                mutex.EnterReadLock();
                try
                {
                    if (isEnd) return false;

                    if (currentNode == null)
                        currentNode = firstNode;
                    else
                        currentNode = currentNode.NextNode;

                    while (true)
                    {
                        if (currentNode == null)
                        {
                            isEnd = true;
                            return false;
                        }

                        if (currentNode.TryGetValue(out T value))
                        {
                            Current = value;
                            return true;
                        }

                        currentNode = currentNode.NextNode;
                    }
                }
                finally
                {
                    mutex.ExitReadLock();
                }
            }

            public void Reset()
            {
                mutex.EnterWriteLock();
                try
                {
                    isEnd = false;
                    currentNode = null;
                }
                finally
                {
                    mutex.ExitWriteLock();
                }
            }

            public void Dispose() { }
        }


        class Node
        {
            public Node(T value)
            {
                this.value = new WeakReference<T>(value);
            }

            readonly WeakReference<T> value;

            public bool TryGetValue(out T value) => this.value.TryGetTarget(out value);

            public Node PrevNode { get; set; }
            public Node NextNode { get; set; }
        }
    }
}
