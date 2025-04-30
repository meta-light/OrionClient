using OrionClientLib.Modules.Models;

namespace OrionClientLib.Modules
{
    public interface IModule
    {
        public string Name { get; }

        public Task<(bool success, string errorMessage)> InitializeAsync(Data data);
        public Task<ExecuteResult> ExecuteAsync(Data data);
        public Task ExitAsync();
    }
}
