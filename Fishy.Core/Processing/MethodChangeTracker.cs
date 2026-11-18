using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Fishy.Processing;

internal sealed class MethodChangeTracker
{
    private readonly Dictionary<MethodDef, int> _fingerprints = new();
    private readonly Dictionary<MethodDef, List<string>> _modifiedBy = new();

    public void Snapshot(ModuleDef module)
    {
        foreach (var method in AllMethodsWithBodies(module))
            _fingerprints[method] = Fingerprint(method.Body!);
    }

    public void DiffAndRecord(ModuleDef module, string stageName)
    {
        foreach (var method in AllMethodsWithBodies(module))
        {
            var current = Fingerprint(method.Body!);
            if (!_fingerprints.TryGetValue(method, out var previous))
            {
                _fingerprints[method] = current;
                Record(method, stageName);
                continue;
            }

            if (previous != current)
            {
                _fingerprints[method] = current;
                Record(method, stageName);
            }
        }
    }

    public IReadOnlyList<string> GetStages(MethodDef method) => _modifiedBy.TryGetValue(method, out var stages) ? stages : [];

    public MethodDef? FindByFullName(ModuleDef module, string fullName) => module.GetTypes().SelectMany(t => t.Methods).FirstOrDefault(m => m.FullName == fullName);

    private void Record(MethodDef method, string stageName)
    {
        if (!_modifiedBy.TryGetValue(method, out var stages))
            _modifiedBy[method] = stages = [];
        if (stages.Count == 0 || stages[^1] != stageName)
            stages.Add(stageName);
    }

    private static IEnumerable<MethodDef> AllMethodsWithBodies(ModuleDef module) => module.GetTypes().SelectMany(type => type.Methods).Where(method => method.Body is not null);

    private static int Fingerprint(CilBody body)
    {
        var offsets = new Dictionary<Instruction, int>(body.Instructions.Count);
        for (var i = 0; i < body.Instructions.Count; i++)
            offsets[body.Instructions[i]] = i;
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + body.Variables.Count;
            hash = hash * 31 + body.ExceptionHandlers.Count;
            hash = hash * 31 + body.Instructions.Count;
            foreach (var instr in body.Instructions)
            {
                hash = hash * 31 + instr.OpCode.Code.GetHashCode();
                hash = hash * 31 + OperandHash(instr.Operand, offsets);
            }
            return hash;
        }
    }

    private static int OperandHash(object? operand, Dictionary<Instruction, int> offsets) =>
        operand switch
        {
            null => 0,
            Instruction target => offsets.TryGetValue(target, out var index) ? index : -1,
            IList<Instruction> targets => targets.Aggregate(targets.Count, (h, t) => h * 31 + (offsets.TryGetValue(t, out var i) ? i : -1)),
            IVariable variable => variable.Index,
            IMethod m => m.FullName.GetHashCode(StringComparison.Ordinal),
            IField f => f.FullName.GetHashCode(StringComparison.Ordinal),
            ITypeDefOrRef t => t.FullName.GetHashCode(StringComparison.Ordinal),
            string s => s.GetHashCode(StringComparison.Ordinal),
            _ => operand.GetHashCode(),
        };
}
