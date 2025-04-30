using OrionClientLib.Pools.Models;
using System.Buffers.Binary;

namespace OrionClientLib.Pools.HQPool
{
    public class OreHQChallengeResponse : IMessage
    {
        public byte[] Challenge { get; private set; }
        public ulong StartNonce { get; private set; }
        public ulong EndNonce { get; private set; }
        public ulong Cutoff { get; private set; }

        public void Deserialize(ArraySegment<byte> data)
        {
            Challenge = data.Slice(1, 32).ToArray();
            Cutoff = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(33, 8));
            StartNonce = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(41, 8));
            EndNonce = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(49, 8));
        }

        public ArraySegment<byte> Serialize()
        {
            throw new NotImplementedException();
        }
    }
}
