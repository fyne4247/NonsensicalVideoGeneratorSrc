using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Copies the AVFormatContext pointer FFmpeg wrote back into DecodeContext._formatContext
// after avformat_open_input. FFmpeg 7 frees a caller-supplied context on failure and
// nulls only that pointer. The field still holds the freed address, and Dispose
// passes it to avformat_free_context (SIGSEGV in av_opt_free).
//
// On success the pointer is unchanged, so the store is a no-op for a normal open.
// No other method is edited.

if (args.Length < 5)
{
    Console.Error.WriteLine("usage: PatchOpenInput <input.dll> <input.pdb> <output.dll> <output.pdb> <dependency.dll>...");
    return 2;
}

string inputDll = Path.GetFullPath(args[0]);
string inputPdb = Path.GetFullPath(args[1]);
string outputDll = Path.GetFullPath(args[2]);
string outputPdb = Path.GetFullPath(args[3]);

if (!File.Exists(inputDll) || !File.Exists(inputPdb))
{
    Console.Error.WriteLine("missing input");
    return 2;
}

Directory.CreateDirectory(Path.GetDirectoryName(outputDll)!);
Directory.CreateDirectory(Path.GetDirectoryName(outputPdb)!);

uint timestamp = ReadPeTimestamp(inputDll);
var resolver = new NameResolver(args.Skip(4).Select(Path.GetFullPath));
var read = new ReaderParameters
{
    ReadSymbols = true,
    SymbolReaderProvider = new DefaultSymbolReaderProvider(throwIfNoSymbol: true),
    AssemblyResolver = resolver,
};
// Cecil loads the pdb that sits beside the dll. Stage the pair so the names match.
string stage = Directory.CreateTempSubdirectory("nvg-vp-stage-").FullName;
try
{
    string stagedDll = Path.Combine(stage, "MonoGame.Extended.VideoPlayback.dll");
    string stagedPdb = Path.Combine(stage, "MonoGame.Extended.VideoPlayback.pdb");
    File.Copy(inputDll, stagedDll, overwrite: true);
    File.Copy(inputPdb, stagedPdb, overwrite: true);

    using var assembly = AssemblyDefinition.ReadAssembly(stagedDll, read);
    var before = Fingerprint(assembly, skipCtor: true);
    Patch(assembly);

    string writtenDll = Path.Combine(stage, "out.dll");
    string writtenPdb = Path.Combine(stage, "out.pdb");
    var write = new WriterParameters
    {
        WriteSymbols = true,
        SymbolWriterProvider = new PortablePdbWriterProvider(),
        Timestamp = timestamp,
    };
    assembly.Write(writtenDll, write);
    NormalizePdbPath(writtenDll);

    using var check = AssemblyDefinition.ReadAssembly(writtenDll, new ReaderParameters
    {
        ReadSymbols = true,
        SymbolReaderProvider = new DefaultSymbolReaderProvider(throwIfNoSymbol: true),
        AssemblyResolver = resolver,
    });
    var after = Fingerprint(check, skipCtor: true);
    if (before != after)
    {
        Console.Error.WriteLine("patch changed a method other than DecodeContext..ctor");
        WriteDiff(before, after);
        return 1;
    }
    if (!CtorHasSync(check))
    {
        Console.Error.WriteLine("patched constructor does not store the open_input pointer back");
        return 1;
    }
    if (ReadPeTimestamp(writtenDll) != timestamp)
    {
        Console.Error.WriteLine("PE timestamp changed");
        return 1;
    }

    File.Copy(writtenDll, outputDll, overwrite: true);
    File.Copy(writtenPdb, outputPdb, overwrite: true);
}
finally
{
    resolver.Dispose();
    try { Directory.Delete(stage, recursive: true); } catch { }
}

Console.WriteLine("input-dll  " + Sha256(inputDll));
Console.WriteLine("input-pdb  " + Sha256(inputPdb));
Console.WriteLine("output-dll " + Sha256(outputDll));
Console.WriteLine("output-pdb " + Sha256(outputPdb));
return 0;

