using Fishy.Processing;

if (!TransformCommandLine.TryParse(args, "deobfuscator", defaultVerbose: false, out var command, out var error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(TransformCommandLine.GetSynopsis("deobfuscator"));
    return 2;
}

if (command.ShowHelp)
{
    Console.WriteLine(TransformCommandLine.GetUsage("deobfuscator"));
    return 0;
}

var failed = false;
if (command.Inputs.Count > 1 && command.Output is not null)
    Directory.CreateDirectory(command.Output);

foreach (var input in command.Inputs)
{
    try
    {
        var output =
            command.Output is null ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!, Path.GetFileNameWithoutExtension(input) + ".Fishy" + Path.GetExtension(input))
            : command.Inputs.Count == 1 ? command.Output
            : Path.Combine(command.Output, Path.GetFileNameWithoutExtension(input) + ".Fishy" + Path.GetExtension(input));
        var stages = command.CreateStages();
        var context = new ProcessingContext(ModuleProcessor.Load(input), input, output);
        foreach (var stage in stages)
            context.Stages.Add(stage);
        var changed = ModuleProcessor.Run(context, command.Verbose ? msg => Console.WriteLine($"  {msg}") : null);
        Console.WriteLine($"Deobfuscated {Path.GetFileName(input)} -> {output} ({changed} change(s)).");
    }
    catch (Exception exception)
    {
        failed = true;
        Console.Error.WriteLine($"error processing '{input}': {exception.Message}");
    }
}

return failed ? 1 : 0;
