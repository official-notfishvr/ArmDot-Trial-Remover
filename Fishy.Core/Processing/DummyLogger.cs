using System.Text.RegularExpressions;
using dnlib.DotNet;

namespace Fishy.Processing;

internal sealed partial class DummyLogger(ModuleDef? module = null, MethodChangeTracker? tracker = null) : ILogger
{
    [GeneratedRegex(@"during writing method '(?<method>.+)' \(0x[0-9A-Fa-f]+\)")]
    private static partial Regex WritingMethodRegex();

    public int Errors { get; private set; }
    public int Warnings { get; private set; }

    public bool IgnoresEvent(LoggerEvent loggerEvent) => loggerEvent is LoggerEvent.Info or LoggerEvent.Verbose or LoggerEvent.VeryVerbose;

    public void Log(object sender, LoggerEvent loggerEvent, string format, params object[] args)
    {
        switch (loggerEvent)
        {
            case LoggerEvent.Error:
                Errors++;
                break;
            case LoggerEvent.Warning:
                Warnings++;
                break;
            default:
                return;
        }

        var message = args is { Length: > 0 } ? string.Format(format, args) : format;
        if (loggerEvent == LoggerEvent.Error)
            message = Enrich(message);

        Console.Error.WriteLine($"[dnlib {loggerEvent.ToString().ToLowerInvariant()}] {message}");
    }

    private string Enrich(string message)
    {
        var match = WritingMethodRegex().Match(message);
        if (!match.Success)
            return message;

        var methodFullName = match.Groups["method"].Value;
        var method = module is null ? null : tracker?.FindByFullName(module, methodFullName);

        if (method is not null)
        {
            var declaringType = method.DeclaringType?.FullName ?? "<unknown type>";
            var paramCount = method.Parameters.Count;
            var localCount = method.Body?.Variables.Count ?? 0;
            var instrCount = method.Body?.Instructions.Count ?? 0;
            message += $"{Environment.NewLine}  declaring type : {declaringType}";
            message += $"{Environment.NewLine}  parameters     : {paramCount}, locals: {localCount}, instructions: {instrCount}";

            var stages = tracker?.GetStages(method) ?? [];
            message += stages.Count > 0 ? $"{Environment.NewLine}  modified by    : {string.Join(" -> ", stages)}" : $"{Environment.NewLine}  modified by    : none of the obfuscation stages (method was already invalid, or untouched)";
        }

        message +=
            $"{Environment.NewLine}  DO NOT set or add CilBody.KeepOldMaxStack or MetadataOptions.Flags KeepOldMaxStack to silence this."
            + $"{Environment.NewLine}  That flag only tells the writer to reuse an existing (and here, never-computed) max-stack value instead of"
            + $"{Environment.NewLine}  recalculating it - it does not fix anything. This error means the stage above emitted CIL that doesn't"
            + $"{Environment.NewLine}  balance (an unreachable/mismatched stack depth across branches, a dangling branch target, or similar) -"
            + $"{Environment.NewLine}  fix the stage's instruction emission instead.";

        return message;
    }
}
