using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Fishy.Helpers;
using Fishy.Processing;

namespace Fishy.Deobfuscation.armdot;

[TransformCommand("deobfuscator", "armdot-demo-limit", Order = 30, Aliases = ["armdot"], IncludeInFull = true, AutoSelectOnDrop = true)]
public sealed class DemoLimitDeobfuscator : ITransform
{
    public string Name => "Armdot seven day demo limit";

    public int Apply(ProcessingContext context)
    {
        var moduleType = context.Module.Types.FirstOrDefault(type => type.Name == "<Module>");
        if (moduleType is null)
            return 0;

        var module = context.Module;
        var methods = moduleType.Methods.Where(method => method.IsStatic && method.HasBody && method.Body!.ExceptionHandlers.Count == 0).ToArray();
        var clocks = methods
            .Where(method =>
                method.MethodSig is { Params.Count: 0, RetType.FullName: "System.DateTime" }
                && method.Body!.Instructions.Count == 2
                && method.Body.Instructions[0].OpCode.Code == Code.Call
                && method.Body.Instructions[0].Operand is IMethod call
                && call.FullName.Contains("System.DateTime::get_UtcNow", StringComparison.Ordinal)
                && method.Body.Instructions[1].OpCode.Code == Code.Ret
            )
            .ToArray();
        var ticks = methods
            .Where(method =>
                method.MethodSig is { RetType.ElementType: ElementType.I8, Params.Count: 1 } signature
                && signature.Params[0] is ByRefSig byRef
                && byRef.Next.FullName == "System.DateTime"
                && method.Body!.Instructions.Any(instruction => instruction.Operand is IMethod call && call.FullName.Contains("System.DateTime::get_Ticks", StringComparison.Ordinal))
            )
            .ToArray();
        var failures = methods
            .Where(method =>
                method.MethodSig is { RetType.ElementType: ElementType.Void, Params.Count: 1 } signature
                && signature.Params[0].ElementType == ElementType.String
                && method.Body!.Instructions.Any(instruction => instruction.Operand is IMethod call && call.FullName.Contains("System.Environment::FailFast", StringComparison.Ordinal))
            )
            .ToArray();
        var exceptions = methods
            .Where(method =>
                method.MethodSig is { RetType.FullName: "System.Exception", Params.Count: 1 } signature
                && signature.Params[0].ElementType == ElementType.String
                && method.Body!.Instructions.Any(instruction => instruction.OpCode.Code == Code.Newobj && instruction.Operand is IMethod call && call.Name == ".ctor" && call.DeclaringType.FullName == "System.Exception")
            )
            .ToArray();
        if (clocks.Length != 1 || ticks.Length != 1 || failures.Length != 1 || exceptions.Length != 1)
            return 0;

        var helpers = new[] { clocks[0], ticks[0], failures[0], exceptions[0] };
        var tables = methods
            .Where(method =>
                helpers.All(helper => method.Body!.Instructions.Any(instruction => instruction.OpCode.Code == Code.Ldftn && instruction.Operand is IMethod target && ReferenceEquals(target.ResolveMethodDef(), helper)))
                && method.Body!.Instructions.Any(instruction => instruction.OpCode.Code is Code.Stelem_I or Code.Stelem_I4)
            )
            .ToArray();
        if (tables.Length != 1)
            return 0;

        var table = tables[0];
        if (
            !moduleType.Methods.Any(initializer =>
                initializer.IsStatic
                && (initializer.Name == ".cctor" || initializer.Name.StartsWith(".cctor$", StringComparison.Ordinal))
                && initializer.HasBody
                && initializer.Body!.Instructions.Any(instruction => instruction.OpCode.Code == Code.Call && instruction.Operand is IMethod target && ReferenceEquals(target.ResolveMethodDef(), table))
            )
        )
            return 0;

        foreach (var method in module.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody))
        foreach (var instruction in method.Body!.Instructions)
            if (instruction.Operand is IMethod target && ReferenceEquals(target.ResolveMethodDef(), clocks[0]) && (!ReferenceEquals(method, table) || instruction.OpCode.Code != Code.Ldftn))
                return 0;

        var references = new TargetRuntimeReferences(module);
        var dateTime = references.Type("System", "DateTime");
        var constructor = references.InstanceMethod(dateTime, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Int64));
        var body = clocks[0].Body!;
        body.Instructions.Clear();
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I8, 0L));
        body.Instructions.Add(Instruction.Create(OpCodes.Newobj, constructor));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.Variables.Clear();
        body.InitLocals = false;
        body.MaxStack = 2;
        return 1;
    }
}
