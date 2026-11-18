using System.Globalization;
using System.Reflection;
using System.Text;

namespace Fishy.Processing;

public sealed record TransformCommandLineResult(IReadOnlyList<string> Inputs, string? Output, Func<IReadOnlyList<ITransform>> CreateStages, bool Verbose, bool ShowHelp)
{
    public IReadOnlyList<ITransform> Stages => CreateStages();
}

public static class TransformCommandLine
{
    public static bool TryParse(string[] args, string tool, bool defaultVerbose, out TransformCommandLineResult result, out string error)
    {
        ArgumentNullException.ThrowIfNull(args);
        var commands = Discover(tool);
        var selected = new HashSet<Command>();
        var optionValues = new Dictionary<Command, object>();
        var inputs = new List<string>();
        string? output = null;
        var verbose = defaultVerbose;
        var full = false;
        var showHelp = false;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (token is "-h" or "--help")
            {
                showHelp = true;
                continue;
            }
            if (token is "-v" or "--verbose")
            {
                verbose = true;
                continue;
            }
            if (token is "-o" or "--output")
            {
                if (++index >= args.Length)
                    return Fail($"Missing value after '{token}'.", out result, out error);
                output = args[index];
                continue;
            }
            if (token.StartsWith('-'))
            {
                var (name, inlineValue) = SplitOption(token);
                if (name == "--full")
                {
                    full = true;
                    continue;
                }

                var matched = commands.Where(command => command.Matches(name)).ToArray();
                if (matched.Length > 0)
                {
                    foreach (var command in matched)
                    {
                        selected.Add(command);
                        if (command.OptionsType is not null && !optionValues.ContainsKey(command))
                            optionValues.Add(command, Activator.CreateInstance(command.OptionsType)!);
                    }
                    continue;
                }

                if (TryFindOption(commands, name, out var optionCommand, out var property, out var negate))
                {
                    selected.Add(optionCommand);
                    var options = GetOptions(optionValues, optionCommand);
                    if (!TrySetOption(property, options, inlineValue, args, ref index, negate, out var optionError))
                        return Fail(optionError, out result, out error);
                    continue;
                }

                return Fail($"Unknown option '{token}'.", out result, out error);
            }

            if (token.Equals("full", StringComparison.OrdinalIgnoreCase))
            {
                full = true;
                continue;
            }
            inputs.Add(token);
        }

        if (showHelp)
        {
            result = new TransformCommandLineResult(inputs, output, () => [], verbose, true);
            return true;
        }
        if (inputs.Count == 0)
            return Fail("An input assembly is required.", out result, out error);

