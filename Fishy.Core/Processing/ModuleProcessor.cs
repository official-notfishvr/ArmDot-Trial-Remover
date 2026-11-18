using System.Diagnostics;
using System.Reflection.PortableExecutable;
using dnlib.DotNet;
using dnlib.DotNet.Writer;

namespace Fishy.Processing;

public static class ModuleProcessor
{
    public static int Run(ProcessingContext context, Action<string>? progress = null)
    {
        var tracker = new MethodChangeTracker();
        tracker.Snapshot(context.Module);

        var stopwatch = new Stopwatch();
        foreach (var stage in context.Stages)
        {
            stopwatch.Restart();
            int changed;
            try
            {
                changed = stage.Apply(context);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Stage '{stage.Name}' failed: {ex.Message}", ex);
            }

            tracker.DiffAndRecord(context.Module, stage.Name);
            context.ChangedItems += changed;
            progress?.Invoke($"{stage.Name}: {changed} change(s) in {stopwatch.ElapsedMilliseconds} ms");
        }

        var logger = new DummyLogger(context.Module, tracker);
        var options = new ModuleWriterOptions(context.Module) { Logger = logger };
        try
        {
            context.Module.Write(context.OutputPath, options);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to write '{context.OutputPath}': {ex.Message}", ex);
        }

        if (logger.Errors > 0)
            throw new InvalidOperationException($"Writing '{context.OutputPath}' reported {logger.Errors} error(s); the output assembly is likely invalid.");

        if (logger.Warnings > 0)
            progress?.Invoke($"writer reported {logger.Warnings} warning(s)");

        foreach (var stage in context.Stages.OfType<IPostWriteTransform>())
        {
            try
            {
                stage.AfterWrite(context);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Stage '{stage.Name}' failed after writing '{context.OutputPath}': {ex.Message}", ex);
            }
        }

        return context.ChangedItems;
    }

    public static ModuleDefMD Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Input assembly was not found.", path);

        if (!IsManagedAssembly(path))
        {
            var managedPath = Path.ChangeExtension(path, ".dll");
            if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(managedPath) && IsManagedAssembly(managedPath))
                throw new BadImageFormatException($"Input '{path}' is a native .NET apphost. Pass its managed assembly '{managedPath}' instead.");

            throw new BadImageFormatException($"Input '{path}' is not a managed .NET assembly (its PE image has no CLI header).");
        }

        return ModuleDefMD.Load(path);
    }

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.HasMetadata && reader.PEHeaders.CorHeader is not null;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }
}
