using System;

namespace Mirror.Benchmark
{
    public class FakeTransport : Transport
    {
        public override bool Available()
        {
            return true;
        }

        public override void ClientConnect(string address)
        {
        }

        public override bool ClientConnected()
        {
            return false;
        }

        public override void ClientDisconnect()
        {
            // nothing
        }

        public override void ClientSend(int channelId, ArraySegment<byte> segment)
        {
            // nothing
        }

        public override int GetMaxPacketSize(int channelId = 0)
        {
            return default;
        }

        public override bool ServerActive()
        {
            return true;
        }

        public override bool ServerDisconnect(int connectionId)
        {
            return true;
        }

        public override string ServerGetClientAddress(int connectionId)
        {
            return default;
        }

        public override void ServerSend(int connectionId, int channelId, ArraySegment<byte> segment)
        {
            // nothing
        }

        public override void ServerStart()
        {
            // nothing
        }

        public override void ServerStop()
        {
            // nothing
        }

        public override Uri ServerUri()
        {
            return default;
        }

        public override void Shutdown()
        {
            // nothing
        }
    }
}
