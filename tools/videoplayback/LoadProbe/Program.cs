using System.Reflection;
using System.Runtime.InteropServices;
using MonoGame.Extended.Framework.Media;
using MonoGame.Extended.VideoPlayback;

// Direct VideoHelper.LoadFromFile, with no MediaGuard. One process, one file.
// bad: a managed exception is success (exit 0). A native crash is not.
// good: open, decode one frame, dispose.

if (args.Length != 2 || args[0] is not ("bad" or "good"))
{
    Console.Error.WriteLine("usage: LoadProbe bad|good <file>");
    return 2;
}

string mode = args[0];
string path = args[1];
StageNativeLibraries();

Video? video = null;
int code = 0;
try
{
    video = VideoHelper.LoadFromFile(path);
    if (mode == "bad")
    {
        Console.Error.WriteLine("opened unexpectedly");
        code = 2;
    }
    else
    {
        int width = video.Width;
        int height = video.Height;
        TimeSpan duration = video.Duration;
        bool decoded = DecodedFrame(video);
        Console.WriteLine("opened duration=" + duration + " size=" + width + "x" + height + " decoded=" + decoded);
        if (width <= 0 || height <= 0 || duration <= TimeSpan.Zero || !decoded)
            code = 3;
    }
}
catch (Exception ex)
{
    Exception root = ex;
    while (root.InnerException != null)
        root = root.InnerException;
    if (mode == "bad")
        Console.WriteLine("managed " + root.GetType().FullName);
    else
    {
        Console.Error.WriteLine(root.GetType().FullName + ": " + root.Message);
        code = 4;
    }
}
finally
{
    if (video is IDisposable disposable)
    {
        disposable.Dispose();
        if (mode == "good" && code == 0)
            Console.WriteLine("closed");
    }
}

return code;

static void StageNativeLibraries()
{
    string? dir = Environment.GetEnvironmentVariable("NVG_FFMPEG_LIB");
    if (string.IsNullOrWhiteSpace(dir))
        return;
    string nextTo = AppContext.BaseDirectory;
    foreach (var (stem, major) in new (string Stem, int Major)[]
    {
        ("avutil", 59),
        ("swresample", 5),
        ("swscale", 8),
        ("avcodec", 61),
        ("avformat", 61),
        ("avfilter", 10),
        ("avdevice", 61),
        ("postproc", 58),
    })
    {
        string file = Path.Combine(dir, LibraryFile(stem, major));
        if (!File.Exists(file))
            continue;
        NativeLibrary.Load(file);
        string link = Path.Combine(nextTo, LibraryFile(stem, major));
        if (!File.Exists(link))
            File.CreateSymbolicLink(link, file);
    }
}

static string LibraryFile(string stem, int major)
{
    if (OperatingSystem.IsWindows())
        return stem + "-" + major + ".dll";
    if (OperatingSystem.IsMacOS())
        return "lib" + stem + "." + major + ".dylib";
    return "lib" + stem + ".so." + major;
}

static unsafe bool DecodedFrame(Video video)
{
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    object? context = video.GetType().GetProperty("DecodeContext", flags)?.GetValue(video);
    if (context == null)
        return false;
    MethodInfo? read = context.GetType().GetMethod("ReadVideoUntilPlaybackIsAfter", flags);
    if (read == null)
        return false;
    read.Invoke(context, new object[] { 0d });
    FieldInfo? field = context.GetType().GetField("_currentVideoFrame", flags);
    object? value = field?.GetValue(context);
    if (value is not Pointer)
        return false;
    return Pointer.Unbox(value) != null;
}
