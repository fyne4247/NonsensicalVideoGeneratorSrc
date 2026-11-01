using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

[Collection("nvg")]
public sealed class SpeechTests
{
    [Fact]
    public void Missing_voice_service_is_not_fatal()
    {
        PlatformTts.HelperExists = _ => false;
        PlatformTts.StartJob = (_, _) => throw new InvalidOperationException("speech was started");
        try
        {
            var tts = new PlatformTts();
            tts.SpeakAsync("hello");
            Assert.Equal(SynthesizerState.Ready, tts.State);
            Assert.NotNull(tts.LastNotice);
            Assert.Contains("unavailable", tts.LastNotice);
        }
        finally
        {
            PlatformTts.HelperExists = null;
            PlatformTts.StartJob = null;
        }
    }

    [Fact]
    public void Speech_is_queued_and_cancel_stops_the_current_job()
    {
        var jobs = new List<FakeSpeech>();
        int Count()
        {
            lock (jobs)
                return jobs.Count;
        }
        PlatformTts.HelperExists = name => name is "say" or "spd-say" or "espeak-ng";
        PlatformTts.StartJob = (file, args) =>
        {
            var job = new FakeSpeech(file, args);
            lock (jobs)
                jobs.Add(job);
            return job;
        };
        try
        {
            var tts = new PlatformTts();
            tts.SpeakAsync("one");
            TestWait.Until(() => Count() == 1);
            tts.SpeakAsync("two");
            Assert.Equal(1, Count());
            Assert.Equal(SynthesizerState.Speaking, tts.State);
            FakeSpeech first;
            lock (jobs)
                first = jobs[0];
            Assert.Equal("one", first.Args[^1]);
            first.Finish();
            TestWait.Until(() => Count() == 2);
            FakeSpeech second;
            lock (jobs)
                second = jobs[1];
            Assert.Equal("two", second.Args[^1]);
            Assert.Equal(first.File, second.File);
            Assert.Contains(first.File, new[] { "say", "spd-say" });
            tts.SpeakAsyncCancelAll();
            TestWait.Until(() => second.Killed && tts.State == SynthesizerState.Ready);
            Assert.Equal(SynthesizerState.Ready, tts.State);
        }
        finally
        {
            PlatformTts.HelperExists = null;
            PlatformTts.StartJob = null;
        }
    }

    sealed class FakeSpeech : ISpeechJob
    {
        readonly ManualResetEventSlim done = new(false);

        public FakeSpeech(string file, string[] args)
        {
            File = file;
            Args = args;
        }

        public string File { get; }
        public string[] Args { get; }
        public bool Killed { get; private set; }

        public void Kill()
        {
            Killed = true;
            done.Set();
        }

        public void Wait()
        {
            done.Wait();
        }

        public void Finish()
        {
            done.Set();
        }
    }
}
