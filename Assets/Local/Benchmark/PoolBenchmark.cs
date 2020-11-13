using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Mirror.Benchmark
{
    public class PoolBenchmark : MonoBehaviour
    {
        public sealed class OutLog : IDisposable
        {
            StreamWriter stream;

            public void Dispose()
            {
                stream.Close();
            }

            public OutLog()
            {
                int i = 1;

                while (File.Exists($"./test{i.ToString().PadLeft(2, '0')}.txt"))
                {
                    i++;
                }

                stream = new StreamWriter($"./test{i.ToString().PadLeft(2, '0')}.txt");
                stream.AutoFlush = true;
            }

            public void Log(string text)
            {
                stream.WriteLine(text);
            }
        }
        public class FakeConnection : NetworkConnectionToClient
        {
            public FakeConnection(int networkConnectionId) : base(networkConnectionId)
            {
                isReady = true;
                isAuthenticated = true;
            }

            public override string address => throw new NotImplementedException();

            public override void Disconnect()
            {
                // nothing
            }

            public override void Send(ArraySegment<byte> segment, int channelId = 0)
            {
                // nothing
            }
        }

        public class Instance
        {
            private int identityCount;
            NetworkIdentity[] identities;
            BenchmarkDoStuffBehaviour[] behaviours;

            public Instance(GameObject prefab, int identityCount, int connCount)
            {
                NetworkServer.Listen(1000);
                for (int i = 0; i < connCount; i++)
                {
                    NetworkServer.AddConnection(new FakeConnection(i + 1));
                }

                this.identityCount = identityCount;
                identities = new NetworkIdentity[identityCount];
                behaviours = new BenchmarkDoStuffBehaviour[identityCount];
                for (int i = 0; i < identityCount; i++)
                {
                    GameObject clone = Instantiate(prefab);
                    NetworkServer.Spawn(clone);
                    identities[i] = clone.GetComponent<NetworkIdentity>();
                    behaviours[i] = clone.GetComponent<BenchmarkDoStuffBehaviour>();
                    behaviours[i].syncInterval = 0;
                }
            }
            public void runUpdate()
            {
                for (int i = 0; i < identityCount; i++)
                {
                    behaviours[i].value++;
                }
                NetworkServer.Update();
            }
            public void Stop()
            {
                for (int i = 0; i < identityCount; i++)
                {
                    NetworkServer.Destroy(identities[i].gameObject);
                }
                NetworkServer.Shutdown();
            }
        }

        [SerializeField] GameObject prefab;
        byte[] buffer = new byte[] { 1 };
        OutLog output;

        private void Start()
        {
            Transport.activeTransport = gameObject.AddComponent<FakeTransport>();
            using (output = new OutLog())
            {
                output.Log($"\n-------------\n\n");

                int iterations = 10000;

                runGroup(iterations, 10, 1);
                runGroup(iterations, 10, 2);
                runGroup(iterations, 10, 5);
                runGroup(iterations, 10, 10);

                iterations = 5000;
                runGroup(iterations, 100, 1);
                runGroup(iterations, 100, 2);
                runGroup(iterations, 100, 5);
                runGroup(iterations, 100, 10);
                runGroup(iterations, 100, 20);

                iterations = 2000;
                runGroup(iterations, 1000, 1);
                runGroup(iterations, 1000, 2);
                runGroup(iterations, 1000, 5);
                runGroup(iterations, 1000, 10);
                runGroup(iterations, 1000, 20);
                runGroup(iterations, 1000, 50);
                runGroup(iterations, 1000, 100);


                //int iterations = 1000000;
                //testOne("", WriterPoolGetMaster, iterations);
                //testOne("", WriterPoolGetPR2414, iterations);
                //testOne("", ReaderPoolGetMaster, iterations);
                //testOne("", ReaderPoolGetPR2414, iterations);

                output.Log($"\n\n\n-------------\n\n");
            }
#if UNITY_EDITOR
            UnityEngine.Debug.Break();
#else
            Application.Quit();
#endif
        }

        private void runGroup(int iterations, int identityCount, int connCount)
        {
            NetworkWriterPool.useMaster = true;
            NetworkReaderPool.useMaster = true;
            long a = runLoops("master", iterations, identityCount, connCount);
            NetworkWriterPool.useMaster = false;
            NetworkReaderPool.useMaster = false;
            long b = runLoops("PR2414", iterations, identityCount, connCount);

            output.Log($"{"diff a/b",-40}{"",5}{"",5}{a / (double)b,10:0.00}");
            output.Log($"-------------\n");

        }

        private void Update()
        {
            return;
            int iterations = 1000;

            testOne("", WriterPoolGetMaster, iterations);
            testOne("", WriterPoolGetPR2414, iterations);
            testOne("", ReaderPoolGetMaster, iterations);
            testOne("", ReaderPoolGetPR2414, iterations);
        }

        void testOne(string label, Action action, int iterations)
        {
            string name = action.GetMethodInfo().Name;

            // warmup
            for (int n = 0; n < iterations / 100; n++)
            {
                action.Invoke();
            }

            Stopwatch sw = Stopwatch.StartNew();
            for (int n = 0; n < iterations; n++)
            {
                action.Invoke();
            }
            sw.Stop();
            string text = $"{label} - {name}";
            output.Log($"{text,-40}{sw.ElapsedMilliseconds,10}");
        }

        void WriterPoolGetMaster()
        {
            using (PooledNetworkWriter writer = NetworkWriterPoolMaster.GetWriter())
            {
                writer.WriteByte(1);
            }
        }
        void WriterPoolGetPR2414()
        {
            using (PooledNetworkWriter_PR2414 writer = NetworkWriterPool_PR2414.GetWriter())
            {
                writer.WriteByte(1);
            }
        }
        void ReaderPoolGetMaster()
        {
            using (PooledNetworkReader reader = NetworkReaderPoolMaster.GetReader(buffer))
            {
                reader.ReadByte();
            }
        }
        void ReaderPoolGetPR2414()
        {
            using (PooledNetworkReader_PR2414 reader = NetworkReaderPool_PR2414.GetReader(buffer))
            {
                reader.ReadByte();
            }
        }


        long runLoops(string label, int iterations, int identityCount, int connCount)
        {
            Instance inst = new Instance(prefab, identityCount, connCount);

            // warmup
            for (int i = 0; i < iterations / 100; i++)
            {
                inst.runUpdate();
            }

            Stopwatch sw = Stopwatch.StartNew();
            for (int n = 0; n < iterations; n++)
            {
                inst.runUpdate();
            }
            sw.Stop();

            inst.Stop();
            output.Log($"{label,-40}{identityCount,5}{connCount,5}{sw.ElapsedMilliseconds,10}");
            return sw.ElapsedMilliseconds;
        }
    }
}
