using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
            ("AAC writer owns COM lifecycle on one MTA thread", AacWriterOwnsLifecycleOnOneMtaThread),
            ("three AAC writers keep isolated owner threads", ThreeAacWritersKeepIsolatedOwnerThreads),
            ("three Media Foundation AAC tracks finalize together", ThreeMediaFoundationAacTracksFinalizeTogether),
            ("AAC bounded queue keeps producers non-blocking", AacBoundedQueueKeepsProducersNonBlocking),
            ("AAC finalization failure preserves current file and permits retry", AacFinalizationFailurePreservesCurrentFileAndPermitsRetry),
            ("AAC abort and disposal release on owner thread", AacAbortAndDisposalReleaseOnOwnerThread),
            ("direct Media Foundation AAC writes finalized M4A", DirectMediaFoundationAacWritesFinalizedM4a),
            ("lossless writer creates WAV only", LosslessWriterCreatesWavOnly),
            ("real-time mixer supports partial source combinations", RealTimeMixerSupportsPartialSources),
            ("real-time mixer failure completes without hanging", RealTimeMixerFailureCompletesWithoutHanging),
            ("bounded writer backpressure is explicit", BoundedWriterBackpressureIsExplicit),
            ("encoder initialization failure has no WAV fallback", EncoderInitializationFailureHasNoWavFallback),
            ("mid-stream encoder failure remains retryable", MidStreamEncoderFailureRemainsRetryable),
            ("recording finalization failure is concise and retryable", RecordingFinalizationFailureIsConciseAndRetryable),
            ("recording format command uses local settings boundary", RecordingFormatCommandUsesLocalSettingsBoundary),
            ("recording metadata maps files by track kind", RecordingMetadataMapsFilesByTrackKind),
            ("transcription prefers finalized mixed audio", TranscriptionPrefersFinalizedMixedAudio),
            ("transcription multipart uploads exact mixed bytes", TranscriptionMultipartUploadsExactMixedBytes),
            ("transcription multipart configures diarization models", TranscriptionMultipartConfiguresDiarizationModels),
            ("transcription targets selected recording and snapshot", TranscriptionTargetsSelectedRecordingAndSnapshot),
            ("transcription publishes indeterminate runtime stage", TranscriptionPublishesIndeterminateRuntimeStage),
            ("transcription retry preserves published transcript", TranscriptionRetryPreservesPublishedTranscript),
            ("compact transcription rejects missing mixed track", CompactTranscriptionRejectsMissingMixedTrack),
            ("oversized compact chunks derive from mixed track", OversizedCompactChunksDeriveFromMixedTrack),
            ("imported audio is managed and range bounded", ImportedAudioIsManagedAndRangeBounded),
            ("meeting source commands persist and refresh", MeetingSourceCommandsPersistAndRefresh),
            ("transcript revision save command", TranscriptRevisionSaveCommand),
            ("meeting analysis runtime state is shared and cancellable", MeetingAnalysisRuntimeStateIsSharedAndCancellable),
            ("transcription cancellation is neutral and preserves sources", TranscriptionCancellationIsNeutralAndPreservesSources),
            ("analysis cancellation preserves previous success", AnalysisCancellationPreservesPreviousSuccess),
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

    private static async Task RecordingFinalizationFailureIsConciseAndRetryable()
    {
        await using var fixture = new RecordingFixture();
        fixture.Recorder.FailNextStop = true;
        var start = await fixture.SendAsync(
            "startMeetingRecording",
            new { meetingId = fixture.Meeting.Id.ToString("N") });
        Assert(start.Success, start.ErrorMessage ?? "Start command failed.");
        var recording = fixture.State.MeetingRecordings.Single(item => item.IsActive);

        var stop = await fixture.SendAsync(
            "stopMeetingRecording",
            new { recordingId = recording.Id.ToString("N") });
        const string expected =
            "Recording could not be finalized. " +
            "The incomplete files were preserved for diagnostics.";
        Assert(!stop.Success && stop.ErrorMessage == expected &&
               recording.State == MeetingRecordingState.Failed &&
               recording.LastError == expected,
            "Finalization failure must expose a concise retryable UI message.");
        Assert(recording.Tracks.Count == 3 &&
               recording.Tracks.All(track =>
                   track.ValidationState == MeetingRecordingValidationState.Invalid &&
                   track.Error.Contains("E_NOINTERFACE", StringComparison.Ordinal)),
            "Technical COM details must remain attached to failed track metadata.");
        Assert(fixture.Diagnostics.Any(message =>
                message.Contains("E_NOINTERFACE", StringComparison.Ordinal)),
            "Full finalization diagnostics must be logged outside the compact UI error.");
        Assert(fixture.Recorder.Status.RecordingId is null,
            "A terminal finalization failure must clear the runtime recording lock.");

        var retry = await fixture.SendAsync(
            "startMeetingRecording",
            new { meetingId = fixture.Meeting.Id.ToString("N") });
        Assert(retry.Success && fixture.Recorder.StartCount == 2,
            "Start recording must be available after a terminal finalization failure.");
    }

    private static async Task RecordingFormatCommandUsesLocalSettingsBoundary()
    {
        await using var fixture = new RecordingFixture();
        var change = await fixture.SendAsync(
            "setMeetingRecordingFormat",
            new { format = "Wav" });
        Assert(change.Success &&
               fixture.LocalSettings.MeetingAssistant.RecordingFormat == MeetingRecordingFormat.Wav &&
               fixture.LocalSettingsSaveCount == 1,
            "Switch to Lossless WAV must persist through the local-settings boundary.");

        var start = await fixture.SendAsync(
            "startMeetingRecording",
            new { meetingId = fixture.Meeting.Id.ToString("N") });
        Assert(start.Success, start.ErrorMessage ?? "Start command failed.");
        var blocked = await fixture.SendAsync(
            "setMeetingRecordingFormat",
            new { format = "AacM4a" });
        Assert(!blocked.Success &&
               fixture.LocalSettings.MeetingAssistant.RecordingFormat == MeetingRecordingFormat.Wav &&
               fixture.LocalSettingsSaveCount == 1,
            "Recording format must not change while a live recorder owns the runtime lock.");
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

    private static async Task AacWriterOwnsLifecycleOnOneMtaThread()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var factory = new TrackingAacSessionFactory();
            await using var writer = new MediaFoundationAacTrackWriter(factory);
            var startCaller = 0;
            var writeCaller = 0;
            var completeCaller = 0;
            await RunOnThreadAsync(ApartmentState.STA, async () =>
            {
                startCaller = Environment.CurrentManagedThreadId;
                await writer.StartAsync(new RecordingTrackWriterStartRequest(
                    MeetingRecordingTrackKind.Microphone,
                    directory,
                    "microphone",
                    PreferredChannels: 1,
                    RecordingId: Guid.NewGuid()));
            });
            await RunOnThreadAsync(ApartmentState.MTA, async () =>
            {
                writeCaller = Environment.CurrentManagedThreadId;
                await writer.WriteAsync(CreateToneFrame(48_000, 1, 0, 960));
            });
            var artifact = await RunOnThreadAsync(ApartmentState.STA, async () =>
            {
                completeCaller = Environment.CurrentManagedThreadId;
                return await writer.CompleteAsync();
            });

            var operations = factory.OperationsFor("microphone");
            var ownerThreads = operations.Select(item => item.ThreadId).Distinct().ToList();
            Assert(artifact.ValidationState == MeetingRecordingValidationState.Valid,
                "The owner-thread test session must produce a finalized artifact.");
            Assert(ownerThreads.Count == 1 &&
                   operations.All(item => item.ApartmentState == ApartmentState.MTA),
                "Initialize, WriteSample, Finalize, and COM release must use one MTA owner thread.");
            Assert(operations.Any(item => item.Operation == "Initialize") &&
                   operations.Any(item => item.Operation == "WriteSample") &&
                   operations.Any(item => item.Operation == "Finalize") &&
                   operations.Any(item => item.Operation == "Release"),
                "The owner-thread test must observe the full encoder lifecycle.");
            Assert(ownerThreads[0] != startCaller &&
                   ownerThreads[0] != writeCaller &&
                   ownerThreads[0] != completeCaller &&
                   new[] { startCaller, writeCaller, completeCaller }.Distinct().Count() == 3,
                "Caller thread changes must never transfer ownership of the encoder session.");
            Assert(writer.OwnerThreadId == ownerThreads[0] &&
                   writer.OwnerApartmentState == ApartmentState.MTA,
                "Writer diagnostics must expose the stable MTA owner thread.");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task ThreeAacWritersKeepIsolatedOwnerThreads()
    {
        var directory = CreateTemporaryDirectory();
        var writers = new List<MediaFoundationAacTrackWriter>();
        try
        {
            var factory = new TrackingAacSessionFactory();
            foreach (var (kind, name, channels) in new[]
                     {
                         (MeetingRecordingTrackKind.System, "system", 2),
                         (MeetingRecordingTrackKind.Microphone, "microphone", 1),
                         (MeetingRecordingTrackKind.Mixed, "mixed", 1)
                     })
            {
                var writer = new MediaFoundationAacTrackWriter(factory);
                writers.Add(writer);
                await writer.StartAsync(new RecordingTrackWriterStartRequest(
                    kind,
                    directory,
                    name,
                    PreferredChannels: channels,
                    RecordingId: Guid.NewGuid()));
            }

            await Task.WhenAll(writers.Select((writer, index) =>
                writer.WriteAsync(CreateToneFrame(
                    writer.InputFormat.SampleRate,
                    writer.InputFormat.Channels,
                    index * 960,
                    960)).AsTask()));
            var artifacts = await Task.WhenAll(writers.Select(writer => writer.CompleteAsync()));

            Assert(artifacts.All(item =>
                    item.ValidationState == MeetingRecordingValidationState.Valid),
                "System, microphone, and mixed writers must all finalize independently.");
            Assert(writers.Select(writer => writer.OwnerThreadId).Distinct().Count() == 3,
                "Concurrent AAC track writers must have isolated live owner threads.");
            foreach (var name in new[] { "system", "microphone", "mixed" })
            {
                var operations = factory.OperationsFor(name);
                Assert(operations.Select(item => item.ThreadId).Distinct().Count() == 1 &&
                       operations.All(item => item.ApartmentState == ApartmentState.MTA),
                    $"The {name} session crossed its MTA owner thread.");
            }
        }
        finally
        {
            foreach (var writer in writers)
            {
                await writer.DisposeAsync();
            }

            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task ThreeMediaFoundationAacTracksFinalizeTogether()
    {
        var directory = CreateTemporaryDirectory();
        var writers = new List<MediaFoundationAacTrackWriter>();
        try
        {
            foreach (var (kind, name, channels) in new[]
                     {
                         (MeetingRecordingTrackKind.System, "system-real", 2),
                         (MeetingRecordingTrackKind.Microphone, "microphone-real", 1),
                         (MeetingRecordingTrackKind.Mixed, "mixed-real", 1)
                     })
            {
                var writer = new MediaFoundationAacTrackWriter();
                writers.Add(writer);
                await writer.StartAsync(new RecordingTrackWriterStartRequest(
                    kind,
                    directory,
                    name,
                    PreferredChannels: channels,
                    RecordingId: Guid.NewGuid()));
            }

            await Task.WhenAll(writers.Select(writer =>
                WriteSyntheticToneAsync(writer, TimeSpan.FromSeconds(1))));
            var artifacts = await Task.WhenAll(writers.Select(writer => writer.CompleteAsync()));
            Assert(artifacts.All(artifact =>
                    artifact.ValidationState == MeetingRecordingValidationState.Valid &&
                    artifact.FinalizationState == MeetingRecordingFinalizationState.Finalized &&
                    File.Exists(Path.Combine(directory, artifact.FileName))),
                "Three live Media Foundation writers must finalize playable system, microphone, and mixed M4A tracks.");
            Assert(writers.Select(writer => writer.OwnerThreadId).Distinct().Count() == 3 &&
                   writers.All(writer => writer.OwnerApartmentState == ApartmentState.MTA),
                "Real Media Foundation tracks must retain separate live MTA owners.");
        }
        finally
        {
            foreach (var writer in writers)
            {
                await writer.DisposeAsync();
            }

            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task AacBoundedQueueKeepsProducersNonBlocking()
    {
        var directory = CreateTemporaryDirectory();
        using var gate = new ManualResetEventSlim();
        var factory = new TrackingAacSessionFactory(writeGate: gate);
        await using var writer = new MediaFoundationAacTrackWriter(factory);
        try
        {
            await writer.StartAsync(new RecordingTrackWriterStartRequest(
                MeetingRecordingTrackKind.System,
                directory,
                "bounded-aac",
                PreferredChannels: 2,
                RecordingId: Guid.NewGuid()));
            var frame = CreateToneFrame(48_000, 2, 0, 960);
            Assert(writer.TryWrite(frame) && factory.WriteEntered.Wait(TimeSpan.FromSeconds(2)),
                "The owner thread must enter the synthetic blocked WriteSample operation.");

            var timer = System.Diagnostics.Stopwatch.StartNew();
            var rejected = false;
            for (var index = 1; index < 2_000; index++)
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

            timer.Stop();
            Assert(rejected && timer.Elapsed < TimeSpan.FromSeconds(1) &&
                   writer.Error?.Contains("bounded", StringComparison.OrdinalIgnoreCase) == true,
                "TryWrite must reject a full bounded AAC queue promptly without blocking capture callbacks.");
        }
        finally
        {
            gate.Set();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await writer.AbortAsync(timeout.Token);
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task AacFinalizationFailurePreservesCurrentFileAndPermitsRetry()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var failedFactory = new TrackingAacSessionFactory(failFinalization: true);
            await using (var failed = new MediaFoundationAacTrackWriter(failedFactory))
            {
                await failed.StartAsync(new RecordingTrackWriterStartRequest(
                    MeetingRecordingTrackKind.Mixed,
                    directory,
                    "failed",
                    PreferredChannels: 1,
                    RecordingId: Guid.NewGuid()));
                await failed.WriteAsync(CreateToneFrame(48_000, 1, 0, 960));
                var artifact = await failed.CompleteAsync();
                Assert(artifact.FinalizationState == MeetingRecordingFinalizationState.Failed &&
                       artifact.ValidationState == MeetingRecordingValidationState.Invalid &&
                       artifact.FileName.Length == 0 &&
                       artifact.InProgressFileName == "failed.current.m4a" &&
                       artifact.Error.Contains("E_NOINTERFACE", StringComparison.Ordinal),
                    "A failed finalization must retain the current file without reporting it Ready.");
                Assert(failedFactory.OperationsFor("failed")
                        .Select(item => item.ThreadId).Distinct().Count() == 1,
                    "Failure cleanup must remain on the failed writer owner thread.");
            }

            var retryFactory = new TrackingAacSessionFactory();
            await using var retry = new MediaFoundationAacTrackWriter(retryFactory);
            await retry.StartAsync(new RecordingTrackWriterStartRequest(
                MeetingRecordingTrackKind.Mixed,
                directory,
                "retry",
                PreferredChannels: 1,
                RecordingId: Guid.NewGuid()));
            await retry.WriteAsync(CreateToneFrame(48_000, 1, 0, 960));
            Assert((await retry.CompleteAsync()).ValidationState ==
                   MeetingRecordingValidationState.Valid,
                "A terminal AAC finalization failure must not prevent another recording.");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task AacAbortAndDisposalReleaseOnOwnerThread()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            foreach (var (name, explicitAbort) in new[]
                     {
                         ("abort", true),
                         ("shutdown", false)
                     })
            {
                var factory = new TrackingAacSessionFactory();
                var writer = new MediaFoundationAacTrackWriter(factory);
                await writer.StartAsync(new RecordingTrackWriterStartRequest(
                    MeetingRecordingTrackKind.Microphone,
                    directory,
                    name,
                    PreferredChannels: 1,
                    RecordingId: Guid.NewGuid()));
                await writer.WriteAsync(CreateToneFrame(48_000, 1, 0, 960));
                if (explicitAbort)
                {
                    await writer.AbortAsync();
                }

                await writer.DisposeAsync();
                var operations = factory.OperationsFor(name);
                Assert(operations.Any(item => item.Operation == "Abort") &&
                       operations.Any(item => item.Operation == "Release") &&
                       operations.Select(item => item.ThreadId).Distinct().Count() == 1 &&
                       operations.All(item => item.ApartmentState == ApartmentState.MTA),
                    "Abort and application-shutdown disposal must release on the owner MTA thread.");
            }
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
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

            await using var repeated = new MediaFoundationAacTrackWriter();
            await repeated.StartAsync(new RecordingTrackWriterStartRequest(
                MeetingRecordingTrackKind.Mixed,
                directory,
                "mixed-repeat",
                PreferredChannels: 1,
                RecordingId: Guid.NewGuid()));
            await WriteSyntheticToneAsync(repeated, TimeSpan.FromSeconds(1));
            var repeatedArtifact = await repeated.CompleteAsync();
            Assert(repeatedArtifact.ValidationState == MeetingRecordingValidationState.Valid &&
                   File.Exists(Path.Combine(directory, repeatedArtifact.FileName)),
                "A second direct AAC writer must start and finalize after the first releases COM resources.");
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

    private static Task RecordingMetadataMapsFilesByTrackKind()
    {
        var orderings = new[]
        {
            new[]
            {
                MeetingRecordingTrackKind.Mixed,
                MeetingRecordingTrackKind.System,
                MeetingRecordingTrackKind.Microphone
            },
            new[]
            {
                MeetingRecordingTrackKind.Microphone,
                MeetingRecordingTrackKind.Mixed,
                MeetingRecordingTrackKind.System
            }
        };

        foreach (var ordering in orderings)
        {
            var state = AppState.CreateDefault();
            var recording = new MeetingRecording
            {
                State = MeetingRecordingState.Recording,
                RecordingFormat = MeetingRecordingFormat.AacM4a
            };
            state.MeetingRecordings.Add(recording);
            var tracks = ordering.Select(kind => CreateFinalTrackArtifact(
                kind,
                kind switch
                {
                    MeetingRecordingTrackKind.System => "system.m4a",
                    MeetingRecordingTrackKind.Microphone => "microphone.m4a",
                    _ => "mixed.m4a"
                })).ToList();
            var marked = new MeetingRecordingService(state).MarkRecorded(
                recording.Id,
                new MeetingRecordingStopResult(
                    DateTimeOffset.UtcNow,
                    AudioTrackHealth.Healthy,
                    AudioTrackHealth.Healthy,
                    null,
                    MeetingRecordingFormat.AacM4a,
                    tracks,
                    HasUsableAudio: true));

            Assert(marked &&
                   recording.SystemAudioFile == "system.m4a" &&
                   recording.MicrophoneFile == "microphone.m4a" &&
                   recording.MixedAudioFile == "mixed.m4a",
                "Finalized files must be assigned by TrackKind regardless of result ordering.");
        }

        return Task.CompletedTask;
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
            var systemPath = Path.Combine(directory, "system.m4a");
            var microphonePath = Path.Combine(directory, "microphone.m4a");
            var staleChunkPath = Path.Combine(directory, "transcription-999.m4a");
            File.WriteAllBytes(systemPath, Encoding.UTF8.GetBytes("distinct-system-fixture"));
            File.WriteAllBytes(microphonePath, Encoding.UTF8.GetBytes("distinct-microphone-fixture"));
            File.WriteAllBytes(staleChunkPath, Encoding.UTF8.GetBytes("stale-chunk"));
            var mixedHash = ComputeFileSha256(mixedPath);
            var systemHash = ComputeFileSha256(systemPath);
            var microphoneHash = ComputeFileSha256(microphonePath);
            var processor = new MeetingAudioProcessor();
            var result = await processor.ProcessAsync(new MeetingAudioProcessingRequest(
                Guid.NewGuid(),
                directory,
                systemPath,
                null,
                microphonePath,
                null,
                ExistingMixedAudioPath: mixedPath,
                RecordingFormat: MeetingRecordingFormat.AacM4a));
            Assert(result.MixedAudioPath == mixedPath &&
                   result.OrderedChunkPaths.SequenceEqual(new[] { mixedPath }) &&
                   ComputeFileSha256(result.OrderedChunkPaths.Single()) == mixedHash &&
                   mixedHash != systemHash &&
                   mixedHash != microphoneHash &&
                   !File.Exists(staleChunkPath) &&
                   Directory.EnumerateFiles(directory, "mixed.wav").Any() == false,
                "Transcription must select the exact finalized mixed M4A bytes, clean stale chunks, and never select source tracks.");

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

    private static async Task TranscriptionMultipartUploadsExactMixedBytes()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var microphoneBytes = Encoding.UTF8.GetBytes("microphone-fixture-A");
            var systemBytes = Encoding.UTF8.GetBytes("system-fixture-B");
            var mixedBytes = Encoding.UTF8.GetBytes("mixed-fixture-C-with-both-sources");
            File.WriteAllBytes(Path.Combine(directory, "microphone.m4a"), microphoneBytes);
            File.WriteAllBytes(Path.Combine(directory, "system.m4a"), systemBytes);
            var mixedPath = Path.Combine(directory, "mixed.m4a");
            File.WriteAllBytes(mixedPath, mixedBytes);
            var handler = new CapturingMultipartHandler();
            var diagnostics = new List<string>();
            using var client = new HttpClient(handler);
            var recordingId = Guid.NewGuid();
            var meetingId = Guid.NewGuid();
            var provider = new OpenAiTranscriptionProvider(
                () => "synthetic-key",
                client,
                (message, _) => diagnostics.Add(message));

            await provider.TranscribeAsync(new TranscriptionProviderRequest(
                mixedPath,
                "test-model",
                MeetingTranscriptLanguage.Russian,
                TimeSpan.Zero,
                recordingId,
                meetingId));

            var expectedHash = Convert.ToHexString(SHA256.HashData(mixedBytes));
            Assert(handler.FileName == "mixed.m4a" &&
                   handler.ContentType == "audio/mp4" &&
                   handler.UploadedBytes.SequenceEqual(mixedBytes) &&
                   !handler.UploadedBytes.SequenceEqual(microphoneBytes) &&
                   !handler.UploadedBytes.SequenceEqual(systemBytes) &&
                   Convert.ToHexString(SHA256.HashData(handler.UploadedBytes)) == expectedHash,
                "Multipart upload must contain the exact selected mixed.m4a bytes.");
            Assert(diagnostics.Any(message =>
                       message.Contains(recordingId.ToString("N"), StringComparison.Ordinal) &&
                       message.Contains("fileName=mixed.m4a", StringComparison.Ordinal) &&
                       message.Contains($"bytes={mixedBytes.Length}", StringComparison.Ordinal) &&
                       message.Contains($"sha256={expectedHash}", StringComparison.Ordinal)),
                "Provider diagnostics must identify the exact multipart filename, size, hash, and recording ID.");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task TranscriptionMultipartConfiguresDiarizationModels()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var audioPath = Path.Combine(directory, "mixed.m4a");
            File.WriteAllBytes(audioPath, Encoding.UTF8.GetBytes("synthetic mixed audio"));

            async Task<IReadOnlyDictionary<string, string>> CaptureFieldsAsync(string model)
            {
                var handler = new CapturingMultipartHandler();
                using var client = new HttpClient(handler);
                var provider = new OpenAiTranscriptionProvider(
                    () => "synthetic-key",
                    client);
                await provider.TranscribeAsync(new TranscriptionProviderRequest(
                    audioPath,
                    model,
                    MeetingTranscriptLanguage.Auto,
                    TimeSpan.Zero));
                return handler.Fields;
            }

            var diarized = await CaptureFieldsAsync("gpt-4o-transcribe-diarize");
            Assert(diarized.GetValueOrDefault("response_format") == "diarized_json" &&
                   diarized.GetValueOrDefault("chunking_strategy") == "auto",
                "Diarization requests must send diarized_json with automatic chunking.");

            foreach (var model in new[] { "gpt-4o-transcribe", "gpt-4o-mini-transcribe" })
            {
                var normal = await CaptureFieldsAsync(model);
                Assert(normal.GetValueOrDefault("response_format") == "json" &&
                       !normal.ContainsKey("chunking_strategy"),
                    $"Normal transcription model {model} must not send diarization fields.");
            }
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task TranscriptionTargetsSelectedRecordingAndSnapshot()
    {
        await using var fixture = new TranscriptionFixture();
        var recordingA = fixture.AddRecordedWav("recording-a", 330);
        var recordingB = fixture.AddRecordedWav("recording-b", 660);

        var result = await fixture.SendAsync(recordingB.Id);
        Assert(result.Success, result.ErrorMessage ?? "Selected recording transcription failed.");
        Assert(fixture.Processor.Requests.Single().RecordingId == recordingB.Id &&
               fixture.Provider.Requests.Single().RecordingId == recordingB.Id &&
               fixture.Provider.Requests.Single().AudioPath.EndsWith(
                   $"{recordingB.Id:N}{Path.DirectorySeparatorChar}mixed.wav",
                   StringComparison.OrdinalIgnoreCase),
            "Connected command must process and upload the selected recording B only.");
        Assert(recordingA.State == MeetingRecordingState.Recorded &&
               recordingA.TranscriptFile.Length == 0 &&
               recordingB.State == MeetingRecordingState.TranscriptReady &&
               recordingB.TranscriptFile == "transcript.json",
            "Transcript metadata must attach only to selected recording B.");

        var snapshot = fixture.Snapshot();
        var snapshotA = snapshot.MeetingRecordings.Single(item => item.Id == recordingA.Id.ToString("N"));
        var snapshotB = snapshot.MeetingRecordings.Single(item => item.Id == recordingB.Id.ToString("N"));
        Assert(snapshotA.TranscriptText.Length == 0 &&
               snapshotB.TranscriptText == fixture.Provider.TranscriptFor(recordingB.Id),
            "Workspace snapshot must load transcript text from the matching recording folder and ID.");
    }

    private static async Task TranscriptionRetryPreservesPublishedTranscript()
    {
        await using var fixture = new TranscriptionFixture();
        var recording = fixture.AddRecordedWav("retry-recording", 440);
        var first = await fixture.SendAsync(recording.Id);
        Assert(first.Success, first.ErrorMessage ?? "Initial transcription failed.");
        var firstHash = fixture.Provider.Hashes.Single();

        var folder = fixture.Storage.ResolveFolder(recording.RecordingFolderRelativePath);
        var staleChunk = Path.Combine(folder, "transcription-999.wav");
        File.WriteAllBytes(staleChunk, Encoding.UTF8.GetBytes("stale chunk from prior attempt"));
        recording.TranscriptionChunkFiles.Add(Path.GetFileName(staleChunk));
        WriteWaveTone(Path.Combine(folder, "mixed.wav"), 880);
        fixture.Provider.BeforeTranscribe = () =>
        {
            Assert(recording.TranscriptFile == "transcript.json" &&
                   recording.TranscriptRawFile.Length > 0 &&
                   recording.TranscriptMarkdownFile.Length > 0 &&
                   !File.Exists(staleChunk) &&
                   File.Exists(Path.Combine(folder, "transcript.json")),
                "Retry must clear stale temporary chunks without removing the last published transcript.");
        };

        var retry = await fixture.SendAsync(recording.Id);
        Assert(retry.Success, retry.ErrorMessage ?? "Retry transcription failed.");
        Assert(fixture.Provider.Hashes.Count == 2 &&
               fixture.Provider.Hashes[1] != firstHash &&
               recording.State == MeetingRecordingState.TranscriptReady &&
               recording.TranscriptionChunkFiles.SequenceEqual(new[] { "mixed.wav" }) &&
               fixture.Snapshot().MeetingRecordings.Single(item =>
                   item.Id == recording.Id.ToString("N")).TranscriptText ==
                   fixture.Provider.TranscriptFor(recording.Id),
            "Retry must use current mixed bytes and expose only the newly generated transcript.");
    }

    private static async Task CompactTranscriptionRejectsMissingMixedTrack()
    {
        await using var fixture = new TranscriptionFixture();
        var recording = fixture.AddCompactWithoutMixed();

        var result = await fixture.SendAsync(recording.Id);
        Assert(!result.Success &&
               recording.State == MeetingRecordingState.Failed &&
               recording.LastError.Contains("finalized mixed", StringComparison.OrdinalIgnoreCase) &&
               fixture.Processor.Requests.Count == 0 &&
               fixture.Provider.Requests.Count == 0,
            "Compact recording without a valid Mixed artifact must fail without microphone/system fallback.");
    }

    private static async Task OversizedCompactChunksDeriveFromMixedTrack()
    {
        const long maximumChunkBytes = 256 * 1024;
        var directory = CreateTemporaryDirectory();
        try
        {
            var systemPath = Path.Combine(directory, "system.m4a");
            var microphonePath = Path.Combine(directory, "microphone.m4a");
            File.WriteAllBytes(systemPath, Encoding.UTF8.GetBytes("system-only-fixture"));
            File.WriteAllBytes(microphonePath, Encoding.UTF8.GetBytes("microphone-only-fixture"));
            await using (var writer = new MediaFoundationAacTrackWriter())
            {
                await writer.StartAsync(new RecordingTrackWriterStartRequest(
                    MeetingRecordingTrackKind.Mixed,
                    directory,
                    "mixed",
                    PreferredChannels: 1));
                await WriteSyntheticToneAsync(writer, TimeSpan.FromSeconds(35), 735);
                var artifact = await writer.CompleteAsync();
                Assert(artifact.ValidationState == MeetingRecordingValidationState.Valid,
                    "Oversized mixed fixture did not finalize.");
            }

            var mixedPath = Path.Combine(directory, "mixed.m4a");
            Assert(new FileInfo(mixedPath).Length > maximumChunkBytes,
                "Oversized fixture must exceed the configured chunk limit.");
            var staleChunk = Path.Combine(directory, "transcription-999.m4a");
            File.WriteAllBytes(staleChunk, Encoding.UTF8.GetBytes("stale"));
            var processor = new MeetingAudioProcessor();
            var result = await processor.ProcessAsync(new MeetingAudioProcessingRequest(
                Guid.NewGuid(),
                directory,
                systemPath,
                null,
                microphonePath,
                null,
                MaximumChunkBytes: maximumChunkBytes,
                ExistingMixedAudioPath: mixedPath,
                RecordingFormat: MeetingRecordingFormat.AacM4a));

            var chunkNames = result.OrderedChunkPaths.Select(Path.GetFileName).ToList();
            Assert(result.MixedAudioPath == mixedPath &&
                   result.OrderedChunkPaths.Count > 1 &&
                   !File.Exists(staleChunk) &&
                   chunkNames.SequenceEqual(chunkNames.OrderBy(name => name, StringComparer.Ordinal)) &&
                   result.OrderedChunkPaths.All(path =>
                       path.StartsWith(directory, StringComparison.OrdinalIgnoreCase) &&
                       Path.GetFileName(path).StartsWith("transcription-", StringComparison.Ordinal) &&
                       new FileInfo(path).Length <= maximumChunkBytes &&
                       ComputeFileSha256(path).Length == 64 &&
                       !string.Equals(path, systemPath, StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(path, microphonePath, StringComparison.OrdinalIgnoreCase)),
                "Oversized Compact chunks must be ordered current-folder artifacts derived only from mixed.m4a.");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task TranscriptionPublishesIndeterminateRuntimeStage()
    {
        await using var fixture = new TranscriptionFixture();
        var recording = fixture.AddRecordedWav("long-transcription", 550);
        var providerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseProvider = new ManualResetEventSlim(false);
        fixture.Provider.BeforeTranscribe = () =>
        {
            providerStarted.TrySetResult();
            releaseProvider.Wait();
        };

        var running = Task.Run(() => fixture.SendAsync(recording.Id));
        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var operation = fixture.Coordinator.ProcessingOperations.Single();
        Assert(operation.Kind == "Transcription" &&
               operation.Stage == "Transcribing" &&
               operation.RecordingId == recording.Id.ToString("N") &&
               operation.StartedAtUtc <= DateTimeOffset.UtcNow,
            "Long transcription must publish its real indeterminate stage and start time.");

        var duplicate = await fixture.SendAsync(recording.Id);
        Assert(!duplicate.Success && fixture.Provider.Requests.Count == 0,
            "A duplicate transcription must be rejected before another provider request starts.");
        releaseProvider.Set();
        var completed = await running;
        Assert(completed.Success && fixture.Coordinator.ProcessingOperations.Count == 0,
            "Successful transcription must clear its transient runtime operation.");
    }

    private static async Task ImportedAudioIsManagedAndRangeBounded()
    {
        var importDirectory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(importDirectory, "external-source.wav");
            WriteWaveTone(sourcePath, 525);
            var interaction = new FakeMeetingSourceInteraction { AudioPath = sourcePath };
            await using var fixture = new TranscriptionFixture(interaction);

            var unsupportedPath = Path.Combine(importDirectory, "external-source.flac");
            File.WriteAllText(unsupportedPath, "unsupported fixture");
            interaction.AudioPath = unsupportedPath;
            var unsupported = await fixture.SendCommandAsync(
                "importMeetingAudio",
                new { meetingId = fixture.Meeting.Id.ToString("N") });
            Assert(!unsupported.Success && fixture.State.MeetingRecordings.Count == 0,
                "Unsupported audio formats should fail clearly without creating metadata.");
            interaction.AudioPath = sourcePath;

            var import = await fixture.SendCommandAsync(
                "importMeetingAudio",
                new { meetingId = fixture.Meeting.Id.ToString("N") });
            Assert(import.Success, import.ErrorMessage ?? "Audio import failed.");
            var recording = fixture.State.MeetingRecordings.Single();
            var folder = fixture.Storage.ResolveFolder(recording.RecordingFolderRelativePath);
            var managedPath = Path.Combine(folder, recording.ManagedFileName);
            Assert(recording.SourceKind == MeetingRecordingSourceKind.Imported &&
                   recording.OriginalFileName == "external-source.wav" &&
                   recording.MixedAudioFile == recording.ManagedFileName &&
                   File.Exists(managedPath),
                "Imported audio should be copied into managed storage with provenance metadata.");

            File.Delete(sourcePath);
            Assert(File.Exists(managedPath),
                "The managed audio copy must survive deletion of the external source.");

            var range = await fixture.SendCommandAsync(
                "setImportedAudioRange",
                new
                {
                    recordingId = recording.Id.ToString("N"),
                    fromSeconds = 0.1,
                    untilSeconds = 0.4
                });
            var invalidRange = await fixture.SendCommandAsync(
                "setImportedAudioRange",
                new
                {
                    recordingId = recording.Id.ToString("N"),
                    fromSeconds = 0.4,
                    untilSeconds = 0.1
                });
            Assert(range.Success && !invalidRange.Success &&
                   recording.ProcessFromSeconds == 0.1 &&
                   recording.ProcessUntilSeconds == 0.4,
                "Imported audio ranges should validate without mutating the managed original.");

            var transcription = await fixture.SendAsync(recording.Id);
            Assert(transcription.Success &&
                   fixture.Processor.Requests.Single().ProcessFromSeconds == 0.1 &&
                   fixture.Processor.Requests.Single().ProcessUntilSeconds == 0.4,
                "The connected transcription path should forward the selected import range.");

            var processingDirectory = Path.Combine(importDirectory, "range-output");
            Directory.CreateDirectory(processingDirectory);
            var processed = await new MeetingAudioProcessor().ProcessAsync(
                new MeetingAudioProcessingRequest(
                    Guid.NewGuid(),
                    processingDirectory,
                    null,
                    null,
                    null,
                    null,
                    ExistingMixedAudioPath: managedPath,
                    RecordingFormat: MeetingRecordingFormat.Wav,
                    ProcessFromSeconds: 0.1,
                    ProcessUntilSeconds: 0.4));
            Assert(Math.Abs(processed.Duration.TotalSeconds - 0.3) < 0.02 &&
                   processed.OrderedChunkPaths.Count == 1 &&
                   processed.OrderedChunkPaths.All(path =>
                       Path.GetExtension(path).Equals(".m4a", StringComparison.OrdinalIgnoreCase)) &&
                   !File.Exists(Path.Combine(processingDirectory, "mixed.wav")),
                "Range processing should encode only the selected interval without a full WAV intermediary.");
        }
        finally
        {
            DeleteTemporaryDirectory(importDirectory);
        }
    }

    private static async Task MeetingSourceCommandsPersistAndRefresh()
    {
        var importDirectory = CreateTemporaryDirectory();
        try
        {
            var transcriptPath = Path.Combine(importDirectory, "meeting.srt");
            File.WriteAllText(
                transcriptPath,
                "1\n00:00:01,000 --> 00:00:02,000\nA: Decision one\n\n" +
                "2\n00:00:02,000 --> 00:00:03,000\nB: Follow-up\n");
            var screenshotPath = Path.Combine(importDirectory, "capture.png");
            File.WriteAllBytes(
                screenshotPath,
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
            var capturedAtUtc = DateTimeOffset.Parse("2026-07-16T17:00:00Z");
            var interaction = new FakeMeetingSourceInteraction
            {
                TranscriptPath = transcriptPath,
                Screenshot = new MeetingScreenshotCaptureResult(
                    screenshotPath,
                    1,
                    1,
                    MeetingScreenshotSourceKind.Window,
                    "Test window",
                    capturedAtUtc)
            };
            var analysisProvider = new CapturingAnalysisProvider();
            await using var fixture = new TranscriptionFixture(interaction, analysisProvider);

            var import = await fixture.SendCommandAsync(
                "importMeetingTranscript",
                new
                {
                    meetingId = fixture.Meeting.Id.ToString("N"),
                    sourceLabel = "Manual SRT"
                });
            Assert(import.Success, import.ErrorMessage ?? "Transcript import failed.");
            var transcript = fixture.State.MeetingTranscripts.Single();
            Assert(fixture.Store.Load().MeetingTranscripts.Single().Id == transcript.Id &&
                   fixture.Meeting.ActiveTranscriptId == transcript.Id,
                "Connected transcript import should save and select the durable version.");

            var analyze = await fixture.SendCommandAsync(
                "analyzeMeetingTranscript",
                new { transcriptId = transcript.Id.ToString("N") });
            Assert(analyze.Success && analysisProvider.Requests.Single().RecordingId is null &&
                   analysisProvider.Requests.Single().TranscriptId == transcript.Id &&
                   analysisProvider.Requests.Single().TranscriptRevisionId == transcript.RevisionId &&
                   analysisProvider.Requests.Single().Transcript.Text.Contains(
                       "A: Decision one",
                       StringComparison.Ordinal),
                "An imported transcript should be analyzable directly with its current revision.");

            var screenshot = await fixture.SendCommandAsync(
                "captureMeetingScreenshot",
                new { meetingId = fixture.Meeting.Id.ToString("N") });
            var untimedScreenshot = fixture.State.MeetingScreenshots.Single();
            Assert(screenshot.Success &&
                   untimedScreenshot.CapturedAtUtc == capturedAtUtc &&
                   untimedScreenshot.RecordingId is null &&
                   untimedScreenshot.OffsetFromRecordingStartSeconds is null &&
                   !File.Exists(screenshotPath),
                "A screenshot outside recording should persist without a recording offset and clean its temporary source.");

            var start = await fixture.SendCommandAsync(
                "startMeetingRecording",
                new { meetingId = fixture.Meeting.Id.ToString("N") });
            Assert(start.Success, start.ErrorMessage ?? "Recording start failed.");
            var activeRecording = fixture.State.MeetingRecordings.Single();
            var activeScreenshotPath = Path.Combine(importDirectory, "capture-active.png");
            File.WriteAllBytes(
                activeScreenshotPath,
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
            interaction.Screenshot = new MeetingScreenshotCaptureResult(
                activeScreenshotPath,
                1,
                1,
                MeetingScreenshotSourceKind.Display,
                "Test display",
                activeRecording.StartedAtUtc!.Value.AddSeconds(7));
            var activeCapture = await fixture.SendCommandAsync(
                "captureMeetingScreenshot",
                new { meetingId = fixture.Meeting.Id.ToString("N") });
            var timedScreenshot = fixture.State.MeetingScreenshots.Single(item =>
                item.Id != untimedScreenshot.Id);
            Assert(activeCapture.Success &&
                   timedScreenshot.RecordingId == activeRecording.Id &&
                   Math.Abs(timedScreenshot.OffsetFromRecordingStartSeconds!.Value - 7) < 0.01,
                "A screenshot during recording should retain the active recording ID and timestamp offset.");
            var stop = await fixture.SendCommandAsync(
                "stopMeetingRecording",
                new { recordingId = activeRecording.Id.ToString("N") });
            Assert(stop.Success, stop.ErrorMessage ?? "Recording stop failed.");

            var saved = fixture.Store.Load();
            Assert(saved.MeetingTranscripts.Count == 1 &&
                   saved.MeetingAnalyses.Count == 1 &&
                   saved.MeetingScreenshots.Count == 2,
                "Transcript, analysis, and screenshot metadata should persist through AppStateStore.");

            var snapshot = fixture.SnapshotWithSources();
            Assert(snapshot.MeetingTranscripts.Single().Segments.Count == 2 &&
                   snapshot.MeetingAnalyses.Single().TranscriptRevisionId ==
                   transcript.RevisionId.ToString("N") &&
                   snapshot.MeetingScreenshots.All(item => item.IsAvailable),
                "A fresh connected snapshot should expose all newly saved MEET sources immediately.");
        }
        finally
        {
            DeleteTemporaryDirectory(importDirectory);
        }
    }

    private static async Task TranscriptRevisionSaveCommand()
    {
        var importDirectory = CreateTemporaryDirectory();
        try
        {
            var transcriptPath = Path.Combine(importDirectory, "editable.srt");
            File.WriteAllText(
                transcriptPath,
                "1\n00:00:01,000 --> 00:00:02,000\nA: First point\n\n" +
                "2\n00:00:02,000 --> 00:00:03,000\nB: Second point\n");
            var interaction = new FakeMeetingSourceInteraction { TranscriptPath = transcriptPath };
            await using var fixture = new TranscriptionFixture(interaction);
            var import = await fixture.SendCommandAsync(
                "importMeetingTranscript",
                new { meetingId = fixture.Meeting.Id.ToString("N") });
            Assert(import.Success, import.ErrorMessage ?? "Transcript import failed.");
            var parent = fixture.State.MeetingTranscripts.Single();
            var speakerA = parent.Speakers.Single(speaker => speaker.OriginalLabel == "A").SpeakerId;
            var speakerB = parent.Speakers.Single(speaker => speaker.OriginalLabel == "B").SpeakerId;
            var stateChangesBefore = fixture.StateChangedCount;

            object ValidPayload() => new
            {
                meetingId = fixture.Meeting.Id.ToString("N"),
                transcriptId = parent.Id.ToString("N"),
                parentRevisionId = parent.RevisionId.ToString("N"),
                segmentEdits = new[] { new { index = 0, text = "Edited first point" } },
                speakers = new[]
                {
                    new { speakerId = speakerA, displayName = "Alexandra", isCurrentUser = true },
                    new { speakerId = speakerB, displayName = "Boris", isCurrentUser = false }
                },
                merges = Array.Empty<object>()
            };

            var invalidMeeting = await fixture.SendCommandAsync(
                "saveMeetingTranscriptRevision",
                new
                {
                    meetingId = Guid.NewGuid().ToString("N"),
                    transcriptId = parent.Id.ToString("N"),
                    parentRevisionId = parent.RevisionId.ToString("N"),
                    speakers = new[]
                    {
                        new { speakerId = speakerA, displayName = "A", isCurrentUser = false },
                        new { speakerId = speakerB, displayName = "B", isCurrentUser = false }
                    }
                });
            var invalidTranscript = await fixture.SendCommandAsync(
                "saveMeetingTranscriptRevision",
                new
                {
                    meetingId = fixture.Meeting.Id.ToString("N"),
                    transcriptId = Guid.NewGuid().ToString("N"),
                    parentRevisionId = parent.RevisionId.ToString("N")
                });
            var invalidRevision = await fixture.SendCommandAsync(
                "saveMeetingTranscriptRevision",
                new
                {
                    meetingId = fixture.Meeting.Id.ToString("N"),
                    transcriptId = parent.Id.ToString("N"),
                    parentRevisionId = Guid.NewGuid().ToString("N")
                });
            var invalidSpeaker = await fixture.SendCommandAsync(
                "saveMeetingTranscriptRevision",
                new
                {
                    meetingId = fixture.Meeting.Id.ToString("N"),
                    transcriptId = parent.Id.ToString("N"),
                    parentRevisionId = parent.RevisionId.ToString("N"),
                    speakers = new[]
                    {
                        new { speakerId = "speaker-ghost", displayName = "Ghost", isCurrentUser = false }
                    }
                });
            var invalidSegment = await fixture.SendCommandAsync(
                "saveMeetingTranscriptRevision",
                new
                {
                    meetingId = fixture.Meeting.Id.ToString("N"),
                    transcriptId = parent.Id.ToString("N"),
                    parentRevisionId = parent.RevisionId.ToString("N"),
                    segmentEdits = new[] { new { index = 99, text = "text" } },
                    speakers = new[]
                    {
                        new { speakerId = speakerA, displayName = "A", isCurrentUser = false },
                        new { speakerId = speakerB, displayName = "B", isCurrentUser = false }
                    }
                });
            var malformedPayload = await fixture.SendCommandAsync(
                "saveMeetingTranscriptRevision",
                new
                {
                    meetingId = fixture.Meeting.Id.ToString("N"),
                    transcriptId = parent.Id.ToString("N"),
                    parentRevisionId = parent.RevisionId.ToString("N"),
                    segmentEdits = "not-an-array"
                });
            Assert(!invalidMeeting.Success && !invalidTranscript.Success &&
                   !invalidRevision.Success && !invalidSpeaker.Success &&
                   !invalidSegment.Success && !malformedPayload.Success &&
                   fixture.State.MeetingTranscripts.Count == 1 &&
                   fixture.Meeting.ActiveTranscriptId == parent.Id &&
                   fixture.StateChangedCount == stateChangesBefore,
                "Invalid revision-save payloads must be rejected without state changes.");

            var save = await fixture.SendCommandAsync(
                "saveMeetingTranscriptRevision",
                ValidPayload());
            Assert(save.Success, save.ErrorMessage ?? "Revision save failed.");
            var revision = fixture.State.MeetingTranscripts.Single(item => item.Id != parent.Id);
            Assert(fixture.Meeting.ActiveTranscriptId == revision.Id &&
                   revision.SourceTranscriptId == parent.Id &&
                   revision.ParentRevisionId == parent.RevisionId &&
                   fixture.StateChangedCount == stateChangesBefore + 1 &&
                   fixture.Store.Load().MeetingTranscripts.Count == 2,
                "A valid revision save should persist, activate the new revision, and notify once.");

            // A duplicate submission built against the old parent revision is
            // rejected instead of silently creating a second identical revision.
            var duplicate = await fixture.SendCommandAsync(
                "saveMeetingTranscriptRevision",
                ValidPayload());
            Assert(!duplicate.Success && fixture.State.MeetingTranscripts.Count == 2,
                "Replaying the same save must not create another revision.");

            var snapshot = fixture.SnapshotWithSources();
            var snapshotRevision = snapshot.MeetingTranscripts
                .Single(item => item.Id == revision.Id.ToString("N"));
            Assert(snapshotRevision.IsActive &&
                   snapshotRevision.Origin == "UserEdited" &&
                   snapshotRevision.SourceTranscriptId == parent.Id.ToString("N") &&
                   snapshotRevision.Segments.First().Text == "Edited first point" &&
                   snapshotRevision.Segments.First().SpeakerName == "Alexandra" &&
                   snapshotRevision.Speakers.Single(speaker => speaker.IsCurrentUser).DisplayName == "Alexandra" &&
                   snapshot.MeetingTranscripts.Single(item => item.Id == parent.Id.ToString("N"))
                       .Segments.First().Text == "First point",
                "The fresh snapshot should expose the new active revision, mappings, and untouched parent.");
        }
        finally
        {
            DeleteTemporaryDirectory(importDirectory);
        }
    }

    private static async Task MeetingAnalysisRuntimeStateIsSharedAndCancellable()
    {
        var importDirectory = CreateTemporaryDirectory();
        try
        {
            var transcriptPath = Path.Combine(importDirectory, "runtime-state.srt");
            File.WriteAllText(
                transcriptPath,
                "1\n00:00:01,000 --> 00:00:02,000\nA: Shared operation state\n");
            var interaction = new FakeMeetingSourceInteraction { TranscriptPath = transcriptPath };
            var provider = new BlockingAnalysisProvider();
            await using var fixture = new TranscriptionFixture(interaction, provider);
            var import = await fixture.SendCommandAsync(
                "importMeetingTranscript",
                new
                {
                    meetingId = fixture.Meeting.Id.ToString("N"),
                    sourceLabel = "Imported phone recording"
                });
            Assert(import.Success, import.ErrorMessage ?? "Transcript import failed.");
            var transcript = fixture.State.MeetingTranscripts.Single();

            var running = fixture.SendCommandAsync(
                "analyzeMeetingTranscript",
                new { transcriptId = transcript.Id.ToString("N") });
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var operation = fixture.Coordinator.ProcessingOperations.Single();
            Assert(operation.Kind == "Analysis" &&
                   operation.Stage == "Analyzing" &&
                   operation.TranscriptId == transcript.Id.ToString("N") &&
                   operation.RecordingId is null,
                "Imported transcript analysis must expose authoritative runtime state without a recording.");
            var snapshot = WorkspaceSnapshotFactory.Create(
                fixture.State,
                mode: WorkspaceSnapshotFactory.ConnectedMode,
                meetingOperations: fixture.Coordinator.ProcessingOperations);
            Assert(snapshot.MeetingOperations.Single().TranscriptId == transcript.Id.ToString("N"),
                "Workspace snapshot must publish the same running transcript operation.");

            var duplicate = await fixture.SendCommandAsync(
                "analyzeMeetingTranscript",
                new { transcriptId = transcript.Id.ToString("N") });
            Assert(!duplicate.Success && provider.RequestCount == 1 &&
                   duplicate.ErrorMessage?.Contains("already has", StringComparison.OrdinalIgnoreCase) == true,
                "Coordinator must reject a duplicate provider request for the same transcript.");

            var cancel = await fixture.SendCommandAsync(
                "cancelMeetingProcessing",
                new { transcriptId = transcript.Id.ToString("N") });
            Assert(cancel.Success, cancel.ErrorMessage ?? "Transcript cancellation failed.");
            var cancelled = await running;
            Assert(cancelled.Success &&
                   cancelled.OutcomeCode == "cancelled" &&
                   fixture.Coordinator.ProcessingOperations.Count == 0 &&
                   fixture.State.MeetingTranscripts.Single().Id == transcript.Id,
                "Cancellation must clear runtime state while preserving the imported transcript.");
        }
        finally
        {
            DeleteTemporaryDirectory(importDirectory);
        }
    }

    private static async Task TranscriptionCancellationIsNeutralAndPreservesSources()
    {
        await using var fixture = new TranscriptionFixture();
        var recording = fixture.AddRecordedWav("original", 440);
        var originalPath = fixture.Storage.ResolveFile(recording, recording.MixedAudioFile);
        var first = await fixture.SendCommandAsync(
            "transcribeMeetingRecording",
            new { recordingId = recording.Id.ToString("N"), acceptUploadDisclosure = true });
        Assert(first.Success, first.ErrorMessage ?? "Initial transcription failed.");
        var transcript = fixture.State.MeetingTranscripts.Single();
        var transcriptPath = fixture.State.MeetingTranscripts.Single().NormalizedArtifactFile;

        fixture.Provider.BeforeTranscribe = () => fixture.Coordinator.CancelProcessing(recording.Id);
        var cancelled = await fixture.SendCommandAsync(
            "transcribeMeetingRecording",
            new { recordingId = recording.Id.ToString("N"), acceptUploadDisclosure = true });

        Assert(cancelled.Success && cancelled.OutcomeCode == "cancelled" &&
               cancelled.OutcomeMessage == "Transcription cancelled. Original audio was kept.",
            "Connected cancellation must be an explicit neutral command outcome.");
        Assert(recording.State == MeetingRecordingState.Ready && recording.LastError.Length == 0,
            "Cancelled transcription must return the recording to Ready without LastError.");
        Assert(File.Exists(originalPath) && fixture.State.MeetingTranscripts.Count == 1 &&
               fixture.State.MeetingTranscripts.Single().Id == transcript.Id &&
               fixture.State.MeetingTranscripts.Single().NormalizedArtifactFile == transcriptPath,
            "Late provider success after cancellation must not replace the transcript or remove original audio.");
        var saved = fixture.Store.Load().MeetingRecordings.Single(item => item.Id == recording.Id);
        Assert(saved.State == MeetingRecordingState.Ready && saved.LastError.Length == 0,
            "Neutral cancellation state must persist without becoming Failed after restart.");
    }

    private static async Task AnalysisCancellationPreservesPreviousSuccess()
    {
        var provider = new SuccessfulThenBlockingAnalysisProvider();
        await using var fixture = new TranscriptionFixture(analysisProvider: provider);
        var recording = fixture.AddRecordedWav("analysis", 550);
        var transcribe = await fixture.SendCommandAsync(
            "transcribeMeetingRecording",
            new { recordingId = recording.Id.ToString("N"), acceptUploadDisclosure = true });
        Assert(transcribe.Success, transcribe.ErrorMessage ?? "Transcription failed.");
        var transcript = fixture.State.MeetingTranscripts.Single();
        var first = await fixture.SendCommandAsync(
            "analyzeMeetingTranscript",
            new { transcriptId = transcript.Id.ToString("N") });
        Assert(first.Success && fixture.State.MeetingAnalyses.Count == 1,
            first.ErrorMessage ?? "Initial analysis failed.");
        var previousAnalysisId = fixture.State.MeetingAnalyses.Single().Id;

        var running = fixture.SendCommandAsync(
            "analyzeMeetingTranscript",
            new { transcriptId = transcript.Id.ToString("N") });
        await provider.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancel = await fixture.SendCommandAsync(
            "cancelMeetingProcessing",
            new { transcriptId = transcript.Id.ToString("N") });
        Assert(cancel.Success, cancel.ErrorMessage ?? "Analysis cancellation request failed.");
        var cancelled = await running;

        Assert(cancelled.Success && cancelled.OutcomeCode == "cancelled" &&
               cancelled.OutcomeMessage == "Analysis cancelled. Previous analysis was kept.",
            "Cancelled re-analysis must return a neutral outcome.");
        Assert(recording.State == MeetingRecordingState.Ready && recording.LastError.Length == 0,
            "Cancelled analysis must return the recording to Ready without LastError.");
        Assert(fixture.State.MeetingAnalyses.Count == 1 &&
               fixture.State.MeetingAnalyses.Single().Id == previousAnalysisId &&
               fixture.State.MeetingAnalyses.All(item => item.State != MeetingAnalysisState.Failed),
            "Cancellation must preserve the previous successful analysis and never create a Failed analysis.");
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
        TimeSpan duration,
        double frequency = 440)
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
                count,
                frequency));
        }
    }

    private static PcmAudioFrame CreateToneFrame(
        int sampleRate,
        int channels,
        long startFrame,
        int frameCount,
        double frequency = 440)
    {
        var bytes = new byte[frameCount * channels * sizeof(short)];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = (short)(Math.Sin(
                2 * Math.PI * frequency * (startFrame + frame) / sampleRate) * 8_000);
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

    private static MeetingRecordingTrackArtifact CreateFinalTrackArtifact(
        MeetingRecordingTrackKind kind,
        string fileName,
        long bytes = 1_024,
        double durationSeconds = 1) => new()
    {
        Kind = kind,
        FileName = fileName,
        Container = Path.GetExtension(fileName).Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            ? "MPEG-4/M4A"
            : "WAV",
        Codec = Path.GetExtension(fileName).Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            ? "AAC-LC"
            : "PCM 16-bit",
        SampleRate = 48_000,
        ChannelCount = kind == MeetingRecordingTrackKind.System ? 2 : 1,
        Bitrate = 96_000,
        DurationSeconds = durationSeconds,
        Bytes = bytes,
        HasAudioFrames = true,
        FinalizationState = MeetingRecordingFinalizationState.Finalized,
        ValidationState = MeetingRecordingValidationState.Valid
    };

    private static void WriteWaveTone(string path, double frequency)
    {
        const int sampleRate = 16_000;
        const int sampleCount = 8_000;
        var samples = new byte[sampleCount * sizeof(short)];
        for (var index = 0; index < sampleCount; index++)
        {
            var sample = (short)(Math.Sin(2 * Math.PI * frequency * index / sampleRate) * 8_000);
            samples[index * 2] = (byte)(sample & 0xff);
            samples[index * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }

        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1));
        writer.Write(samples, 0, samples.Length);
    }

    private static string ComputeFileSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

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

    private static Task RunOnThreadAsync(
        ApartmentState apartmentState,
        Func<Task> action) =>
        RunOnThreadAsync<object?>(apartmentState, async () =>
        {
            await action();
            return null;
        });

    private static Task<T> RunOnThreadAsync<T>(
        ApartmentState apartmentState,
        Func<Task<T>> action)
    {
        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(action().GetAwaiter().GetResult());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "TaskOverlay test caller"
        };
        thread.SetApartmentState(apartmentState);
        thread.Start();
        return completion.Task;
    }

    private sealed record AacOwnerOperation(
        string Session,
        string Operation,
        int ThreadId,
        ApartmentState ApartmentState);

    private sealed class TrackingAacSessionFactory : IMediaFoundationAacEncoderSessionFactory
    {
        private readonly object _sync = new();
        private readonly bool _failFinalization;
        private readonly ManualResetEventSlim? _writeGate;
        private readonly List<AacOwnerOperation> _operations = new();

        public TrackingAacSessionFactory(
            bool failFinalization = false,
            ManualResetEventSlim? writeGate = null)
        {
            _failFinalization = failFinalization;
            _writeGate = writeGate;
        }

        public ManualResetEventSlim WriteEntered { get; } = new();

        public IMediaFoundationAacEncoderSession Create(
            RecordingTrackWriterStartRequest request,
            string finalPath,
            string inProgressPath)
        {
            Record(request.BaseFileName, "Initialize");
            return new TrackingAacSession(
                this,
                request,
                finalPath,
                inProgressPath,
                _failFinalization,
                _writeGate);
        }

        public IReadOnlyList<AacOwnerOperation> OperationsFor(string session)
        {
            lock (_sync)
            {
                return _operations.Where(item => item.Session == session).ToList();
            }
        }

        public void Record(string session, string operation)
        {
            lock (_sync)
            {
                _operations.Add(new AacOwnerOperation(
                    session,
                    operation,
                    Environment.CurrentManagedThreadId,
                    Thread.CurrentThread.GetApartmentState()));
            }
        }
    }

    private sealed class TrackingAacSession : IMediaFoundationAacEncoderSession
    {
        private readonly TrackingAacSessionFactory _factory;
        private readonly string _session;
        private readonly string _finalPath;
        private readonly string _inProgressPath;
        private readonly bool _failFinalization;
        private readonly ManualResetEventSlim? _writeGate;
        private bool _hasFrames;

        public TrackingAacSession(
            TrackingAacSessionFactory factory,
            RecordingTrackWriterStartRequest request,
            string finalPath,
            string inProgressPath,
            bool failFinalization,
            ManualResetEventSlim? writeGate)
        {
            _factory = factory;
            _session = request.BaseFileName;
            _finalPath = finalPath;
            _inProgressPath = inProgressPath;
            _failFinalization = failFinalization;
            _writeGate = writeGate;
            InputFormat = new WaveFormat(48_000, 16, request.PreferredChannels);
            File.WriteAllBytes(_inProgressPath, new byte[] { 1 });
        }

        public WaveFormat InputFormat { get; }
        public int Bitrate => 96_000;

        public void WriteFrame(PcmAudioFrame frame)
        {
            _factory.Record(_session, "WriteSample");
            _factory.WriteEntered.Set();
            _writeGate?.Wait();
            _hasFrames = frame.Data.Length > 0;
        }

        public AacEncoderSessionCompletion Complete()
        {
            _factory.Record(_session, "Finalize");
            if (_failFinalization)
            {
                throw new COMException(
                    "Synthetic IMFSinkWriter finalization failed: E_NOINTERFACE.",
                    unchecked((int)0x80004002));
            }

            Assert(_hasFrames, "Tracking AAC session expected at least one frame.");
            File.WriteAllBytes(_finalPath, new byte[] { 1, 2, 3, 4 });
            File.Delete(_inProgressPath);
            return new AacEncoderSessionCompletion(4, TimeSpan.FromMilliseconds(20));
        }

        public void Abort(bool preserveInProgressFile)
        {
            _factory.Record(_session, "Abort");
            if (!preserveInProgressFile)
            {
                File.Delete(_inProgressPath);
            }
        }

        public void Dispose() => _factory.Record(_session, "Release");
    }

    private sealed class CapturingMultipartHandler : HttpMessageHandler
    {
        public byte[] UploadedBytes { get; private set; } = Array.Empty<byte>();
        public string FileName { get; private set; } = string.Empty;
        public string ContentType { get; private set; } = string.Empty;
        public IReadOnlyDictionary<string, string> Fields { get; private set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var multipart = request.Content as MultipartFormDataContent ??
                            throw new InvalidOperationException("Expected multipart transcription request.");
            var file = multipart.FirstOrDefault(content =>
                string.Equals(
                    content.Headers.ContentDisposition?.Name?.Trim('"'),
                    "file",
                    StringComparison.Ordinal)) ??
                       throw new InvalidOperationException("Multipart request has no audio file part.");
            UploadedBytes = await file.ReadAsByteArrayAsync(cancellationToken);
            FileName = (file.Headers.ContentDisposition?.FileNameStar ??
                        file.Headers.ContentDisposition?.FileName ?? string.Empty).Trim('"');
            ContentType = file.Headers.ContentType?.MediaType ?? string.Empty;
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var content in multipart.Where(content => !ReferenceEquals(content, file)))
            {
                var name = content.Headers.ContentDisposition?.Name?.Trim('"');
                if (!string.IsNullOrWhiteSpace(name))
                {
                    fields[name] = await content.ReadAsStringAsync(cancellationToken);
                }
            }

            Fields = fields;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"text\":\"synthetic transcript\"}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class FakeMeetingSourceInteraction : IMeetingSourceInteraction
    {
        public string? AudioPath { get; set; }
        public string? TranscriptPath { get; set; }
        public MeetingScreenshotCaptureResult? Screenshot { get; set; }

        public string? PickAudioFile() => AudioPath;

        public string? PickTranscriptFile() => TranscriptPath;

        public MeetingScreenshotCaptureResult? CaptureScreenshot() => Screenshot;
    }

    private sealed class CapturingAnalysisProvider : IMeetingAnalysisProvider
    {
        public string Name => "Synthetic analysis";
        public List<MeetingAnalysisProviderRequest> Requests { get; } = new();

        public Task<MeetingAnalysisProviderResponse> AnalyzeAsync(
            MeetingAnalysisProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var analysis = new MeetingAnalysis
            {
                RecordingId = request.RecordingId,
                TranscriptId = request.TranscriptId,
                TranscriptRevisionId = request.TranscriptRevisionId,
                MeetId = request.MeetId,
                State = MeetingAnalysisState.ReadyForReview,
                Provider = Name,
                Model = request.Model,
                Summary = "Synthetic source analysis",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            return Task.FromResult(new MeetingAnalysisProviderResponse(
                "{\"summary\":\"Synthetic source analysis\"}",
                analysis));
        }
    }

    private sealed class BlockingAnalysisProvider : IMeetingAnalysisProvider
    {
        public string Name => "blocking-analysis";
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int RequestCount { get; private set; }

        public async Task<MeetingAnalysisProviderResponse> AnalyzeAsync(
            MeetingAnalysisProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable blocking provider completion.");
        }
    }

    private sealed class SuccessfulThenBlockingAnalysisProvider : IMeetingAnalysisProvider
    {
        private int _requestCount;

        public string Name => "successful-then-blocking-analysis";
        public TaskCompletionSource SecondStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<MeetingAnalysisProviderResponse> AnalyzeAsync(
            MeetingAnalysisProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            _requestCount++;
            if (_requestCount > 1)
            {
                SecondStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var analysis = new MeetingAnalysis
            {
                RecordingId = request.RecordingId,
                TranscriptId = request.TranscriptId,
                TranscriptRevisionId = request.TranscriptRevisionId,
                MeetId = request.MeetId,
                State = MeetingAnalysisState.ReadyForReview,
                Provider = Name,
                Model = request.Model,
                Summary = "Durable previous analysis",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            return new MeetingAnalysisProviderResponse("{}", analysis);
        }
    }

    private sealed class TranscriptionFixture : IAsyncDisposable
    {
        private readonly string _directory = CreateTemporaryDirectory();

        public TranscriptionFixture(
            IMeetingSourceInteraction? sourceInteraction = null,
            IMeetingAnalysisProvider? analysisProvider = null)
        {
            State = AppState.CreateDefault();
            Meeting = new MeetingItem
            {
                ProjectId = State.Projects[0].Id,
                Title = "Transcription fixture MEET",
                StartsAtUtc = DateTimeOffset.UtcNow,
                DurationMinutes = 30,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            State.Meetings.Add(Meeting);
            Store = new AppStateStore(_directory);
            Storage = new MeetingRecordingStorage(_directory);
            Processor = new PassthroughAudioProcessor();
            Provider = new CapturingTranscriptionProvider();
            var settings = new LocalAppSettings
            {
                MeetingAssistant = new MeetingAssistantSettings
                {
                    ProviderUploadDisclosureAccepted = true,
                    TranscriptionModel = "test-model",
                    Language = MeetingTranscriptLanguage.Russian
                }
            };
            Coordinator = new MeetingAssistantCoordinator(
                State,
                settings,
                _directory,
                () => Store.Save(State),
                () => { },
                () => StateChangedCount++,
                new FakeMeetingRecorder(),
                Processor,
                Provider,
                analysisProvider ?? new UnusedAnalysisProvider(),
                (message, _) => Diagnostics.Add(message));
            Handler = new MeetingAssistantWorkspaceCommandHandler(
                Coordinator,
                sourceInteraction);
        }

        public AppState State { get; }
        public MeetingItem Meeting { get; }
        public AppStateStore Store { get; }
        public MeetingRecordingStorage Storage { get; }
        public PassthroughAudioProcessor Processor { get; }
        public CapturingTranscriptionProvider Provider { get; }
        public MeetingAssistantCoordinator Coordinator { get; }
        public MeetingAssistantWorkspaceCommandHandler Handler { get; }
        public List<string> Diagnostics { get; } = new();
        public int StateChangedCount { get; private set; }

        public MeetingRecording AddRecordedWav(string label, double frequency)
        {
            var id = Guid.NewGuid();
            var layout = Storage.CreateLayout(Meeting.Id, id);
            WriteWaveTone(layout.MixedAudioPath, frequency);
            using var reader = new WaveFileReader(layout.MixedAudioPath);
            var info = new FileInfo(layout.MixedAudioPath);
            var recording = new MeetingRecording
            {
                Id = id,
                MeetId = Meeting.Id,
                SourceKind = MeetingRecordingSourceKind.ManualMeet,
                State = MeetingRecordingState.Recorded,
                RecordingFormat = MeetingRecordingFormat.Wav,
                RecordingFolderRelativePath = layout.RelativeFolder,
                MixedAudioFile = Path.GetFileName(layout.MixedAudioPath),
                Tracks = new List<MeetingRecordingTrackArtifact>
                {
                    CreateFinalTrackArtifact(
                        MeetingRecordingTrackKind.Mixed,
                        Path.GetFileName(layout.MixedAudioPath),
                        info.Length,
                        reader.TotalTime.TotalSeconds)
                },
                LastError = label,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            State.MeetingRecordings.Add(recording);
            Store.Save(State);
            return recording;
        }

        public MeetingRecording AddCompactWithoutMixed()
        {
            var id = Guid.NewGuid();
            var layout = Storage.CreateLayout(Meeting.Id, id);
            File.WriteAllBytes(Path.Combine(layout.AbsoluteFolder, "system.m4a"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(layout.AbsoluteFolder, "microphone.m4a"), new byte[] { 4, 5, 6 });
            var recording = new MeetingRecording
            {
                Id = id,
                MeetId = Meeting.Id,
                SourceKind = MeetingRecordingSourceKind.ManualMeet,
                State = MeetingRecordingState.Recorded,
                RecordingFormat = MeetingRecordingFormat.AacM4a,
                RecordingFolderRelativePath = layout.RelativeFolder,
                SystemAudioFile = "system.m4a",
                MicrophoneFile = "microphone.m4a",
                Tracks = new List<MeetingRecordingTrackArtifact>
                {
                    CreateFinalTrackArtifact(MeetingRecordingTrackKind.System, "system.m4a"),
                    CreateFinalTrackArtifact(MeetingRecordingTrackKind.Microphone, "microphone.m4a")
                },
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            State.MeetingRecordings.Add(recording);
            Store.Save(State);
            return recording;
        }

        public async Task<WorkspaceCommandResult> SendAsync(Guid recordingId)
        {
            return await SendCommandAsync(
                "transcribeMeetingRecording",
                new
                {
                    recordingId = recordingId.ToString("N"),
                    acceptUploadDisclosure = true
                });
        }

        public async Task<WorkspaceCommandResult> SendCommandAsync(string type, object payload)
        {
            var command = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                commandId = Guid.NewGuid().ToString("N"),
                type,
                payload
            });
            return await Handler.TryHandleAsync(command) ??
                   throw new InvalidOperationException("Meeting source command was not recognized.");
        }

        public WorkspaceSnapshot Snapshot() => WorkspaceSnapshotFactory.Create(
            State,
            mode: WorkspaceSnapshotFactory.ConnectedMode,
            transcriptLoader: Coordinator.LoadTranscriptText);

        public WorkspaceSnapshot SnapshotWithSources() => WorkspaceSnapshotFactory.Create(
            State,
            mode: WorkspaceSnapshotFactory.ConnectedMode,
            transcriptLoader: Coordinator.LoadTranscriptText,
            meetingTranscriptLoader: Coordinator.LoadTranscriptContent,
            screenshotThumbnailLoader: Coordinator.LoadScreenshotThumbnailDataUrl);

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
            DeleteTemporaryDirectory(_directory);
        }
    }

    private sealed class PassthroughAudioProcessor : IMeetingAudioProcessor
    {
        public List<MeetingAudioProcessingRequest> Requests { get; } = new();

        public Task<MeetingAudioProcessingResult> ProcessAsync(
            MeetingAudioProcessingRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var path = request.ExistingMixedAudioPath ??
                       throw new InvalidDataException("Synthetic processor requires mixed audio.");
            using var reader = new AudioFileReader(path);
            return Task.FromResult(new MeetingAudioProcessingResult(
                path,
                new[] { path },
                reader.TotalTime));
        }
    }

    private sealed class CapturingTranscriptionProvider : ITranscriptionProvider
    {
        private readonly Dictionary<Guid, string> _latestTranscripts = new();

        public string Name => "Synthetic";
        public List<TranscriptionProviderRequest> Requests { get; } = new();
        public List<string> Hashes { get; } = new();
        public Action? BeforeTranscribe { get; set; }

        public Task<TranscriptionProviderResponse> TranscribeAsync(
            TranscriptionProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeTranscribe?.Invoke();
            Requests.Add(request);
            var hash = ComputeFileSha256(request.AudioPath);
            Hashes.Add(hash);
            var recordingId = request.RecordingId ??
                              throw new InvalidOperationException("Recording ID was not forwarded to provider.");
            var text = $"transcript-{hash[..12]}";
            _latestTranscripts[recordingId] = text;
            return Task.FromResult(new TranscriptionProviderResponse(
                JsonSerializer.Serialize(new { text }),
                text,
                new[]
                {
                    new TranscriptSegment
                    {
                        Index = 0,
                        StartSeconds = request.ChunkOffset.TotalSeconds,
                        EndSeconds = request.ChunkOffset.TotalSeconds + 1,
                        Text = text
                    }
                },
                "ru"));
        }

        public string TranscriptFor(Guid recordingId) => _latestTranscripts[recordingId];
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
            LocalSettings = new LocalAppSettings
            {
                MeetingAssistant = new MeetingAssistantSettings
                {
                    DefaultRecordingPolicy = MeetingRecordingPolicy.Manual
                }
            };
            Coordinator = new MeetingAssistantCoordinator(
                State,
                LocalSettings,
                _directory,
                () => Store.Save(State),
                () => LocalSettingsSaveCount++,
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
        public LocalAppSettings LocalSettings { get; }
        public FakeMeetingRecorder Recorder { get; }
        public MeetingAssistantCoordinator Coordinator { get; }
        public MeetingAssistantWorkspaceCommandHandler Handler { get; }
        public List<string> Diagnostics { get; } = new();
        public int StateChangedCount { get; private set; }
        public int LocalSettingsSaveCount { get; private set; }

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
        public bool FailNextStop { get; set; }
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
            if (FailNextStop)
            {
                FailNextStop = false;
                const string technicalError =
                    "Synthetic IMFSinkWriter finalization failed: E_NOINTERFACE " +
                    "(0x80004002), IID {3137F1CD-FE5E-4805-A5D8-FB477448CB3D}.";
                return Task.FromResult(new MeetingRecordingStopResult(
                    DateTimeOffset.UtcNow,
                    AudioTrackHealth.Failed,
                    AudioTrackHealth.Failed,
                    technicalError,
                    _format,
                    new[]
                    {
                        CreateFailedArtifact(MeetingRecordingTrackKind.System, technicalError),
                        CreateFailedArtifact(MeetingRecordingTrackKind.Microphone, technicalError),
                        CreateFailedArtifact(MeetingRecordingTrackKind.Mixed, technicalError)
                    },
                    HasUsableAudio: false));
            }

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

        private MeetingRecordingTrackArtifact CreateFailedArtifact(
            MeetingRecordingTrackKind kind,
            string error)
        {
            var extension = _format == MeetingRecordingFormat.AacM4a ? ".m4a" : ".wav";
            return new MeetingRecordingTrackArtifact
            {
                Kind = kind,
                InProgressFileName = kind.ToString().ToLowerInvariant() + ".current" + extension,
                Container = _format == MeetingRecordingFormat.AacM4a ? "MPEG-4/M4A" : "WAV",
                Codec = _format == MeetingRecordingFormat.AacM4a ? "AAC-LC" : "PCM 16-bit",
                SampleRate = 48_000,
                ChannelCount = kind == MeetingRecordingTrackKind.System ? 2 : 1,
                Bitrate = _format == MeetingRecordingFormat.AacM4a ? 96_000 : 768_000,
                HasAudioFrames = true,
                FinalizationState = MeetingRecordingFinalizationState.Failed,
                ValidationState = MeetingRecordingValidationState.Invalid,
                Error = error
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
