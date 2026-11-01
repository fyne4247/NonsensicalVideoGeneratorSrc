namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// A program plus its argument vector. Each entry is one argv element.
    /// </summary>
    public readonly struct NativeCommand
    {
        public NativeCommand(string fileName, params string[] arguments)
        {
            FileName = fileName;
            Arguments = arguments ?? System.Array.Empty<string>();
        }

        public string FileName { get; }
        public string[] Arguments { get; }
    }
}