static void Patch(AssemblyDefinition assembly)
{
    var module = assembly.MainModule;
    var decode = module.Types.Single(t => t.FullName == "MonoGame.Extended.VideoPlayback.DecodeContext");
    var ctor = decode.Methods.Single(m => m.IsConstructor && !m.IsStatic && m.HasBody);
    var instructions = ctor.Body.Instructions;
    int openAt = -1;
    for (int i = 0; i < instructions.Count; i++)
    {
        if (IsCall(instructions[i], "avformat_open_input"))
        {
            if (openAt >= 0)
                throw new InvalidOperationException("more than one avformat_open_input in the constructor");
            openAt = i;
        }
    }
    if (openAt < 0)
        throw new InvalidOperationException("avformat_open_input is missing from the constructor");
    if (!IsExpectedPrefix(instructions, openAt))
        throw new InvalidOperationException("constructor prefix does not match the shipped DecodeContext");

    Instruction open = instructions[openAt];
    FieldReference field = (FieldReference)instructions[openAt - 8].Operand;
    if (field.Name != "_formatContext")
        throw new InvalidOperationException("expected _formatContext store before open");

    var error = new VariableDefinition(module.TypeSystem.Int32);
    ctor.Body.Variables.Add(error);
    if (error.Index != 1)
        throw new InvalidOperationException("error local index is " + error.Index);

    var il = ctor.Body.GetILProcessor();
    Instruction storeError = il.Create(OpCodes.Stloc, error);
    Instruction loadThis = il.Create(OpCodes.Ldarg_0);
    Instruction loadFormat = il.Create(OpCodes.Ldloc_0);
    Instruction storeField = il.Create(OpCodes.Stfld, field);
    Instruction reloadError = il.Create(OpCodes.Ldloc, error);
    il.InsertAfter(open, storeError);
    il.InsertAfter(storeError, loadThis);
    il.InsertAfter(loadThis, loadFormat);
    il.InsertAfter(loadFormat, storeField);
    il.InsertAfter(storeField, reloadError);
}

static bool IsExpectedPrefix(Mono.Collections.Generic.Collection<Instruction> instructions, int openAt)
{
    // call avformat_alloc_context
    // stloc.0
    // ldarg.0
    // ldloc.0
    // stfld _formatContext
    // ldloca.s V_0
    // conv.u
    // ldarg.1
    // ldc.i4.0
    // conv.u
    // ldc.i4.0
    // conv.u
    // call avformat_open_input
    if (openAt < 12)
        return false;
    Instruction[] got = instructions.Skip(openAt - 12).Take(13).ToArray();
    if (!IsCall(got[0], "avformat_alloc_context"))
        return false;
    if (got[1].OpCode != OpCodes.Stloc_0)
        return false;
    if (got[2].OpCode != OpCodes.Ldarg_0 || got[3].OpCode != OpCodes.Ldloc_0)
        return false;
    if (got[4].OpCode != OpCodes.Stfld || got[4].Operand is not FieldReference field || field.Name != "_formatContext")
        return false;
    if (got[5].OpCode != OpCodes.Ldloca_S || got[5].Operand is not VariableDefinition local || local.Index != 0)
        return false;
    if (got[6].OpCode != OpCodes.Conv_U || got[7].OpCode != OpCodes.Ldarg_1)
        return false;
    if (got[8].OpCode != OpCodes.Ldc_I4_0 || got[9].OpCode != OpCodes.Conv_U)
        return false;
    if (got[10].OpCode != OpCodes.Ldc_I4_0 || got[11].OpCode != OpCodes.Conv_U)
        return false;
    return IsCall(got[12], "avformat_open_input");
}

static bool CtorHasSync(AssemblyDefinition assembly)
{
    var decode = assembly.MainModule.Types.Single(t => t.FullName == "MonoGame.Extended.VideoPlayback.DecodeContext");
    var ctor = decode.Methods.Single(m => m.IsConstructor && !m.IsStatic);
    var ins = ctor.Body.Instructions;
    for (int i = 0; i < ins.Count; i++)
    {
        if (!IsCall(ins[i], "avformat_open_input"))
            continue;
        if (i + 5 >= ins.Count)
            return false;
        if (ins[i + 1].OpCode != OpCodes.Stloc_1 && ins[i + 1].OpCode != OpCodes.Stloc)
            return false;
        if (ins[i + 2].OpCode != OpCodes.Ldarg_0 || ins[i + 3].OpCode != OpCodes.Ldloc_0)
            return false;
        if (ins[i + 4].OpCode != OpCodes.Stfld || ins[i + 4].Operand is not FieldReference field || field.Name != "_formatContext")
            return false;
        if (ins[i + 5].OpCode != OpCodes.Ldloc_1 && ins[i + 5].OpCode != OpCodes.Ldloc)
            return false;
        // find_stream_info must still consume the local pointer, not a second open.
        bool sawInfo = ins.Skip(i + 6).Any(x => IsCall(x, "avformat_find_stream_info"));
        bool sawSecondOpen = ins.Skip(i + 1).Any(x => IsCall(x, "avformat_open_input"));
        return sawInfo && !sawSecondOpen;
    }
    return false;
}