        try
        {
            inputs = inputs.SelectMany(ExpandInput).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (IOException exception)
        {
            return Fail($"Could not enumerate input directory: {exception.Message}", out result, out error);
        }

        if (inputs.Count == 0)
            return Fail("The input directory contains no .dll or .exe assemblies.", out result, out error);

        if (full)
            foreach (var command in commands.Where(command => command.Metadata.IncludeInFull))
            {
                selected.Add(command);
                if (command.OptionsType is not null && !optionValues.ContainsKey(command))
                    optionValues.Add(command, Activator.CreateInstance(command.OptionsType)!);
            }

        if (selected.Count == 0 && IsFileDrop(args))
            foreach (var command in commands.Where(command => command.Metadata.AutoSelectOnDrop))
                selected.Add(command);

        if (selected.Count == 0)
            return Fail("Choose at least one transform (or use 'full').", out result, out error);

        result = new TransformCommandLineResult(inputs, output, CreateStages, verbose, false);
        return true;

        IReadOnlyList<ITransform> CreateStages() => selected.OrderBy(command => command.Metadata.Order).ThenBy(command => command.Metadata.Name, StringComparer.Ordinal).Select(command => command.Create(optionValues.GetValueOrDefault(command))).ToArray();

        static IEnumerable<string> ExpandInput(string input)
        {
            if (!Directory.Exists(input))
                return [input];

            return Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Where(IsAssemblyPath).Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith(".Fishy", StringComparison.OrdinalIgnoreCase)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        static bool IsFileDrop(string[] arguments) => arguments.Length > 0 && arguments.All(argument => !argument.StartsWith('-') && File.Exists(argument));
    }

    public static string GetUsage(string tool)
    {
        var commands = Discover(tool);
        var builder = new StringBuilder().Append(GetSynopsis(tool)).Append("\n\n").AppendLine("Transforms:");

        foreach (var command in commands.OrderBy(command => command.Metadata.Order).ThenBy(command => command.Metadata.Name, StringComparer.Ordinal))
        {
            builder.Append("  --").Append(command.Metadata.Name);
            if (command.Metadata.Aliases.Length > 0)
                builder.Append("  (aliases: ").AppendJoin(", ", command.Metadata.Aliases.Select(alias => "--" + alias)).Append(')');
            builder.AppendLine();
            foreach (var property in command.OptionProperties)
            {
                var optionName = command.OptionName(property);
                var valueHint = " <" + TypeHint(property.PropertyType) + ">";
                var defaultValue = property.GetValue(Activator.CreateInstance(command.OptionsType!)!);
                builder.Append("      --").Append(optionName).Append(valueHint).Append("  (default: ").Append(FormatDefault(defaultValue)).AppendLine(")");
                if (property.PropertyType == typeof(bool))
                    builder.Append("      --no-").Append(optionName).AppendLine();
            }
        }

        builder.AppendLine("  -v, --verbose  Show per-transform progress (off by default).");
        builder.AppendLine("  -h, --help  Show this help.");
        return builder.ToString();
    }

    public static string GetSynopsis(string tool)
    {
        var title = tool == "obfuscator" ? "Fishy Obfuscator" : "Fishy Deobfuscator";
        return $"Usage: {title} <input-or-directory> [<input-or-directory> ...] [full] [-o|--output <path-or-directory>] [-v|--verbose] [transform options]\nRun with -h or --help to list transforms, option types, and defaults.";
    }

    private static bool IsAssemblyPath(string path) => Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);

