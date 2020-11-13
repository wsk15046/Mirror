using System;
using System.Collections.Generic;

namespace Mirror
{
    public class Pool_PR2414<T>
    {
        // Mirror is single threaded, no need for concurrent collections
        readonly Stack<T> objects = new Stack<T>();

        // some types might need additional parameters in their constructor, so
        // we use a Func<T> generator
        readonly Func<T> objectGenerator;

        public Pool_PR2414(Func<T> objectGenerator)
        {
            this.objectGenerator = objectGenerator;
        }

        // take an element from the pool, or create a new one if empty
        public T Take() => objects.Count > 0 ? objects.Pop() : objectGenerator();

        // return an element to the pool
        public void Return(T item) => objects.Push(item);

        // count to see how many objects are in the pool. useful for tests.
        public int Count => objects.Count;
    }

    /// <summary>
    /// NetworkWriter to be used with <see cref="NetworkWriterPool">NetworkWriterPool</see>
    /// </summary>
    public sealed class PooledNetworkWriter_PR2414 : PooledNetworkWriter
    {
        public sealed override void Dispose()
        {
            NetworkWriterPool_PR2414.Recycle(this);
        }
    }
    /// <summary>
    /// Pool of NetworkWriters
    /// <para>Use this pool instead of <see cref="NetworkWriter">NetworkWriter</see> to reduce memory allocation</para>
    /// <para>Use <see cref="Capacity">Capacity</see> to change size of pool</para>
    /// </summary>
    public static class NetworkWriterPool_PR2414
    {
        // reuse Pool<T>
        // we still wrap it in NetworkWriterPool.Get/Recyle so we can reset the
        // position before reusing.
        // this is also more consistent with NetworkReaderPool where we need to
        // assign the internal buffer before reusing.
        static readonly Pool_PR2414<PooledNetworkWriter_PR2414> pool = new Pool_PR2414<PooledNetworkWriter_PR2414>(
            () => new PooledNetworkWriter_PR2414()
        );

        /// <summary>
        /// Get the next writer in the pool
        /// <para>If pool is empty, creates a new Writer</para>
        /// </summary>
        public static PooledNetworkWriter_PR2414 GetWriter()
        {
            // grab from from pool & reset position
            PooledNetworkWriter_PR2414 writer = pool.Take();
            writer.Reset();
            return writer;
        }

        /// <summary>
        /// Puts writer back into pool
        /// <para>When pool is full, the extra writer is left for the GC</para>
        /// </summary>
        public static void Recycle(PooledNetworkWriter_PR2414 writer)
        {
            pool.Return(writer);
        }
    }
    /// <summary>
    /// NetworkReader to be used with <see cref="NetworkReaderPool">NetworkReaderPool</see>
    /// </summary>
    public class PooledNetworkReader_PR2414 : PooledNetworkReader
    {
        internal PooledNetworkReader_PR2414(byte[] bytes) : base(bytes) { }
        internal PooledNetworkReader_PR2414(ArraySegment<byte> segment) : base(segment) { }

        public override void Dispose()
        {
            NetworkReaderPool_PR2414.Recycle(this);
        }
    }

    /// <summary>
    /// Pool of NetworkReaders
    /// <para>Use this pool instead of <see cref="NetworkReader">NetworkReader</see> to reduce memory allocation</para>
    /// <para>Use <see cref="Capacity">Capacity</see> to change size of pool</para>
    /// </summary>
    public static class NetworkReaderPool_PR2414
    {
        // reuse Pool<T>
        // we still wrap it in NetworkReaderPool.Get/Recyle so we can reset the
        // position and array before reusing.
        static readonly Pool_PR2414<PooledNetworkReader_PR2414> pool = new Pool_PR2414<PooledNetworkReader_PR2414>(
            // byte[] will be assigned in GetReader
            () => new PooledNetworkReader_PR2414(new byte[] { })
        );

        /// <summary>
        /// Get the next reader in the pool
        /// <para>If pool is empty, creates a new Reader</para>
        /// </summary>
        public static PooledNetworkReader_PR2414 GetReader(byte[] bytes)
        {
            // grab from from pool & set buffer
            PooledNetworkReader_PR2414 reader = pool.Take();
            reader.buffer = new ArraySegment<byte>(bytes);
            reader.Position = 0;
            return reader;
        }

        /// <summary>
        /// Get the next reader in the pool
        /// <para>If pool is empty, creates a new Reader</para>
        /// </summary>
        public static PooledNetworkReader_PR2414 GetReader(ArraySegment<byte> segment)
        {
            // grab from from pool & set buffer
            PooledNetworkReader_PR2414 reader = pool.Take();
            reader.buffer = segment;
            reader.Position = 0;
            return reader;
        }

        /// <summary>
        /// Puts reader back into pool
        /// <para>When pool is full, the extra reader is left for the GC</para>
        /// </summary>
        public static void Recycle(PooledNetworkReader_PR2414 reader)
        {
            pool.Return(reader);
        }
    }
}