static bool IsCall(Instruction instruction, string name)
{
    return instruction.OpCode == OpCodes.Call
        && instruction.Operand is MethodReference method
        && method.Name == name;
}

static string Fingerprint(AssemblyDefinition assembly, bool skipCtor)
{
    var builder = new StringBuilder();
    foreach (var type in AllTypes(assembly.MainModule.Types).OrderBy(t => t.FullName, StringComparer.Ordinal))
    {
        foreach (var method in type.Methods.OrderBy(m => m.FullName, StringComparer.Ordinal).ThenBy(m => m.Parameters.Count))
        {
            if (skipCtor && type.FullName == "MonoGame.Extended.VideoPlayback.DecodeContext" && method.IsConstructor && !method.IsStatic)
                continue;
            builder.Append(method.FullName).Append('\n');
            if (!method.HasBody)
                continue;
            foreach (var instruction in method.Body.Instructions)
                builder.Append(instruction.OpCode.Name).Append(' ').Append(OperandKey(instruction)).Append('\n');
            foreach (var handler in method.Body.ExceptionHandlers)
                builder.Append("eh ").Append(handler.HandlerType).Append('\n');
        }
    }
    return builder.ToString();
}

static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types)
    {
        yield return type;
        foreach (var nested in AllTypes(type.NestedTypes))
            yield return nested;
    }
}

static string OperandKey(Instruction instruction)
{
    return instruction.Operand switch
    {
        null => "",
        MethodReference method => method.FullName,
        FieldReference field => field.FullName,
        VariableDefinition variable => "v" + variable.Index,
        ParameterDefinition parameter => "p" + parameter.Index,
        Instruction target => "br",
        string text => text,
        _ => instruction.Operand.ToString() ?? "",
    };
}

static void WriteDiff(string before, string after)
{
    string[] left = before.Split('\n');
    string[] right = after.Split('\n');
    int shown = 0;
    int n = Math.Max(left.Length, right.Length);
    for (int i = 0; i < n && shown < 40; i++)
    {
        string a = i < left.Length ? left[i] : "<missing>";
        string b = i < right.Length ? right[i] : "<missing>";
        if (a == b)
            continue;
        Console.Error.WriteLine("- " + a);
        Console.Error.WriteLine("+ " + b);
        shown++;
    }
}

static void NormalizePdbPath(string dllPath)
{
    byte[] data = File.ReadAllBytes(dllPath);
    int pe = BitConverter.ToInt32(data, 0x3C);
    ushort sectionCount = BitConverter.ToUInt16(data, pe + 6);
    ushort optionalSize = BitConverter.ToUInt16(data, pe + 20);
    int optional = pe + 24;
    int debugRva = BitConverter.ToInt32(data, optional + 112 + 6 * 8);
    int debugSize = BitConverter.ToInt32(data, optional + 112 + 6 * 8 + 4);
    int sectionTable = optional + optionalSize;
    int debugOff = RvaToOffset(data, sectionTable, sectionCount, debugRva);
    int count = debugSize / 28;
    var entries = new List<DebugEntry>();
    for (int i = 0; i < count; i++)
    {
        int at = debugOff + i * 28;
        entries.Add(new DebugEntry(
            BitConverter.ToUInt32(data, at),
            BitConverter.ToUInt32(data, at + 4),
            BitConverter.ToUInt16(data, at + 8),
            BitConverter.ToUInt16(data, at + 10),
            BitConverter.ToUInt32(data, at + 12),
            BitConverter.ToUInt32(data, at + 16),
            BitConverter.ToUInt32(data, at + 20),
            BitConverter.ToUInt32(data, at + 24)));
    }

    const string pdbName = "MonoGame.Extended.VideoPlayback.pdb";
    int origin = int.MaxValue;
    int end = 0;
    foreach (DebugEntry entry in entries)
    {
        if (entry.Size == 0)
            continue;
        origin = Math.Min(origin, (int)entry.Pointer);
        end = Math.Max(end, (int)entry.Pointer + (int)entry.Size);
    }

    var blobs = new List<byte[]>();
    foreach (DebugEntry entry in entries)
    {
        if (entry.Size == 0)
        {
            blobs.Add(Array.Empty<byte>());
            continue;
        }
        byte[] blob = data[(int)entry.Pointer..((int)entry.Pointer + (int)entry.Size)];
        if (entry.Type == 2)
            blob = RewriteCodeViewPath(blob, pdbName);
        blobs.Add(blob);
    }

    int cursor = origin;
    for (int i = 0; i < entries.Count; i++)
    {
        DebugEntry entry = entries[i];
        byte[] blob = blobs[i];
        uint pointer = entry.Size == 0 ? entry.Pointer : (uint)cursor;
        uint address = entry.Size == 0 ? entry.Address : (uint)(entry.Address - entry.Pointer + pointer);
        if (blob.Length > 0)
        {
            blob.CopyTo(data, cursor);
            cursor += blob.Length;
        }
        entries[i] = entry with { Size = (uint)blob.Length, Pointer = pointer, Address = address };
    }
    for (int i = cursor; i < end; i++)
        data[i] = 0;
    for (int i = 0; i < entries.Count; i++)
    {
        DebugEntry entry = entries[i];
        int at = debugOff + i * 28;
        BitConverter.TryWriteBytes(data.AsSpan(at + 16), entry.Size);
        BitConverter.TryWriteBytes(data.AsSpan(at + 20), entry.Address);
        BitConverter.TryWriteBytes(data.AsSpan(at + 24), entry.Pointer);
    }
    File.WriteAllBytes(dllPath, data);
}

