using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using TaskOverlay.App;
using TaskOverlay.Core;

namespace TaskOverlay.App.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("manual recording command lifecycle", ManualRecordingCommandLifecycle),
            ("manual recording ignores policy and schedule", ManualRecordingIgnoresPolicyAndSchedule),
            ("failed start returns idle and permits retry", FailedStartReturnsIdleAndPermitsRetry),
            ("stale persisted recording does not mask start", StalePersistedRecordingDoesNotMaskStart),
            ("stop without runtime reconciles stale state", StopWithoutRuntimeReconcilesStaleState),
            ("active runtime remains owned by its MEET", ActiveRuntimeRemainsOwnedByItsMeet),
            ("recording writer factory selects explicit format", RecordingWriterFactorySelectsExplicitFormat),
            ("direct Media Foundation AAC writes finalized M4A", DirectMediaFoundationAacWritesFinalizedM4a),
            ("lossless writer creates WAV only", LosslessWriterCreatesWavOnly),
            ("real-time mixer supports partial source combinations", RealTimeMixerSupportsPartialSources),
            ("real-time mixer failure completes without hanging", RealTimeMixerFailureCompletesWithoutHanging),
            ("bounded writer backpressure is explicit", BoundedWriterBackpressureIsExplicit),
            ("encoder initialization failure has no WAV fallback", EncoderInitializationFailureHasNoWavFallback),
            ("mid-stream encoder failure remains retryable", MidStreamEncoderFailureRemainsRetryable),
            ("transcription prefers finalized mixed audio", TranscriptionPrefersFinalizedMixedAudio),
            ("recording format setting is local and defaults compact", RecordingFormatSettingIsLocalAndDefaultsCompact),
            ("legacy WAV migration and interrupted recovery are idempotent", LegacyWavMigrationAndInterruptedRecoveryAreIdempotent)
        };

        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS: {test.Name}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {test.Name}");
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        return 0;
    }

    private static async Task ManualRecordingCommandLifecycle()
    {
        await using var fixture = new RecordingFixture();
        var start = await fixture.SendAsync(
            "startMeetingRecording",
            new { meetingId = fixture.Meeting.Id.ToString("N") });

        Assert(start.Success, start.ErrorMessage ?? "Start command failed.");
        Assert(fixture.Recorder.StartCount == 1, "Start must reach the recorder exactly once.");
        var active = fixture.State.MeetingRecordings.Single();
        Assert(active.MeetId == fixture.Meeting.Id,
            "The recording must use the selected meetingId.");
        Assert(active.State == MeetingRecordingState.Recording &&
               active.RecordingFormat == MeetingRecordingFormat.AacM4a &&
               fixture.Recorder.Status.RecordingId == active.Id,
            "Successful Start must produce matching persisted and runtime Recording state.");
        var activeSnapshot = fixture.Snapshot();
        Assert(activeSnapshot.ActiveMeetingRecordingId == active.Id.ToString("N") &&
               activeSnapshot.MeetingRecordings.Single().MeetingId == fixture.Meeting.Id.ToString("N"),
            "Fresh snapshot must identify the live recording and owning MEET.");

        var stop = await fixture.SendAsync(
            "stopMeetingRecording",
            new { recordingId = active.Id.ToString("N") });
        Assert(stop.Success, stop.ErrorMessage ?? "Stop command failed.");
        Assert(fixture.Recorder.StopCount == 1 &&
               fixture.Recorder.Status.RecordingId is null,
            "Stop must finalize and clear the runtime session.");
        Assert(active.State == MeetingRecordingState.Recorded,
            "Stop must persist a completed recording under the MEET.");
        Assert(fixture.Snapshot().ActiveMeetingRecordingId is null,
            "Fresh snapshot after Stop must return to idle.");

        var reloaded = fixture.Store.Load();
        Assert(reloaded.MeetingRecordings.Single().State == MeetingRecordingState.Recorded &&
               reloaded.MeetingRecordings.Single().MeetId == fixture.Meeting.Id,
            "Finalized recording must survive state save/load.");

        var secondStart = await fixture.SendAsync(
            "startMeetingRecording",
            new { meetingId = fixture.Meeting.Id.ToString("N") });
        Assert(secondStart.Success && fixture.Recorder.StartCount == 2,
            "A second recording must start after a normal Stop.");
        Assert(fixture.State.MeetingRecordings.Count == 2 &&
               fixture.State.MeetingRecordings.Count(item => item.State == MeetingRecordingState.Recording) == 1,
            "The first recording must remain completed while the second owns runtime activity.");
        Assert(fixture.StateChangedCount >= 5,
            "Lifecycle persistence must request fresh Workspace snapshots.");
        Assert(fixture.Diagnostics.Any(message => message.Contains(
                   "Manual MEET recording start command received", StringComparison.Ordinal)) &&
               fixture.Diagnostics.Any(message => message.Contains(
                   "device initialization completed", StringComparison.Ordinal)) &&
               fixture.Diagnostics.Any(message => message.Contains(
                   "recording finalized", StringComparison.Ordinal)),
            "Safe diagnostics must cover command, device initialization, and finalization.");
    }

    private static async Task ManualRecordingIgnoresPolicyAndSchedule()
    {
        var now = DateTimeOffset.UtcNow;
        var scenarios = new[]
        {
            (MeetingRecordingPolicy.Manual, now.AddDays(-7), "past Manual MEET"),
            (MeetingRecordingPolicy.Inherit, now, "current inherited-Manual MEET"),
            (MeetingRecordingPolicy.Manual, now.AddDays(7), "future Manual MEET")
        };

        foreach (var scenario in scenarios)
        {
            await using var fixture = new RecordingFixture(
                scenario.Item1,
                scenario.Item2);
            var result = await fixture.SendAsync(
                "startMeetingRecording",
                new { meetingId = fixture.Meeting.Id.ToString("N") });
            Assert(result.Success, $"Manual Start must be available for {scenario.Item3}.");
            var recording = fixture.State.MeetingRecordings.Single(item => item.IsActive);
            var stop = await fixture.SendAsync(
                "stopMeetingRecording",
                new { recordingId = recording.Id.ToString("N") });
            Assert(stop.Success, $"Cleanup Stop failed for {scenario.Item3}.");
        }
    }

    private static async Task FailedStartReturnsIdleAndPermitsRetry()
    {
        await using var fixture = new RecordingFixture();
        fixture.Recorder.FailNextStart = true;
        var failed = await fixture.SendAsync(
            "startMeetingRecording",
            new { meetingId = fixture.Meeting.Id.ToString("N") });

        Assert(!failed.Success && !string.IsNullOrWhiteSpace(failed.ErrorMessage),
            "Failed device initialization must return a visible command error.");
        Assert(fixture.Recorder.Status.RecordingId is null &&
               fixture.State.MeetingRecordings.Single().State == MeetingRecordingState.Failed &&
               fixture.Snapshot().ActiveMeetingRecordingId is null,
            "Failed Start must return runtime and snapshot to idle.");

        var retry = await fixture.SendAsync(
            "startMeetingRecording",
            new { meetingId = fixture.Meeting.Id.ToString("N") });
        Assert(retry.Success && fixture.Recorder.Status.RecordingId is not null,
            "Start must become available again after initialization failure.");
    }

    private static async Task StalePersistedRecordingDoesNotMaskStart()
    {
        await using var fixture = new RecordingFixture();
        var stale = fixture.AddPersistedActiveRecording();
        Assert(fixture.Snapshot().ActiveMeetingRecordingId is null,
            "Persisted Recording state alone must not create fake runtime activity.");

        var start = await fixture.SendAsync(
            "startMeetingRecording",
            new { meetingId = fixture.Meeting.Id.ToString("N") });

        Assert(start.Success, start.ErrorMessage ?? "Start after stale state failed.");
        Assert(stale.State == MeetingRecordingState.Failed &&
               stale.LastError.Contains("No live recorder session", StringComparison.Ordinal),
            "Persisted Recording without runtime capture must be repaired, not treated as live.");
        Assert(fixture.State.MeetingRecordings.Single(item => item.IsActive).Id ==
               fixture.Recorder.Status.RecordingId,
            "New Start must own the only live recording after stale recovery.");
    }

    private static async Task StopWithoutRuntimeReconcilesStaleState()
    {
        await using var fixture = new RecordingFixture();
        var stale = fixture.AddPersistedActiveRecording();

        var stop = await fixture.SendAsync(
            "stopMeetingRecording",
            new { recordingId = stale.Id.ToString("N") });

        Assert(!stop.Success &&
               stop.ErrorMessage?.Contains("No live", StringComparison.OrdinalIgnoreCase) == true,
            "Stop without a runtime session must return a safe visible error.");
        Assert(stale.State == MeetingRecordingState.Failed &&
               fixture.Snapshot().ActiveMeetingRecordingId is null,
            "Stop without runtime must reconcile stale activity and publish idle state.");
    }

    private static async Task ActiveRuntimeRemainsOwnedByItsMeet()
    {
        await using var fixture = new RecordingFixture();
        var other = fixture.AddMeeting("Other MEET", DateTimeOffset.UtcNow.AddHours(2));
        var start = await fixture.SendAsync(
            "startMeetingRecording",
            new { meetingId = fixture.Meeting.Id.ToString("N") });
        Assert(start.Success, start.ErrorMessage ?? "Start command failed.");

        var snapshot = fixture.Snapshot();
        var active = snapshot.MeetingRecordings.Single(recording =>
            recording.Id == snapshot.ActiveMeetingRecordingId);
        Assert(active.MeetingId == fixture.Meeting.Id.ToString("N") &&
               active.MeetingId != other.Id.ToString("N"),
            "Selecting another MEET must not transfer runtime recording ownership.");
    }

    private static Task RecordingWriterFactorySelectsExplicitFormat()
    {
        var factory = new RecordingTrackWriterFactory();
        Assert(factory.Create(MeetingRecordingFormat.AacM4a) is
                   MediaFoundationAacTrackWriter,
            "Compact format must select the direct Media Foundation AAC writer.");
        Assert(factory.Create(MeetingRecordingFormat.Wav) is WaveTrackWriter,
            "Lossless format must select the independent WAV writer.");
        return Task.CompletedTask;
    }

    private static async Task DirectMediaFoundationAacWritesFinalizedM4a()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await using var writer = new MediaFoundationAacTrackWriter();
            await writer.StartAsync(new RecordingTrackWriterStartRequest(
                MeetingRecordingTrackKind.Mixed,
                directory,
                "mixed",
                PreferredChannels: 1));
            await WriteSyntheticToneAsync(writer, TimeSpan.FromSeconds(2));
            var artifact = await writer.CompleteAsync();
            var path = Path.Combine(directory, artifact.FileName);

            Assert(artifact.ValidationState == MeetingRecordingValidationState.Valid &&
                   artifact.FinalizationState == MeetingRecordingFinalizationState.Finalized &&
                   artifact.Codec == "AAC-LC" &&
                   artifact.Bitrate > 0 &&
                   File.Exists(path),
                "Direct AAC writer must finalize and validate a playable M4A artifact.");
            Assert(Directory.EnumerateFiles(directory, "*.wav").Any() == false &&
                   File.Exists(Path.Combine(directory, "mixed.current.m4a")) == false,
                "Compact AAC recording must not create a full temporary WAV or retain a current file after Stop.");
            using var reader = new MediaFoundationReader(path);
            Assert(reader.TotalTime > TimeSpan.FromSeconds(1),
                "Finalized M4A must reopen with plausible duration.");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task LosslessWriterCreatesWavOnly()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await using var writer = new WaveTrackWriter();
            await writer.StartAsync(new RecordingTrackWriterStartRequest(
                MeetingRecordingTrackKind.Microphone,
                directory,
                "microphone",
                PreferredChannels: 1));
            await WriteSyntheticToneAsync(writer, TimeSpan.FromMilliseconds(250));
            var artifact = await writer.CompleteAsync();

            Assert(artifact.ValidationState == MeetingRecordingValidationState.Valid &&
                   artifact.FileName == "microphone.wav" &&
                   File.Exists(Path.Combine(directory, artifact.FileName)),
                "Lossless mode must finalize its explicit WAV artifact.");
            Assert(Directory.EnumerateFiles(directory, "*.m4a").Any() == false,
                "Lossless WAV mode must not run a hidden AAC conversion.");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task RealTimeMixerSupportsPartialSources()
    {
        await AssertMixScenario(systemExpected: true, microphoneExpected: true,
            sendSystem: true, sendMicrophone: true, shouldContainAudio: true);
        await AssertMixScenario(systemExpected: true, microphoneExpected: false,
            sendSystem: true, sendMicrophone: false, shouldContainAudio: true);
        await AssertMixScenario(systemExpected: false, microphoneExpected: true,
            sendSystem: false, sendMicrophone: true, shouldContainAudio: true);
        await AssertMixScenario(systemExpected: true, microphoneExpected: true,
            sendSystem: false, sendMicrophone: false, shouldContainAudio: false);
    }

    private static async Task RealTimeMixerFailureCompletesWithoutHanging()
    {
        var writer = new CollectingTrackWriter();
        await using var mixer = new RealtimePcmMixer(
            systemExpected: true,
            microphoneExpected: false,
            writer);
        await mixer.StartAsync(Path.GetTempPath());

        var accepted = mixer.TryAdd(
            MeetingRecordingTrackKind.System,
            CreateToneFrame(44_100, 2, 0, 882),
            new WaveFormat(44_100, 16, 2));
        Assert(!accepted,
            "The mixer must reject frames outside its normalized input format.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var artifact = await mixer.CompleteAsync(
            TimeSpan.FromMilliseconds(20),
            timeout.Token);
        Assert(artifact.FinalizationState == MeetingRecordingFinalizationState.Failed &&
               artifact.ValidationState == MeetingRecordingValidationState.Invalid &&
               !string.IsNullOrWhiteSpace(artifact.Error),
            "A failed mix queue must complete promptly with an explicit invalid artifact.");
    }

    private static async Task AssertMixScenario(
        bool systemExpected,
        bool microphoneExpected,
        bool sendSystem,
        bool sendMicrophone,
        bool shouldContainAudio)
    {
        var writer = new CollectingTrackWriter();
        await using var mixer = new RealtimePcmMixer(
            systemExpected,
            microphoneExpected,
            writer);
        await mixer.StartAsync(Path.GetTempPath());
        var frameCount = 960;
        var duration = TimeSpan.FromMilliseconds(20).Ticks;
        if (sendSystem)
        {
            Assert(mixer.TryAdd(
                    MeetingRecordingTrackKind.System,
                    CreateToneFrame(48_000, 2, 0, frameCount),
                    new WaveFormat(48_000, 16, 2)),
                "Normalized stereo system frame must enter the mixer.");
        }

        if (sendMicrophone)
        {
            Assert(mixer.TryAdd(
                    MeetingRecordingTrackKind.Microphone,
                    CreateToneFrame(48_000, 1, 0, frameCount),
                    new WaveFormat(48_000, 16, 1)),
                "Normalized mono microphone frame must enter the mixer.");
        }

        var artifact = await mixer.CompleteAsync(TimeSpan.FromTicks(duration));
        Assert((artifact.ValidationState == MeetingRecordingValidationState.Valid) ==
               shouldContainAudio,
            "Mixed artifact validity must match actual received source frames.");
        Assert((writer.Frames.Count > 0) == shouldContainAudio,
            "The mixer must not invent successful audio when both sources are empty.");
    }

    private static async Task BoundedWriterBackpressureIsExplicit()
    {
        using var gate = new ManualResetEventSlim();
        await using var writer = new BlockingQueuedTrackWriter(gate);
        await writer.StartAsync(new RecordingTrackWriterStartRequest(
            MeetingRecordingTrackKind.System,
            Path.GetTempPath(),
            "bounded",
            PreferredChannels: 1));
        var frame = CreateToneFrame(48_000, 1, 0, 960);
        var rejected = false;
        for (var index = 0; index < 2_000; index++)
        {
            if (!writer.TryWrite(frame with
                {
                    SampleTime100Nanoseconds = index * frame.SampleDuration100Nanoseconds
                }))
            {
                rejected = true;
                break;
            }
        }

        Assert(rejected && writer.Error?.Contains("bounded", StringComparison.OrdinalIgnoreCase) == true,
            "A full encoder queue must fail explicitly instead of growing without bound or silently dropping frames.");
        gate.Set();
        await writer.AbortAsync();
    }

    private static async Task MidStreamEncoderFailureRemainsRetryable()
    {
        await using (var failedWriter = new FailingQueuedTrackWriter())
        {
            await failedWriter.StartAsync(new RecordingTrackWriterStartRequest(
                MeetingRecordingTrackKind.Microphone,
                Path.GetTempPath(),
                "failed",
                PreferredChannels: 1));
            var frame = CreateToneFrame(48_000, 1, 0, 960);
            await failedWriter.WriteAsync(frame);
            await failedWriter.WriteAsync(frame with
            {
                SampleTime100Nanoseconds = frame.SampleDuration100Nanoseconds
            });
            var artifact = await failedWriter.CompleteAsync();
            Assert(artifact.ValidationState == MeetingRecordingValidationState.Invalid &&
                   artifact.FinalizationState == MeetingRecordingFinalizationState.Failed &&
                   !string.IsNullOrWhiteSpace(artifact.Error),
                "A mid-stream encoder exception must produce an explicit failed artifact.");
        }

        var retryDirectory = CreateTemporaryDirectory();
        try
        {
            await using var retry = new WaveTrackWriter();
            await retry.StartAsync(new RecordingTrackWriterStartRequest(
                MeetingRecordingTrackKind.Microphone,
                retryDirectory,
                "retry",
                PreferredChannels: 1));
            await WriteSyntheticToneAsync(retry, TimeSpan.FromMilliseconds(100));
            var retryArtifact = await retry.CompleteAsync();
            Assert(retryArtifact.ValidationState == MeetingRecordingValidationState.Valid,
                "An encoder failure must not block a later recording writer.");
        }
        finally
        {
            DeleteTemporaryDirectory(retryDirectory);
        }
    }

    private static async Task EncoderInitializationFailureHasNoWavFallback()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await using var failed = new InitializationFailingTrackWriter();
            var threw = false;
            try
            {
                await failed.StartAsync(new RecordingTrackWriterStartRequest(
                    MeetingRecordingTrackKind.System,
                    directory,
                    "system",
                    PreferredChannels: 2));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains(
                       "Synthetic AAC initialization failure",
                       StringComparison.Ordinal))
            {
                threw = true;
            }

            Assert(threw && Directory.EnumerateFiles(directory, "*.wav").Any() == false,
                "Compact encoder initialization failure must be visible and must not create a hidden WAV fallback.");

            await using var retry = new WaveTrackWriter();
            await retry.StartAsync(new RecordingTrackWriterStartRequest(
                MeetingRecordingTrackKind.System,
                directory,
                "system",
                PreferredChannels: 2));
            await WriteSyntheticToneAsync(retry, TimeSpan.FromMilliseconds(100));
            Assert((await retry.CompleteAsync()).ValidationState ==
                   MeetingRecordingValidationState.Valid,
                "The user must be able to retry explicitly in Lossless WAV mode.");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task TranscriptionPrefersFinalizedMixedAudio()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await using (var writer = new MediaFoundationAacTrackWriter())
            {
                await writer.StartAsync(new RecordingTrackWriterStartRequest(
                    MeetingRecordingTrackKind.Mixed,
                    directory,
                    "mixed",
                    PreferredChannels: 1));
                await WriteSyntheticToneAsync(writer, TimeSpan.FromMilliseconds(500));
                var artifact = await writer.CompleteAsync();
                Assert(artifact.ValidationState == MeetingRecordingValidationState.Valid,
                    "Test mixed M4A did not finalize.");
            }

            var mixedPath = Path.Combine(directory, "mixed.m4a");
            var processor = new MeetingAudioProcessor();
            var result = await processor.ProcessAsync(new MeetingAudioProcessingRequest(
                Guid.NewGuid(),
                directory,
                null,
                null,
                null,
                null,
                ExistingMixedAudioPath: mixedPath,
                RecordingFormat: MeetingRecordingFormat.AacM4a));
            Assert(result.MixedAudioPath == mixedPath &&
                   result.OrderedChunkPaths.SequenceEqual(new[] { mixedPath }) &&
                   Directory.EnumerateFiles(directory, "mixed.wav").Any() == false,
                "Transcription must prefer the finalized mixed M4A without creating a full WAV.");

            var currentPath = Path.Combine(directory, "mixed.current.m4a");
            File.Copy(mixedPath, currentPath);
            var rejected = false;
            try
            {
                await processor.ProcessAsync(new MeetingAudioProcessingRequest(
                    Guid.NewGuid(),
                    directory,
                    null,
                    null,
                    null,
                    null,
                    ExistingMixedAudioPath: currentPath,
                    RecordingFormat: MeetingRecordingFormat.AacM4a));
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Assert(rejected,
                "Transcription must reject in-progress/current M4A artifacts.");

            var providerRejected = false;
            try
            {
                await new OpenAiTranscriptionProvider(() => "synthetic-key")
                    .TranscribeAsync(new TranscriptionProviderRequest(
                        currentPath,
                        "test-model",
                        MeetingTranscriptLanguage.Auto,
                        TimeSpan.Zero));
            }
            catch (OpenAiProviderException)
            {
                providerRejected = true;
            }

            Assert(providerRejected,
                "The provider boundary must also reject current/part artifacts before upload.");

            var missingMixedRejected = false;
            try
            {
                await processor.ProcessAsync(new MeetingAudioProcessingRequest(
                    Guid.NewGuid(),
                    directory,
                    mixedPath,
                    null,
                    null,
                    null,
                    ExistingMixedAudioPath: null,
                    RecordingFormat: MeetingRecordingFormat.AacM4a));
            }
            catch (InvalidDataException)
            {
                missingMixedRejected = true;
            }

            Assert(missingMixedRejected &&
                   Directory.EnumerateFiles(directory, "mixed.wav").Any() == false,
                "Compact transcription must not rebuild a missing mixed track through a full WAV intermediate.");

            var legacyDirectory = Path.Combine(directory, "legacy");
            Directory.CreateDirectory(legacyDirectory);
            await using (var legacyWriter = new WaveTrackWriter())
            {
                await legacyWriter.StartAsync(new RecordingTrackWriterStartRequest(
                    MeetingRecordingTrackKind.System,
                    legacyDirectory,
                    "system",
                    PreferredChannels: 1,
                    PreferredSampleRate: 48_000));
                await WriteSyntheticToneAsync(
                    legacyWriter,
                    TimeSpan.FromMilliseconds(250));
                await legacyWriter.CompleteAsync();
            }

            var legacyResult = await processor.ProcessAsync(
                new MeetingAudioProcessingRequest(
                    Guid.NewGuid(),
                    legacyDirectory,
                    Path.Combine(legacyDirectory, "system.wav"),
                    DateTimeOffset.UtcNow,
                    null,
                    null,
                    RecordingFormat: MeetingRecordingFormat.Wav));
            Assert(legacyResult.MixedAudioPath.EndsWith(
                       "mixed.wav",
                       StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(legacyResult.MixedAudioPath),
                "Legacy WAV recordings must remain transcribable through the compatibility path.");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static Task RecordingFormatSettingIsLocalAndDefaultsCompact()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var defaults = new LocalAppSettings();
            Assert(defaults.MeetingAssistant.RecordingFormat ==
                   MeetingRecordingFormat.AacM4a,
                "Compact AAC/M4A must be the machine-local default.");
            defaults.MeetingAssistant.RecordingFormat = MeetingRecordingFormat.Wav;
            new LocalSettingsStore(directory).Save(defaults);
            var loaded = new LocalSettingsStore(directory).Load();
            Assert(loaded.MeetingAssistant.RecordingFormat == MeetingRecordingFormat.Wav,
                "Explicit WAV selection must persist in machine-local settings.");

            var state = AppState.CreateDefault();
            new AppStateStore(directory).Save(state);
            var stateJson = File.ReadAllText(Path.Combine(directory, "state.json"));
            Assert(!stateJson.Contains("meetingAssistant", StringComparison.OrdinalIgnoreCase) &&
                   !stateJson.Contains("recordingFormat", StringComparison.OrdinalIgnoreCase),
                "The new-recording format setting must not enter shared AppState/state.json.");
            return Task.CompletedTask;
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static Task LegacyWavMigrationAndInterruptedRecoveryAreIdempotent()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var state = AppState.CreateDefault();
            state.SchemaVersion = 4;
            var legacy = new MeetingRecording
            {
                SourceKind = MeetingRecordingSourceKind.Emergency,
                State = MeetingRecordingState.Recorded,
                RecordingFolderRelativePath = $"meetings/unclassified/recordings/{Guid.NewGuid():N}",
                MixedAudioFile = "mixed.wav",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            var interrupted = new MeetingRecording
            {
                SourceKind = MeetingRecordingSourceKind.Emergency,
                State = MeetingRecordingState.Recording,
                RecordingFormat = MeetingRecordingFormat.AacM4a,
                RecordingFolderRelativePath = $"meetings/unclassified/recordings/{Guid.NewGuid():N}",
                Tracks = new List<MeetingRecordingTrackArtifact>
                {
                    new()
                    {
                        Kind = MeetingRecordingTrackKind.Mixed,
                        InProgressFileName = "mixed.current.m4a",
                        Container = "MPEG-4/M4A",
                        Codec = "AAC-LC",
                        FinalizationState = MeetingRecordingFinalizationState.InProgress,
                        ValidationState = MeetingRecordingValidationState.Unknown
                    }
                },
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            state.MeetingRecordings.Add(legacy);
            state.MeetingRecordings.Add(interrupted);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(
                    JsonNamingPolicy.CamelCase) }
            };
            File.WriteAllText(
                Path.Combine(directory, "state.json"),
                JsonSerializer.Serialize(state, options));

            var loaded = new AppStateStore(directory).Load();
            var loadedLegacy = loaded.MeetingRecordings.Single(item => item.Id == legacy.Id);
            var loadedInterrupted = loaded.MeetingRecordings.Single(item => item.Id == interrupted.Id);
            Assert(loaded.SchemaVersion == AppState.CurrentSchemaVersion &&
                   loadedLegacy.RecordingFormat == MeetingRecordingFormat.Wav &&
                   loadedLegacy.MixedAudioFile == "mixed.wav" &&
                   loadedLegacy.Tracks.Single().Kind == MeetingRecordingTrackKind.Mixed,
                "Schema v4 WAV references must migrate without destructive file changes.");
            Assert(loadedInterrupted.State == MeetingRecordingState.Failed &&
                   loadedInterrupted.Tracks.Single().FinalizationState ==
                   MeetingRecordingFinalizationState.Interrupted &&
                   loadedInterrupted.Tracks.Single().ValidationState ==
                   MeetingRecordingValidationState.Invalid &&
                   loadedInterrupted.Tracks.Single().InProgressFileName ==
                   "mixed.current.m4a",
                "Startup recovery must preserve the in-progress name while refusing to report it Ready.");

            var secondLoad = new AppStateStore(directory).Load();
            Assert(secondLoad.MeetingRecordings.Count == 2 &&
                   secondLoad.MeetingRecordings.Single(item => item.Id == interrupted.Id)
                       .Tracks.Count == 1,
                "Repeated startup recovery must not duplicate recordings or artifacts.");
            return Task.CompletedTask;
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task WriteSyntheticToneAsync(
        IRecordingTrackWriter writer,
        TimeSpan duration)
    {
        const int frameCount = 960;
        var totalFrames = (long)Math.Ceiling(
            duration.TotalSeconds * writer.InputFormat.SampleRate);
        for (long startFrame = 0; startFrame < totalFrames; startFrame += frameCount)
        {
            var count = (int)Math.Min(frameCount, totalFrames - startFrame);
            await writer.WriteAsync(CreateToneFrame(
                writer.InputFormat.SampleRate,
                writer.InputFormat.Channels,
                startFrame,
                count));
        }
    }

    private static PcmAudioFrame CreateToneFrame(
        int sampleRate,
        int channels,
        long startFrame,
        int frameCount)
    {
        var bytes = new byte[frameCount * channels * sizeof(short)];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = (short)(Math.Sin(
                2 * Math.PI * 440 * (startFrame + frame) / sampleRate) * 8_000);
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = (frame * channels + channel) * sizeof(short);
                bytes[offset] = (byte)(sample & 0xff);
                bytes[offset + 1] = (byte)((sample >> 8) & 0xff);
            }
        }

        return new PcmAudioFrame(
            bytes,
            startFrame * 10_000_000L / sampleRate,
            frameCount * 10_000_000L / sampleRate);
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "TaskOverlay.App.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RecordingFixture : IAsyncDisposable
    {
        private readonly string _directory;

        public RecordingFixture(
            MeetingRecordingPolicy policy = MeetingRecordingPolicy.Manual,
            DateTimeOffset? startsAtUtc = null)
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "TaskOverlay.App.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            State = AppState.CreateDefault(DateTimeOffset.UtcNow);
            Meeting = AddMeeting("Selected MEET", startsAtUtc ?? DateTimeOffset.UtcNow);
            Meeting.RecordingPolicy = policy;
            Store = new AppStateStore(_directory);
            Recorder = new FakeMeetingRecorder();
            var localSettings = new LocalAppSettings
            {
                MeetingAssistant = new MeetingAssistantSettings
                {
                    DefaultRecordingPolicy = MeetingRecordingPolicy.Manual
                }
            };
            Coordinator = new MeetingAssistantCoordinator(
                State,
                localSettings,
                _directory,
                () => Store.Save(State),
                () => { },
                () => StateChangedCount++,
                Recorder,
                new UnusedAudioProcessor(),
                new UnusedTranscriptionProvider(),
                new UnusedAnalysisProvider(),
                (message, _) => Diagnostics.Add(message));
            Handler = new MeetingAssistantWorkspaceCommandHandler(Coordinator);
        }

        public AppState State { get; }
        public MeetingItem Meeting { get; }
        public AppStateStore Store { get; }
        public FakeMeetingRecorder Recorder { get; }
        public MeetingAssistantCoordinator Coordinator { get; }
        public MeetingAssistantWorkspaceCommandHandler Handler { get; }
        public List<string> Diagnostics { get; } = new();
        public int StateChangedCount { get; private set; }

        public MeetingItem AddMeeting(string title, DateTimeOffset startsAtUtc)
        {
            var meeting = new MeetingItem
            {
                ProjectId = State.Projects[0].Id,
                Title = title,
                StartsAtUtc = startsAtUtc,
                DurationMinutes = 30,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            State.Meetings.Add(meeting);
            return meeting;
        }

        public MeetingRecording AddPersistedActiveRecording()
        {
            var recording = new MeetingRecording
            {
                MeetId = Meeting.Id,
                SourceKind = MeetingRecordingSourceKind.ManualMeet,
                State = MeetingRecordingState.Recording,
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                RecordingFolderRelativePath =
                    $"meetings/{Meeting.Id:N}/recordings/{Guid.NewGuid():N}",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            };
            State.MeetingRecordings.Add(recording);
            return recording;
        }

        public async Task<WorkspaceCommandResult> SendAsync(string type, object payload)
        {
            var command = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                commandId = Guid.NewGuid().ToString("N"),
                type,
                payload
            });
            return await Handler.TryHandleAsync(command) ??
                   throw new InvalidOperationException("Command was not recognized.");
        }

        public WorkspaceSnapshot Snapshot() => WorkspaceSnapshotFactory.Create(
            State,
            mode: WorkspaceSnapshotFactory.ConnectedMode,
            activeMeetingRecordingId: Recorder.Status.RecordingId);

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private sealed class CollectingTrackWriter : IRecordingTrackWriter
    {
        private RecordingTrackWriterState _state = RecordingTrackWriterState.Created;

        public List<PcmAudioFrame> Frames { get; } = new();
        public RecordingTrackWriterState CurrentState => _state;
        public WaveFormat InputFormat { get; private set; } = new(48_000, 16, 1);
        public long BytesWritten => Frames.Sum(frame => frame.Data.LongLength);
        public TimeSpan Duration => Frames.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(Frames.Max(frame =>
                frame.SampleTime100Nanoseconds + frame.SampleDuration100Nanoseconds));
        public string? Error { get; private set; }

        public Task StartAsync(
            RecordingTrackWriterStartRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InputFormat = new WaveFormat(48_000, 16, 1);
            _state = RecordingTrackWriterState.Writing;
            return Task.CompletedTask;
        }

        public bool TryWrite(PcmAudioFrame frame)
        {
            if (_state != RecordingTrackWriterState.Writing)
            {
                return false;
            }

            Frames.Add(frame);
            return true;
        }

        public ValueTask WriteAsync(
            PcmAudioFrame frame,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryWrite(frame))
            {
                throw new InvalidOperationException("Collecting writer is not active.");
            }

            return ValueTask.CompletedTask;
        }

        public Task<MeetingRecordingTrackArtifact> CompleteAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state = RecordingTrackWriterState.Completed;
            return Task.FromResult(SnapshotArtifact());
        }

        public Task AbortAsync(CancellationToken cancellationToken = default)
        {
            _state = RecordingTrackWriterState.Aborted;
            return Task.CompletedTask;
        }

        public MeetingRecordingTrackArtifact SnapshotArtifact()
        {
            var valid = Frames.Count > 0;
            return new MeetingRecordingTrackArtifact
            {
                Kind = MeetingRecordingTrackKind.Mixed,
                FileName = valid ? "mixed.test" : string.Empty,
                Container = "test",
                Codec = "PCM16",
                SampleRate = InputFormat.SampleRate,
                ChannelCount = InputFormat.Channels,
                Bitrate = InputFormat.AverageBytesPerSecond * 8,
                DurationSeconds = Duration.TotalSeconds,
                Bytes = BytesWritten,
                HasAudioFrames = valid,
                FinalizationState = _state == RecordingTrackWriterState.Completed
                    ? MeetingRecordingFinalizationState.Finalized
                    : MeetingRecordingFinalizationState.InProgress,
                ValidationState = valid
                    ? MeetingRecordingValidationState.Valid
                    : MeetingRecordingValidationState.Invalid,
                Error = Error ?? (valid ? string.Empty : "No audio frames were captured.")
            };
        }

        public ValueTask DisposeAsync()
        {
            if (_state == RecordingTrackWriterState.Writing)
            {
                _state = RecordingTrackWriterState.Aborted;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingQueuedTrackWriter : QueuedRecordingTrackWriter
    {
        private readonly ManualResetEventSlim _gate;

        public BlockingQueuedTrackWriter(ManualResetEventSlim gate)
            : base(null)
        {
            _gate = gate;
        }

        protected override Task InitializeCoreAsync(
            RecordingTrackWriterStartRequest request,
            CancellationToken cancellationToken)
        {
            InputFormat = new WaveFormat(48_000, 16, 1);
            return Task.CompletedTask;
        }

        protected override void WriteFrameCore(PcmAudioFrame frame) => _gate.Wait();

        protected override Task CompleteCoreAsync(CancellationToken cancellationToken)
        {
            ValidationState = MeetingRecordingValidationState.Valid;
            return Task.CompletedTask;
        }

        protected override Task AbortCoreAsync(
            bool preserveInProgressFile,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingQueuedTrackWriter : QueuedRecordingTrackWriter
    {
        private int _writes;

        public FailingQueuedTrackWriter()
            : base(null)
        {
        }

        protected override Task InitializeCoreAsync(
            RecordingTrackWriterStartRequest request,
            CancellationToken cancellationToken)
        {
            InputFormat = new WaveFormat(48_000, 16, 1);
            return Task.CompletedTask;
        }

        protected override void WriteFrameCore(PcmAudioFrame frame)
        {
            if (Interlocked.Increment(ref _writes) >= 2)
            {
                throw new InvalidOperationException("Synthetic encoder failure.");
            }
        }

        protected override Task CompleteCoreAsync(CancellationToken cancellationToken)
        {
            ValidationState = MeetingRecordingValidationState.Valid;
            return Task.CompletedTask;
        }

        protected override Task AbortCoreAsync(
            bool preserveInProgressFile,
            CancellationToken cancellationToken)
        {
            ValidationState = MeetingRecordingValidationState.Invalid;
            return Task.CompletedTask;
        }
    }

    private sealed class InitializationFailingTrackWriter : QueuedRecordingTrackWriter
    {
        public InitializationFailingTrackWriter()
            : base(null)
        {
        }

        protected override Task InitializeCoreAsync(
            RecordingTrackWriterStartRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic AAC initialization failure.");

        protected override void WriteFrameCore(PcmAudioFrame frame) =>
            throw new NotSupportedException();

        protected override Task CompleteCoreAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        protected override Task AbortCoreAsync(
            bool preserveInProgressFile,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeMeetingRecorder : IMeetingRecorder
    {
        private MeetingRecorderRuntimeStatus _status = IdleStatus();
        private MeetingRecordingFormat _format = MeetingRecordingFormat.AacM4a;

        public bool FailNextStart { get; set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public MeetingRecorderRuntimeStatus Status => _status;

        public IReadOnlyList<AudioDeviceDescriptor> GetMicrophoneDevices() =>
            Array.Empty<AudioDeviceDescriptor>();

        public IReadOnlyList<AudioDeviceDescriptor> GetSystemOutputDevices() =>
            Array.Empty<AudioDeviceDescriptor>();

        public Task<MeetingRecordingStartResult> StartAsync(
            MeetingRecordingStartRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            if (FailNextStart)
            {
                FailNextStart = false;
                throw new InvalidOperationException("Synthetic device initialization failure.");
            }

            if (_status.RecordingId is not null)
            {
                throw new InvalidOperationException("Synthetic recorder already active.");
            }

            var startedAt = DateTimeOffset.UtcNow;
            _format = request.RecordingFormat;
            _status = new MeetingRecorderRuntimeStatus(
                request.RecordingId,
                startedAt,
                AudioTrackHealth.Healthy,
                AudioTrackHealth.Healthy,
                null,
                _format);
            return Task.FromResult(new MeetingRecordingStartResult(
                startedAt,
                startedAt,
                startedAt,
                string.Empty,
                string.Empty,
                AudioTrackHealth.Healthy,
                AudioTrackHealth.Healthy,
                null,
                _format,
                new[]
                {
                    CreateArtifact(MeetingRecordingTrackKind.System, finalized: false),
                    CreateArtifact(MeetingRecordingTrackKind.Microphone, finalized: false),
                    CreateArtifact(MeetingRecordingTrackKind.Mixed, finalized: false)
                }));
        }

        public Task<MeetingRecordingStopResult> StopAsync(
            Guid recordingId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_status.RecordingId != recordingId)
            {
                throw new InvalidOperationException("Synthetic recorder ownership mismatch.");
            }

            StopCount++;
            _status = IdleStatus();
            return Task.FromResult(new MeetingRecordingStopResult(
                DateTimeOffset.UtcNow,
                AudioTrackHealth.Healthy,
                AudioTrackHealth.Healthy,
                null,
                _format,
                new[]
                {
                    CreateArtifact(MeetingRecordingTrackKind.System, finalized: true),
                    CreateArtifact(MeetingRecordingTrackKind.Microphone, finalized: true),
                    CreateArtifact(MeetingRecordingTrackKind.Mixed, finalized: true)
                },
                HasUsableAudio: true));
        }

        public ValueTask DisposeAsync()
        {
            _status = IdleStatus();
            return ValueTask.CompletedTask;
        }

        private static MeetingRecorderRuntimeStatus IdleStatus() => new(
            null,
            null,
            AudioTrackHealth.Unknown,
            AudioTrackHealth.Unknown,
            null);

        private MeetingRecordingTrackArtifact CreateArtifact(
            MeetingRecordingTrackKind kind,
            bool finalized)
        {
            var extension = _format == MeetingRecordingFormat.AacM4a ? ".m4a" : ".wav";
            var name = kind.ToString().ToLowerInvariant();
            return new MeetingRecordingTrackArtifact
            {
                Kind = kind,
                FileName = finalized ? name + extension : string.Empty,
                InProgressFileName = finalized ? string.Empty : name + ".current" + extension,
                Container = _format == MeetingRecordingFormat.AacM4a ? "MPEG-4/M4A" : "WAV",
                Codec = _format == MeetingRecordingFormat.AacM4a ? "AAC-LC" : "PCM 16-bit",
                SampleRate = 48_000,
                ChannelCount = kind == MeetingRecordingTrackKind.System ? 2 : 1,
                Bitrate = _format == MeetingRecordingFormat.AacM4a ? 96_000 : 768_000,
                DurationSeconds = finalized ? 1 : 0,
                Bytes = finalized ? 12_000 : 0,
                HasAudioFrames = finalized,
                FinalizationState = finalized
                    ? MeetingRecordingFinalizationState.Finalized
                    : MeetingRecordingFinalizationState.InProgress,
                ValidationState = finalized
                    ? MeetingRecordingValidationState.Valid
                    : MeetingRecordingValidationState.Unknown
            };
        }
    }

    private sealed class UnusedAudioProcessor : IMeetingAudioProcessor
    {
        public Task<MeetingAudioProcessingResult> ProcessAsync(
            MeetingAudioProcessingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by recording lifecycle tests.");
    }

    private sealed class UnusedTranscriptionProvider : ITranscriptionProvider
    {
        public string Name => "Unused";

        public Task<TranscriptionProviderResponse> TranscribeAsync(
            TranscriptionProviderRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by recording lifecycle tests.");
    }

    private sealed class UnusedAnalysisProvider : IMeetingAnalysisProvider
    {
        public string Name => "Unused";

        public Task<MeetingAnalysisProviderResponse> AnalyzeAsync(
            MeetingAnalysisProviderRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by recording lifecycle tests.");
    }
}
