using ILGPU.Runtime;

namespace OrionClientLib.Hashers
{
    public interface IGPUHasher
    {
        public List<Device> GetDevices(bool onlyValid);
    }
}