static byte[] RewriteCodeViewPath(byte[] blob, string pdbName)
{
    if (blob.Length < 24 || blob[0] != (byte)'R' || blob[1] != (byte)'S' || blob[2] != (byte)'D' || blob[3] != (byte)'S')
        throw new InvalidOperationException("CodeView entry is not RSDS");
    byte[] name = Encoding.UTF8.GetBytes(pdbName);
    byte[] rewritten = new byte[24 + name.Length + 1];
    Buffer.BlockCopy(blob, 0, rewritten, 0, 24);
    Buffer.BlockCopy(name, 0, rewritten, 24, name.Length);
    return rewritten;
}

static int RvaToOffset(byte[] data, int sectionTable, int sectionCount, int rva)
{
    for (int i = 0; i < sectionCount; i++)
    {
        int at = sectionTable + i * 40;
        uint virtualSize = BitConverter.ToUInt32(data, at + 8);
        uint virtualAddress = BitConverter.ToUInt32(data, at + 12);
        uint rawSize = BitConverter.ToUInt32(data, at + 16);
        uint rawPointer = BitConverter.ToUInt32(data, at + 20);
        uint span = Math.Max(virtualSize, rawSize);
        if ((uint)rva >= virtualAddress && (uint)rva < virtualAddress + span)
            return (int)(rawPointer + ((uint)rva - virtualAddress));
    }
    throw new InvalidOperationException("RVA " + rva.ToString("X") + " is outside the image");
}

static uint ReadPeTimestamp(string path)
{
    using var stream = File.OpenRead(path);
    using var reader = new BinaryReader(stream);
    stream.Position = 0x3C;
    int pe = reader.ReadInt32();
    stream.Position = pe + 8;
    return reader.ReadUInt32();
}

static string Sha256(string path)
{
    using var sha = SHA256.Create();
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
}

sealed class NameResolver : IAssemblyResolver
{
    readonly Dictionary<string, string> files;
    readonly Dictionary<string, AssemblyDefinition> loaded = new(StringComparer.OrdinalIgnoreCase);

    public NameResolver(IEnumerable<string> paths)
    {
        files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
            files[Path.GetFileNameWithoutExtension(path)] = path;
    }

    public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters());

    public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        if (loaded.TryGetValue(name.Name, out AssemblyDefinition? cached))
            return cached;
        if (!files.TryGetValue(name.Name, out string? path))
            throw new AssemblyResolutionException(name);
        var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = this });
        loaded[name.Name] = assembly;
        return assembly;
    }

    public void Dispose()
    {
        foreach (AssemblyDefinition assembly in loaded.Values)
            assembly.Dispose();
        loaded.Clear();
    }
}

readonly record struct DebugEntry(uint Characteristics, uint Stamp, ushort Major, ushort Minor, uint Type, uint Size, uint Address, uint Pointer);
