using dnlib.DotNet;

namespace Fishy.Helpers;

public sealed class TargetRuntimeReferences
{
    public TargetRuntimeReferences(ModuleDef module)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
    }

    public ModuleDef Module { get; }
    public ICorLibTypes CorLib => Module.CorLibTypes;

    public bool IsNetStandard21 => string.Equals(CorLib.AssemblyRef.Name, "netstandard", StringComparison.OrdinalIgnoreCase) && CorLib.AssemblyRef.Version.Major == 2 && CorLib.AssemblyRef.Version.Minor == 1;

    public TypeRef Type(string @namespace, string name) => CorLib.GetTypeRef(@namespace, name);

    public TypeRef FrameworkType(TypeRef source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (IsNetStandard21 && source.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            return Type(source.Namespace, source.Name);

        return source;
    }

    public MemberRef InstanceMethod(ITypeDefOrRef declaringType, string name, MethodSig signature) => new MemberRefUser(Module, name, signature, declaringType);

    public MemberRef StaticMethod(ITypeDefOrRef declaringType, string name, MethodSig signature) => new MemberRefUser(Module, name, signature, declaringType);

    public MemberRef GetMethodFromHandle()
    {
        var methodBase = Type("System.Reflection", "MethodBase");
        var runtimeHandle = Type("System", "RuntimeMethodHandle");
        return StaticMethod(methodBase, "GetMethodFromHandle", MethodSig.CreateStatic(new ClassSig(methodBase), new ValueTypeSig(runtimeHandle)));
    }

    public MemberRef GetTypeFromHandle()
    {
        var type = Type("System", "Type");
        var runtimeHandle = Type("System", "RuntimeTypeHandle");
        return StaticMethod(type, nameof(System.Type.GetTypeFromHandle), MethodSig.CreateStatic(new ClassSig(type), new ValueTypeSig(runtimeHandle)));
    }

    public MemberRef GetModule()
    {
        var type = Type("System", "Type");
        var module = Type("System.Reflection", "Module");
        return InstanceMethod(type, "get_Module", MethodSig.CreateInstance(new ClassSig(module)));
    }
}