    private static Command[] Discover(string tool) =>
        typeof(ITransform)
            .Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ITransform).IsAssignableFrom(type))
            .Select(type => (Type: type, Metadata: type.GetCustomAttribute<TransformCommandAttribute>()))
            .Where(item => item.Metadata?.Tool.Equals(tool, StringComparison.OrdinalIgnoreCase) == true)
            .Select(item => new Command(item.Type, item.Metadata!))
            .ToArray();

    private static object GetOptions(IDictionary<Command, object> values, Command command)
    {
        if (command.OptionsType is null)
            throw new InvalidOperationException($"Transform '{command.Metadata.Name}' does not accept options.");
        if (!values.TryGetValue(command, out var options))
            values.Add(command, options = Activator.CreateInstance(command.OptionsType)!);
        return options;
    }

    private static bool TryFindOption(IEnumerable<Command> commands, string name, out Command command, out PropertyInfo property, out bool negate)
    {
        foreach (var candidate in commands)
        foreach (var option in candidate.OptionProperties)
        {
            var optionName = "--" + candidate.OptionName(option);
            if (name.Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                command = candidate;
                property = option;
                negate = false;
                return true;
            }
            if (option.PropertyType == typeof(bool) && name.Equals("--no-" + optionName[2..], StringComparison.OrdinalIgnoreCase))
            {
                command = candidate;
                property = option;
                negate = true;
                return true;
            }
        }
        command = null!;
        property = null!;
        negate = false;
        return false;
    }

    private static bool TrySetOption(PropertyInfo property, object options, string? inlineValue, string[] args, ref int index, bool negate, out string error)
    {
        error = string.Empty;
        if (property.PropertyType == typeof(bool))
        {
            var value = !negate;
            var boolText = inlineValue;
            if (boolText is null && index + 1 < args.Length && bool.TryParse(args[index + 1], out _))
                boolText = args[++index];
            if (boolText is not null && !bool.TryParse(boolText, out value))
            {
                error = $"Option for '{property.Name}' must be true or false.";
                return false;
            }
            property.SetValue(options, value);
            return true;
        }
        if (negate)
        {
            error = $"Option '{property.Name}' cannot be negated.";
            return false;
        }
        var text = inlineValue;
        if (text is null)
        {
            if (++index >= args.Length)
            {
                error = $"Missing value for '{property.Name}'.";
                return false;
            }
            text = args[index];
        }
        try
        {
            var type = property.PropertyType;
            var value =
                type.IsEnum ? Enum.Parse(type, text, true)
                : IsStringList(type) ? text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            property.SetValue(options, value);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            error = $"Invalid value '{text}' for '{property.Name}'.";
            return false;
        }
    }

    private static (string Name, string? Value) SplitOption(string token)
    {
        var separator = token.IndexOf('=');
        return separator < 0 ? (token, null) : (token[..separator], token[(separator + 1)..]);
    }

    private static bool IsStringList(Type type) => type == typeof(string[]) || typeof(IReadOnlyList<string>).IsAssignableFrom(type) || typeof(IList<string>).IsAssignableFrom(type);

    private static bool IsSupportedOption(Type type) =>
        type == typeof(bool)
        || type == typeof(string)
        || type.IsEnum
        || IsStringList(type)
        || Type.GetTypeCode(type) is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    private static string TypeHint(Type type)
    {
        if (type == typeof(bool))
            return "bool";
        if (type.IsEnum)
            return string.Join('|', Enum.GetNames(type));
        if (IsStringList(type))
            return "comma-separated strings";
        return Type.GetTypeCode(type) switch
        {
            TypeCode.SByte => "sbyte",
            TypeCode.Byte => "byte",
            TypeCode.Int16 => "short",
            TypeCode.UInt16 => "ushort",
            TypeCode.Int32 => "int",
            TypeCode.UInt32 => "uint",
            TypeCode.Int64 => "long",
            TypeCode.UInt64 => "ulong",
            TypeCode.Single => "float",
            TypeCode.Double => "double",
            TypeCode.Decimal => "decimal",
            _ => "string",
        };
    }

    private static string FormatDefault(object? value) =>
        value switch
        {
            null => "none",
            string text when text.Length == 0 => "empty",
            IEnumerable<string> strings => "[" + string.Join(", ", strings.Select(Quote)) + "]",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "none",
        };

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static bool Fail(string message, out TransformCommandLineResult result, out string error)
    {
        result = null!;
        error = message;
        return false;
    }

    private sealed class Command
    {
        private readonly ConstructorInfo _constructor;

        public Command(Type transformType, TransformCommandAttribute metadata)
        {
            Metadata = metadata;
            _constructor = transformType.GetConstructors().OrderByDescending(constructor => constructor.GetParameters().Length).First();
            OptionsType = _constructor.GetParameters().FirstOrDefault()?.ParameterType;
            OptionProperties = OptionsType?.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(property => property.CanWrite && property.SetMethod?.IsPublic == true && IsSupportedOption(property.PropertyType)).ToArray() ?? [];
        }

        public TransformCommandAttribute Metadata { get; }
        public Type? OptionsType { get; }
        public PropertyInfo[] OptionProperties { get; }

        public bool Matches(string name) => name.Equals("--" + Metadata.Name, StringComparison.OrdinalIgnoreCase) || Metadata.Aliases.Any(alias => name.Equals("--" + alias, StringComparison.OrdinalIgnoreCase));

        public string OptionName(PropertyInfo property)
        {
            var option = ToKebabCase(property.Name);
            var commandPrefix = Metadata.Name + "-";
            if (option.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase))
                option = option[commandPrefix.Length..];
            return Metadata.Name + "-" + option;
        }

        public ITransform Create(object? options) => (ITransform)(_constructor.GetParameters().Length == 0 ? _constructor.Invoke([]) : _constructor.Invoke([options ?? Activator.CreateInstance(OptionsType!)]));

        private static string ToKebabCase(string value)
        {
            var builder = new StringBuilder();
            for (var index = 0; index < value.Length; index++)
            {
                if (char.IsUpper(value[index]) && index > 0)
                    builder.Append('-');
                builder.Append(char.ToLowerInvariant(value[index]));
            }
            return builder.ToString();
        }
    }
}
