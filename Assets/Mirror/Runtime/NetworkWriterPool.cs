using System;
using UnityEngine;

namespace Mirror
{
    public abstract class PooledNetworkWriter : NetworkWriter, IDisposable
    {
        public abstract void Dispose();
    }

    public static class NetworkWriterPool
    {
        public static bool useMaster;

        /// <summary>
        /// Get the next writer in the pool
        /// <para>If pool is empty, creates a new Writer</para>
        /// </summary>
        public static PooledNetworkWriter GetWriter()
        {
            if (useMaster)
            {
                return NetworkWriterPoolMaster.GetWriter();
            }
            else
            {
                return NetworkWriterPool_PR2414.GetWriter();
            }
        }

        internal static void Recycle(PooledNetworkWriter a)
        {
            if (useMaster)
            {
                NetworkWriterPoolMaster.Recycle((PooledNetworkWriterMaster)a);
            }
            else
            {
                NetworkWriterPool_PR2414.Recycle((PooledNetworkWriter_PR2414)a);
            }
        }
    }


    /// NetworkWriter to be used with <see cref="NetworkWriterPool">NetworkWriterPool</see>
    /// </summary>
    public sealed class PooledNetworkWriterMaster : PooledNetworkWriter
    {
        public sealed override void Dispose()
        {
            NetworkWriterPoolMaster.Recycle(this);
        }
    }


    /// <summary>
    /// Pool of NetworkWriters
    /// <para>Use this pool instead of <see cref="NetworkWriter">NetworkWriter</see> to reduce memory allocation</para>
    /// <para>Use <see cref="Capacity">Capacity</see> to change size of pool</para>
    /// </summary>
    public static class NetworkWriterPoolMaster
    {
        static readonly ILogger logger = LogFactory.GetLogger(typeof(NetworkWriterPoolMaster), LogType.Error);

        /// <summary>
        /// Size of the pool
        /// <para>If pool is too small getting writers will causes memory allocation</para>
        /// <para>Default value: 100 </para>
        /// </summary>
        public static int Capacity
        {
            get => pool.Length;
            set
            {
                // resize the array
                Array.Resize(ref pool, value);

                // if capacity is smaller than before, then we need to adjust
                // 'next' so it doesn't point to an index out of range
                // -> if we set '0' then next = min(_, 0-1) => -1
                // -> if we set '2' then next = min(_, 2-1) =>  1
                next = Mathf.Min(next, pool.Length - 1);
            }
        }

        /// <summary>
        /// Mirror usually only uses up to 4 writes in nested usings,
        /// 100 is a good margin for edge cases when users need a lot writers at
        /// the same time.
        ///
        /// <para>keep in mind, most entries of the pool will be null in most cases</para>
        /// </summary>
        ///
        /// Note: we use an Array instead of a Stack because it's significantly
        ///       faster: https://github.com/vis2k/Mirror/issues/1614
        static PooledNetworkWriterMaster[] pool = new PooledNetworkWriterMaster[100];

        static int next = -1;

        /// <summary>
        /// Get the next writer in the pool
        /// <para>If pool is empty, creates a new Writer</para>
        /// </summary>
        public static PooledNetworkWriterMaster GetWriter()
        {
            if (next == -1)
            {
                return new PooledNetworkWriterMaster();
            }

            PooledNetworkWriterMaster writer = pool[next];
            pool[next] = null;
            next--;

            // reset cached writer length and position
            writer.Reset();
            return writer;
        }



        /// <summary>
        /// Puts writer back into pool
        /// <para>When pool is full, the extra writer is left for the GC</para>
        /// </summary>
        public static void Recycle(PooledNetworkWriterMaster writer)
        {
            if (next < pool.Length - 1)
            {
                next++;
                pool[next] = writer;
            }
            else
            {
                logger.LogWarning("NetworkWriterPool.Recycle, Pool was full leaving extra writer for GC");
            }
        }
    }
}
