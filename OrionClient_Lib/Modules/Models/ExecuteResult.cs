using Spectre.Console.Rendering;

namespace OrionClientLib.Modules.Models
{
    public class ExecuteResult
    {
        public IRenderable Renderer { get; set; }
        public bool Exited { get; set; }
    }
}
