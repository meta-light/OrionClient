namespace OrionClientLib.Pools.Models
{
    public interface IMessage
    {
        public void Deserialize(ArraySegment<byte> data);
        public ArraySegment<byte> Serialize();
    }
}
