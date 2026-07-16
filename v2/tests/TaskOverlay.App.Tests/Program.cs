using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
            ("active runtime remains owned by its MEET", ActiveRuntimeRemainsOwnedByItsMeet)
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

    private sealed class FakeMeetingRecorder : IMeetingRecorder
    {
        private MeetingRecorderRuntimeStatus _status = IdleStatus();

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
            _status = new MeetingRecorderRuntimeStatus(
                request.RecordingId,
                startedAt,
                AudioTrackHealth.Healthy,
                AudioTrackHealth.Healthy,
                null);
            return Task.FromResult(new MeetingRecordingStartResult(
                startedAt,
                startedAt,
                startedAt,
                "system.wav",
                "microphone.wav",
                AudioTrackHealth.Healthy,
                AudioTrackHealth.Healthy,
                null));
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
                null));
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
