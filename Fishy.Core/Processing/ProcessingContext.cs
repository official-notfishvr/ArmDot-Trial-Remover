using dnlib.DotNet;

namespace Fishy.Processing;

public sealed class ProcessingContext(ModuleDef module, string inputPath, string outputPath)
{
    public ModuleDef Module { get; } = module;
    public string InputPath { get; } = inputPath;
    public string OutputPath { get; } = outputPath;
    public IList<ITransform> Stages { get; } = new List<ITransform>();
    public int ChangedItems { get; set; }
}
