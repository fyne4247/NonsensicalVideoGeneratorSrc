using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

[Collection("nvg")]
public sealed class AtomicDownloadTests
{
    static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".webm", ".mov", ".avi", ".mkv", ".wmv" };

    readonly Sandbox sandbox;

    public AtomicDownloadTests(Sandbox sandbox)
    {
        this.sandbox = sandbox;
    }

    [Fact]
    public void Success_publishes_from_a_same_directory_partial_the_scan_skips()
    {
        using var server = new Loopback();
        byte[] body = Encoding.UTF8.GetBytes("good-media-bytes");
        server.Respond = (stream, _) => WriteHttp(stream, 200, body, body.Length);
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "clip.mp4");
        string? partial = null;
        AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(server.Url("clip.mp4"), finalPath, probe: path =>
        {
            partial = path;
            string partialName = Path.GetFileName(path);
            Assert.Equal(dir, Path.GetDirectoryName(path));
            Assert.EndsWith(AtomicDownload.PartialMarker, partialName, StringComparison.Ordinal);
            Assert.True(partialName.Length <= 48);
            Assert.DoesNotContain("clip", partialName, StringComparison.OrdinalIgnoreCase);
            Assert.False(AtomicDownload.ListedByLibraryScan(path, VideoExtensions));
            Assert.True(new FileInfo(path).Length > 0);
            return true;
        }));

        Assert.True(result.Published);
        Assert.Equal("ok", result.Detail);
        Assert.Equal(body, File.ReadAllBytes(finalPath));
        Assert.False(string.IsNullOrEmpty(partial));
        Assert.False(File.Exists(partial));
        AssertNoPartial(dir);
    }

    [Fact]
    public void Image_publish_does_not_probe()
    {
        using var server = new Loopback();
        byte[] body = Encoding.UTF8.GetBytes("png-bytes");
        server.Respond = (stream, _) => WriteHttp(stream, 200, body, body.Length);
        string finalPath = Path.Combine(NewDir(), "still.png");
        AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(server.Url("still.png"), finalPath, probe: _ =>
            throw new InvalidOperationException("probe")));
        Assert.True(result.Published);
        Assert.Equal(body, File.ReadAllBytes(finalPath));
    }

    [Fact]
    public void Http_404_leaves_no_final_and_keeps_a_valid_file()
    {
        using var server = new Loopback();
        server.Respond = (stream, _) => WriteHttp(stream, 404, Array.Empty<byte>(), 0);
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "clip.mp4");
        byte[] original = Seed(finalPath, 40);
        AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(server.Url("clip.mp4"), finalPath));
        Assert.False(result.Published);
        Assert.Contains("404", result.Detail, StringComparison.Ordinal);
        AssertSame(finalPath, original);
        AssertNoPartial(dir);
    }

    [Fact]
    public void Truncated_body_is_not_published()
    {
        using var server = new Loopback();
        server.Respond = (stream, _) =>
        {
            WriteHeaders(stream, 200, 100);
            stream.Write(new byte[10], 0, 10);
            stream.Flush();
        };
        AssertRejected(server, "truncated.mp4");
    }

    [Fact]
    public void Connection_dropped_mid_body_is_not_published()
    {
        using var server = new Loopback();
        server.Respond = (stream, client) =>
        {
            WriteHeaders(stream, 200, 100000);
            stream.Write(new byte[30], 0, 30);
            stream.Flush();
            client.Client.LingerState = new LingerOption(true, 0);
            client.Client.Close();
        };
        AssertRejected(server, "dropped.mp4");
    }

    [Fact]
    public void Wrong_length_is_not_published()
    {
        Assert.False(AtomicDownload.LengthAccepted(10, 100));
        Assert.False(AtomicDownload.LengthAccepted(0, 0));
        Assert.False(AtomicDownload.LengthAccepted(0, null));
        Assert.True(AtomicDownload.LengthAccepted(5, 5));
        Assert.True(AtomicDownload.LengthAccepted(5, null));

        using var server = new Loopback();
        server.Respond = (stream, _) =>
        {
            WriteHeaders(stream, 200, 80);
            stream.Write(Encoding.ASCII.GetBytes("short"), 0, 5);
            stream.Flush();
        };
        AssertRejected(server, "wrong-length.mp4");
    }

    [Fact]
    public void Hanging_server_times_out_without_a_final_file()
    {
        using var server = new Loopback();
        server.Respond = (stream, _) =>
        {
            try
            {
                while (stream.ReadByte() >= 0)
                {
                }
            }
            catch (Exception)
            {
            }
        };
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "hang.mp4");
        byte[] original = Seed(finalPath, 24);
        AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(
            server.Url("hang.mp4"),
            finalPath,
            overall: TimeSpan.FromSeconds(1),
            connect: TimeSpan.FromSeconds(1)));
        Assert.False(result.Published);
        Assert.Equal("timeout", result.Detail);
        AssertSame(finalPath, original);
        AssertNoPartial(dir);
    }

    [Fact]
    public async Task Cancel_leaves_no_final_and_keeps_a_valid_file()
    {
        using var server = new Loopback();
        server.Respond = (stream, _) =>
        {
            WriteHeaders(stream, 200, 5_000_000);
            byte[] block = new byte[1024];
            try
            {
                for (int i = 0; i < 4000; i++)
                {
                    stream.Write(block, 0, block.Length);
                    stream.Flush();
                    Thread.Sleep(20);
                }
            }
            catch (Exception)
            {
            }
        };
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "cancel.mp4");
        byte[] original = Seed(finalPath, 32);
        using var cancel = new CancellationTokenSource();
        Task<AtomicDownload.DownloadResult> task = Task.Run(() => AtomicDownload.Fetch(Request(
            server.Url("cancel.mp4"),
            finalPath,
            cancel: cancel.Token,
            overall: TimeSpan.FromSeconds(30))));
        TestWait.Until(() => Directory.GetFiles(dir, "*" + AtomicDownload.PartialMarker).Length > 0);
        Assert.True(File.Exists(finalPath));
        AtomicDownload.SweepPartials(dir);
        Assert.NotEmpty(Directory.GetFiles(dir, "*" + AtomicDownload.PartialMarker));
        cancel.Cancel();
        AtomicDownload.DownloadResult result = await task;
        Assert.False(result.Published);
        Assert.Equal("cancelled", result.Detail);
        AssertSame(finalPath, original);
        AssertNoPartial(dir);
    }

    [Fact]
    public async Task Exit_flag_cancels_the_download()
    {
        using var server = new Loopback();
        server.Respond = (stream, _) =>
        {
            WriteHeaders(stream, 200, 5_000_000);
            byte[] block = new byte[256];
            try
            {
                for (int i = 0; i < 4000; i++)
                {
                    stream.Write(block, 0, block.Length);
                    stream.Flush();
                    Thread.Sleep(30);
                }
            }
            catch (Exception)
            {
            }
        };
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "exit.mp4");
        int stop = 0;
        Task<AtomicDownload.DownloadResult> task = Task.Run(() => AtomicDownload.Fetch(Request(
            server.Url("exit.mp4"),
            finalPath,
            stop: () => Volatile.Read(ref stop) == 1,
            overall: TimeSpan.FromSeconds(30))));
        TestWait.Until(() => Directory.GetFiles(dir, "*" + AtomicDownload.PartialMarker).Length > 0);
        Volatile.Write(ref stop, 1);
        AtomicDownload.DownloadResult result = await task;
        Assert.Equal("cancelled", result.Detail);
        Assert.False(File.Exists(finalPath));
        AssertNoPartial(dir);
    }

    [Fact]
    public void Valid_existing_file_is_kept_for_the_lua_already_exists_case()
    {
        string finalPath = Path.Combine(NewDir(), "kept.mp4");
        byte[] original = Seed(finalPath, 16);
        AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(
            "http://127.0.0.1:1/kept.mp4",
            finalPath,
            keep: true));
        Assert.True(result.AlreadyExisted);
        Assert.False(result.Published);
        Assert.Equal("already exists", result.Detail);
        AssertSame(finalPath, original);
    }

    [Fact]
    public void Different_bytes_publish_beside_the_existing_file()
    {
        using var server = new Loopback();
        byte[] body = Encoding.UTF8.GetBytes("replaced-bytes");
        server.Respond = (stream, _) => WriteHttp(stream, 200, body, body.Length);
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "replace.mp4");
        byte[] original = Seed(finalPath, 12);
        AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(server.Url("replace.mp4"), finalPath, keep: false));
        string second = Path.Combine(dir, "replace (2).mp4");
        Assert.True(result.Published);
        Assert.False(result.AlreadyExisted);
        Assert.Equal(second, result.FinalPath);
        Assert.Equal(body, File.ReadAllBytes(second));
        AssertSame(finalPath, original);
        AssertNoPartial(dir);

        byte[] thirdBody = Encoding.UTF8.GetBytes("third-bytes-here");
        server.Respond = (stream, _) => WriteHttp(stream, 200, thirdBody, thirdBody.Length);
        AtomicDownload.DownloadResult third = AtomicDownload.Fetch(Request(server.Url("replace.mp4"), finalPath, keep: false));
        string thirdPath = Path.Combine(dir, "replace (3).mp4");
        Assert.Equal(thirdPath, third.FinalPath);
        Assert.Equal(thirdBody, File.ReadAllBytes(thirdPath));
        AssertSame(finalPath, original);
        Assert.Equal(body, File.ReadAllBytes(second));
        AssertNoPartial(dir);
    }

    [Fact]
    public void Identical_bytes_report_existing_and_leave_the_file()
    {
        using var server = new Loopback();
        byte[] body = Encoding.UTF8.GetBytes("same-bytes");
        server.Respond = (stream, _) => WriteHttp(stream, 200, body, body.Length);
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "clip.mp4");
        File.WriteAllBytes(finalPath, body);
        AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(server.Url("clip.mp4"), finalPath, keep: false));
        Assert.True(result.AlreadyExisted);
        Assert.False(result.Published);
        Assert.Equal("already exists", result.Detail);
        Assert.Equal(finalPath, result.FinalPath);
        Assert.Equal(body, File.ReadAllBytes(finalPath));
        Assert.False(File.Exists(Path.Combine(dir, "clip (2).mp4")));
        AssertNoPartial(dir);

        byte[] other = Encoding.UTF8.GetBytes("other-bytes");
        File.WriteAllBytes(finalPath, other);
        string second = Path.Combine(dir, "clip (2).mp4");
        File.WriteAllBytes(second, body);
        AtomicDownload.DownloadResult numbered = AtomicDownload.Fetch(Request(server.Url("clip.mp4"), finalPath, keep: false));
        Assert.True(numbered.AlreadyExisted);
        Assert.Equal(second, numbered.FinalPath);
        Assert.False(File.Exists(Path.Combine(dir, "clip (3).mp4")));
        Assert.Equal(other, File.ReadAllBytes(finalPath));
        Assert.Equal(body, File.ReadAllBytes(second));
        AssertNoPartial(dir);
    }

    [Fact]
    public void Publish_time_collision_inside_the_probe_is_not_overwritten()
    {
        using var server = new Loopback();
        byte[] body = Encoding.UTF8.GetBytes("downloaded-bytes");
        byte[] planted = Encoding.UTF8.GetBytes("planted-during-probe");
        server.Respond = (stream, _) => WriteHttp(stream, 200, body, body.Length);
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "clip.mp4");
        AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(server.Url("clip.mp4"), finalPath, keep: false, probe: path =>
        {
            Assert.False(File.Exists(finalPath));
            File.WriteAllBytes(finalPath, planted);
            return true;
        }));
        string second = Path.Combine(dir, "clip (2).mp4");
        Assert.True(result.Published);
        Assert.Equal(second, result.FinalPath);
        Assert.Equal(planted, File.ReadAllBytes(finalPath));
        Assert.Equal(body, File.ReadAllBytes(second));
        AssertNoPartial(dir);
    }

    [Fact]
    public void Zero_byte_final_is_replaced_only_after_a_good_download()
    {
        using var server = new Loopback();
        byte[] body = Encoding.UTF8.GetBytes("healed-bytes");
        server.Respond = (stream, _) => WriteHttp(stream, 200, body, body.Length);
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "empty.mp4");
        File.WriteAllBytes(finalPath, Array.Empty<byte>());
        Assert.False(AtomicDownload.ExistsAsValidFile(finalPath));
        Assert.False(AtomicDownload.IsSelectable(finalPath));
        AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(server.Url("empty.mp4"), finalPath, keep: true));
        Assert.True(result.Published);
        Assert.Equal(body, File.ReadAllBytes(finalPath));
        AssertNoPartial(dir);
    }

    [Fact]
    public void Probe_failure_does_not_replace_a_valid_file()
    {
        using var server = new Loopback();
        byte[] body = Encoding.UTF8.GetBytes("not-a-video");
        server.Respond = (stream, _) => WriteHttp(stream, 200, body, body.Length);
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "clip.mp4");
        byte[] original = Seed(finalPath, 20);
        AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(
            server.Url("clip.mp4"),
            finalPath,
            probe: _ => false));
        Assert.False(result.Published);
        Assert.Equal("probe", result.Detail);
        AssertSame(finalPath, original);
        AssertNoPartial(dir);
    }

    [Fact]
    public void Killed_process_partials_are_hidden_and_swept_without_touching_real_files()
    {
        string dir = NewDir();
        string valid = Path.Combine(dir, "kept.mp4");
        string zero = Path.Combine(dir, "zero.mp4");
        byte[] original = Seed(valid, 48);
        File.WriteAllBytes(zero, Array.Empty<byte>());
        string partial = Path.Combine(dir, "kept.mp4." + Guid.NewGuid().ToString("N") + AtomicDownload.PartialMarker);
        File.WriteAllBytes(partial, new byte[] { 1, 2, 3 });
        string staging = Path.Combine(dir, AtomicDownload.PartialMarker + "-dead");
        Directory.CreateDirectory(staging);
        string stagedClip = Path.Combine(staging, "title.mp4");
        File.WriteAllBytes(stagedClip, new byte[] { 4, 5, 6 });

        Assert.False(AtomicDownload.ListedByLibraryScan(partial, VideoExtensions));
        Assert.False(AtomicDownload.ListedByLibraryScan(stagedClip, VideoExtensions));
        Assert.True(AtomicDownload.ListedByLibraryScan(valid, VideoExtensions));
        Assert.True(AtomicDownload.ListedByLibraryScan(zero, VideoExtensions));
        Assert.True(AtomicDownload.IsSelectable(valid));
        Assert.False(AtomicDownload.IsSelectable(zero));
        Assert.False(AtomicDownload.IsSelectable(partial));
        string[] candidates = { zero, partial, Path.Combine(dir, "missing.mp4") };
        Assert.Equal("", candidates.FirstOrDefault(AtomicDownload.IsSelectable) ?? "");

        int removed = AtomicDownload.SweepPartials(dir);
        Assert.True(removed >= 2);
        Assert.False(File.Exists(partial));
        Assert.False(Directory.Exists(staging));
        AssertSame(valid, original);
        Assert.True(File.Exists(zero));
        Assert.Equal(0, new FileInfo(zero).Length);
    }

    [PosixFact]
    public void Staged_tool_publishes_only_after_exit_zero_and_a_probe()
    {
        string dir = NewDir();
        string staging = AtomicDownload.CreateStaging(dir);
        try
        {
            byte[] body = Encoding.UTF8.GetBytes("staged-video");
            File.WriteAllBytes(Path.Combine(staging, "title.mp4"), body);
            File.WriteAllBytes(Path.Combine(staging, "title.mp4.part"), new byte[] { 9 });
            string? probed = null;
            using Process process = StartShell("exit 0");
            AtomicDownload.DownloadResult result = AtomicDownload.FinishStaged(
                process, staging, dir, null, path =>
                {
                    probed = path;
                    Assert.StartsWith(staging, path, StringComparison.Ordinal);
                    return true;
                }, TimeSpan.FromSeconds(5), default);
            Assert.True(result.Published);
            Assert.Equal(body, File.ReadAllBytes(Path.Combine(dir, "title.mp4")));
            Assert.False(File.Exists(Path.Combine(dir, "title.mp4.part")));
            Assert.False(string.IsNullOrEmpty(probed));
            Assert.False(AtomicDownload.ListedByLibraryScan(probed, VideoExtensions));
        }
        finally
        {
            AtomicDownload.DeleteStaging(staging);
        }
    }

    [PosixFact]
    public void Staged_tool_failure_timeout_and_probe_miss_keep_the_existing_file()
    {
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "title.mp4");
        byte[] original = Seed(finalPath, 18);

        string failedStaging = AtomicDownload.CreateStaging(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(failedStaging, "title.mp4"), new byte[] { 1, 2, 3, 4 });
            using Process process = StartShell("exit 7");
            AtomicDownload.DownloadResult failed = AtomicDownload.FinishStaged(
                process, failedStaging, dir, null, _ => true, TimeSpan.FromSeconds(5), default);
            Assert.False(failed.Published);
            Assert.Contains("exit 7", failed.Detail, StringComparison.Ordinal);
            AssertSame(finalPath, original);
        }
        finally
        {
            AtomicDownload.DeleteStaging(failedStaging);
        }

        string slowStaging = AtomicDownload.CreateStaging(dir);
        try
        {
            using Process process = StartShell("sleep 30");
            AtomicDownload.DownloadResult timed = AtomicDownload.FinishStaged(
                process, slowStaging, dir, null, _ => true, TimeSpan.FromSeconds(1), default);
            Assert.Equal("timeout", timed.Detail);
            Assert.False(timed.Published);
            AssertSame(finalPath, original);
        }
        finally
        {
            AtomicDownload.DeleteStaging(slowStaging);
        }

        string probeStaging = AtomicDownload.CreateStaging(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(probeStaging, "title.mp4"), Encoding.UTF8.GetBytes("bad"));
            using Process process = StartShell("exit 0");
            AtomicDownload.DownloadResult probed = AtomicDownload.FinishStaged(
                process, probeStaging, dir, null, _ => false, TimeSpan.FromSeconds(5), default);
            Assert.False(probed.Published);
            Assert.Equal("probe", probed.Detail);
            AssertSame(finalPath, original);
        }
        finally
        {
            AtomicDownload.DeleteStaging(probeStaging);
        }
    }

    [PosixFact]
    public void Staged_different_output_is_published_beside_the_existing_file()
    {
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "title.mp4");
        byte[] original = Seed(finalPath, 18);
        byte[] body = Encoding.UTF8.GetBytes("staged-video");
        string staging = AtomicDownload.CreateStaging(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(staging, "title.mp4"), body);
            using Process process = StartShell("exit 0");
            AtomicDownload.DownloadResult result = AtomicDownload.FinishStaged(
                process, staging, dir, null, _ => true, TimeSpan.FromSeconds(5), default);
            string published = Path.Combine(dir, "title (2).mp4");
            Assert.True(result.Published);
            Assert.Equal(published, result.FinalPath);
            Assert.Equal(new[] { published }, result.PublishedPaths);
            Assert.Equal(body, File.ReadAllBytes(published));
            AssertSame(finalPath, original);
        }
        finally
        {
            AtomicDownload.DeleteStaging(staging);
        }
    }

    [PosixFact]
    public void Staged_identical_output_reports_existing()
    {
        string dir = NewDir();
        string finalPath = Path.Combine(dir, "title.mp4");
        byte[] body = Encoding.UTF8.GetBytes("staged-video");
        File.WriteAllBytes(finalPath, body);
        string staging = AtomicDownload.CreateStaging(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(staging, "title.mp4"), body);
            using Process process = StartShell("exit 0");
            AtomicDownload.DownloadResult result = AtomicDownload.FinishStaged(
                process, staging, dir, null, _ => true, TimeSpan.FromSeconds(5), default);
            Assert.True(result.AlreadyExisted);
            Assert.False(result.Published);
            Assert.Equal("already exists", result.Detail);
            Assert.Equal(finalPath, result.FinalPath);
            Assert.False(File.Exists(Path.Combine(dir, "title (2).mp4")));
            Assert.Equal(body, File.ReadAllBytes(finalPath));
        }
        finally
        {
            AtomicDownload.DeleteStaging(staging);
        }
    }

    [PosixFact]
    public void Staged_mix_publishes_only_the_new_file()
    {
        string dir = NewDir();
        byte[] kept = Encoding.UTF8.GetBytes("kept-title");
        byte[] extra = Encoding.UTF8.GetBytes("extra-clip");
        string title = Path.Combine(dir, "title.mp4");
        File.WriteAllBytes(title, kept);
        string staging = AtomicDownload.CreateStaging(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(staging, "title.mp4"), kept);
            File.WriteAllBytes(Path.Combine(staging, "other.mp4"), extra);
            using Process process = StartShell("exit 0");
            AtomicDownload.DownloadResult result = AtomicDownload.FinishStaged(
                process, staging, dir, null, _ => true, TimeSpan.FromSeconds(5), default);
            string published = Path.Combine(dir, "other.mp4");
            Assert.True(result.Published);
            Assert.False(result.AlreadyExisted);
            Assert.Equal(new[] { published }, result.PublishedPaths);
            Assert.Equal(kept, File.ReadAllBytes(title));
            Assert.Equal(extra, File.ReadAllBytes(published));
            Assert.False(File.Exists(Path.Combine(dir, "title (2).mp4")));
        }
        finally
        {
            AtomicDownload.DeleteStaging(staging);
        }
    }

    [Fact]
    public void File_ops_reach_the_trace_hook_including_directory_delete()
    {
        var ops = new List<string>();
        AtomicDownload.FileOpHook = (op, parts) =>
        {
            string joined = parts == null ? "" : string.Join(" ", parts);
            lock (ops)
                ops.Add(op + " " + joined);
        };
        try
        {
            using var server = new Loopback();
            byte[] body = Encoding.UTF8.GetBytes("trace-bytes");
            server.Respond = (stream, _) => WriteHttp(stream, 200, body, body.Length);
            string dir = NewDir();
            string finalPath = Path.Combine(dir, "trace.png");
            AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(server.Url("trace.png"), finalPath));
            Assert.True(result.Published);

            string partial = Path.Combine(dir, Guid.NewGuid().ToString("N") + AtomicDownload.PartialMarker);
            File.WriteAllBytes(partial, new byte[] { 1, 2, 3 });
            string swept = Path.Combine(dir, AtomicDownload.PartialMarker + "-sweep");
            Directory.CreateDirectory(swept);
            File.WriteAllBytes(Path.Combine(swept, "inside.mp4"), new byte[] { 4 });
            int removed = AtomicDownload.SweepPartials(dir);
            Assert.True(removed >= 2);
            Assert.False(File.Exists(partial));
            Assert.False(Directory.Exists(swept));

            string staging = AtomicDownload.CreateStaging(dir);
            File.WriteAllBytes(Path.Combine(staging, "gone.mp4"), new byte[] { 5 });
            AtomicDownload.DeleteStaging(staging);
            Assert.False(Directory.Exists(staging));

            Assert.Contains(ops, line => line.StartsWith("File.Move ", StringComparison.Ordinal) && line.Contains(finalPath, StringComparison.Ordinal));
            Assert.Contains(ops, line => line.StartsWith("File.Delete ", StringComparison.Ordinal) && line.Contains(partial, StringComparison.Ordinal));
            Assert.Contains(ops, line => line.StartsWith("Directory.Delete ", StringComparison.Ordinal) && line.Contains(swept, StringComparison.Ordinal));
            Assert.Contains(ops, line => line.StartsWith("Directory.Delete ", StringComparison.Ordinal) && line.Contains(staging, StringComparison.Ordinal));
        }
        finally
        {
            AtomicDownload.FileOpHook = null;
        }
    }

    [Fact]
    public void Production_wires_the_trace_hook_to_NvgTrace()
    {
        string root = RepoRoot();
        string library = File.ReadAllText(Path.Combine(root, "src", "data", "LibraryData.cs"));
        string handler = File.ReadAllText(Path.Combine(root, "src", "data", "PluginHandler.cs"));
        Assert.Contains("AtomicDownload.FileOpHook = NvgTrace.FileOp;", library);
        Assert.Contains("AtomicDownload.FileOpHook = NvgTrace.FileOp;", handler);
    }

    [Fact]
    public void Sweep_under_a_partial_named_ancestor_deletes_only_partial_entries()
    {
        string parent = Path.Combine(sandbox.Root, "parent" + AtomicDownload.PartialMarker);
        string library = Path.Combine(parent, "library");
        Directory.CreateDirectory(library);
        string clip = Path.Combine(library, "clip.mp4");
        byte[] original = Seed(clip, 16);
        string partial = Path.Combine(library, Guid.NewGuid().ToString("N") + AtomicDownload.PartialMarker);
        File.WriteAllBytes(partial, new byte[] { 1, 2, 3 });
        string staging = Path.Combine(library, AtomicDownload.PartialMarker + "-dead");
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, "nested.mp4"), new byte[] { 4, 5 });

        Assert.True(AtomicDownload.IsPartialPath(library));
        Assert.False(AtomicDownload.IsPartialEntry(library));
        Assert.True(AtomicDownload.IsPartialEntry(partial));
        Assert.True(AtomicDownload.IsPartialEntry(staging));

        int removed = AtomicDownload.SweepPartials(parent);
        Assert.True(removed >= 2);
        Assert.True(Directory.Exists(parent));
        Assert.True(Directory.Exists(library));
        Assert.False(File.Exists(partial));
        Assert.False(Directory.Exists(staging));
        AssertSame(clip, original);

        AtomicDownload.DeleteStaging(library);
        Assert.True(Directory.Exists(library));
        Assert.True(Directory.Exists(parent));
        AssertSame(clip, original);
    }

    [Fact]
    public void Long_final_name_uses_a_short_partial_and_a_short_staging_directory()
    {
        using var server = new Loopback();
        byte[] body = Encoding.UTF8.GetBytes("long-name-bytes");
        server.Respond = (stream, _) => WriteHttp(stream, 200, body, body.Length);
        string dir = NewDir();
        string stem = new string('a', 236);
        string finalName = stem + ".mp4";
        Assert.Equal(240, finalName.Length);
        string finalPath = Path.Combine(dir, finalName);
        string? partialName = null;
        AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(server.Url("clip.mp4"), finalPath, probe: path =>
        {
            partialName = Path.GetFileName(path);
            Assert.True(partialName.Length <= 48);
            Assert.DoesNotContain(stem, partialName, StringComparison.Ordinal);
            Assert.EndsWith(AtomicDownload.PartialMarker, partialName, StringComparison.Ordinal);
            Assert.Matches("^[0-9a-f]{32}\\.nvgpart$", partialName);
            return true;
        }));
        Assert.True(result.Published);
        Assert.Equal(finalPath, result.FinalPath);
        Assert.Equal(body, File.ReadAllBytes(finalPath));
        Assert.False(string.IsNullOrEmpty(partialName));
        AssertNoPartial(dir);

        string staging = AtomicDownload.CreateStaging(dir);
        try
        {
            string stagingName = Path.GetFileName(staging);
            Assert.StartsWith(AtomicDownload.PartialMarker + "-", stagingName, StringComparison.Ordinal);
            Assert.InRange(stagingName.Length, 1, 20);
            Assert.DoesNotContain(stem, stagingName, StringComparison.Ordinal);
        }
        finally
        {
            AtomicDownload.DeleteStaging(staging);
        }
    }

    [Fact]
    public void Ffprobe_accepts_a_wav_and_rejects_a_missing_tool_or_junk()
    {
        string dir = NewDir();
        string wav = Path.Combine(dir, "tone.wav");
        string junk = Path.Combine(dir, "junk.mp3");
        File.WriteAllBytes(wav, TinyWav());
        File.WriteAllBytes(junk, Encoding.ASCII.GetBytes("not audio"));
        Assert.False(AtomicDownload.ProbeWithFfprobe("ffprobe-does-not-exist", wav));
        if (!ToolPaths.IsAvailable("ffprobe", true))
            return;
        string tool = ToolPaths.Ffprobe(true);
        Assert.True(AtomicDownload.ProbeWithFfprobe(tool, wav));
        Assert.False(AtomicDownload.ProbeWithFfprobe(tool, junk));
    }

    void AssertRejected(Loopback server, string name)
    {
        string dir = NewDir();
        string finalPath = Path.Combine(dir, name);
        byte[] original = Seed(finalPath, 28);
        AtomicDownload.DownloadResult result = AtomicDownload.Fetch(Request(server.Url(name), finalPath));
        Assert.False(result.Published);
        Assert.NotEqual("ok", result.Detail);
        AssertSame(finalPath, original);
        AssertNoPartial(dir);
    }

    AtomicDownload.Request Request(
        string url,
        string finalPath,
        Func<string, bool>? probe = null,
        CancellationToken cancel = default,
        Func<bool>? stop = null,
        bool keep = false,
        TimeSpan? overall = null,
        TimeSpan? connect = null)
    {
        return new AtomicDownload.Request
        {
            Url = url,
            FinalPath = finalPath,
            KeepValidExisting = keep,
            Cancellation = cancel,
            ShouldStop = stop,
            Probe = probe ?? (_ => true),
            ConnectTimeout = connect ?? TimeSpan.FromSeconds(2),
            OverallTimeout = overall ?? TimeSpan.FromSeconds(5),
        };
    }

    string NewDir()
    {
        string dir = Path.Combine(sandbox.Root, "downloads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static Process StartShell(string command)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(command);
        process.Start();
        return process;
    }

    static byte[] Seed(string path, int length)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i + 3);
        File.WriteAllBytes(path, bytes);
        return bytes;
    }

    static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "src", "data", "PluginHandler.cs")))
                return dir;
            string? parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent) || parent == dir)
                break;
            dir = parent;
        }
        throw new InvalidOperationException("Could not find PluginHandler.cs above " + AppContext.BaseDirectory);
    }

    static void AssertSame(string path, byte[] original)
    {
        Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    static void AssertNoPartial(string dir)
    {
        Assert.DoesNotContain(Directory.GetFiles(dir), AtomicDownload.IsPartialPath);
        Assert.DoesNotContain(Directory.GetDirectories(dir), AtomicDownload.IsPartialPath);
    }

    static void WriteHeaders(Stream stream, int status, long length)
    {
        string text = "HTTP/1.1 " + status + " X\r\nContent-Length: " + length + "\r\nConnection: close\r\n\r\n";
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    static void WriteHttp(Stream stream, int status, byte[] body, long length)
    {
        WriteHeaders(stream, status, length);
        if (body.Length > 0)
        {
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }
    }

    static byte[] TinyWav()
    {
        const int sampleRate = 8000;
        const short channels = 1;
        const short bits = 16;
        byte[] data = new byte[32];
        int byteRate = sampleRate * channels * bits / 8;
        short blockAlign = (short)(channels * bits / 8);
        using var stream = new MemoryStream();
        void Text(string value) => stream.Write(Encoding.ASCII.GetBytes(value));
        void Int(int value) => stream.Write(BitConverter.GetBytes(value));
        void Short(short value) => stream.Write(BitConverter.GetBytes(value));
        Text("RIFF");
        Int(36 + data.Length);
        Text("WAVE");
        Text("fmt ");
        Int(16);
        Short(1);
        Short(channels);
        Int(sampleRate);
        Int(byteRate);
        Short(blockAlign);
        Short(bits);
        Text("data");
        Int(data.Length);
        stream.Write(data);
        return stream.ToArray();
    }

    sealed class Loopback : IDisposable
    {
        readonly TcpListener listener;
        readonly CancellationTokenSource stop = new();

        public Loopback()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            ThreadPool.QueueUserWorkItem(_ => Accept());
        }

        public int Port { get; }
        public Action<NetworkStream, TcpClient>? Respond { get; set; }

        public string Url(string name) => "http://127.0.0.1:" + Port.ToString() + "/" + name;

        public void Dispose()
        {
            stop.Cancel();
            try
            {
                listener.Stop();
            }
            catch (Exception)
            {
            }
            stop.Dispose();
        }

        void Accept()
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (!listener.Pending())
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => Serve(client));
                }
                catch (Exception)
                {
                    if (stop.IsCancellationRequested)
                        return;
                }
            }
        }

        void Serve(TcpClient client)
        {
            using (client)
            {
                client.NoDelay = true;
                using NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 2000;
                var buffer = new byte[2048];
                try
                {
                    stream.Read(buffer, 0, buffer.Length);
                }
                catch (Exception)
                {
                }
                try
                {
                    Respond?.Invoke(stream, client);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
