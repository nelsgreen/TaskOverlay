using System;

namespace TaskOverlay.Core;

public sealed class UnsupportedFutureStateVersionException : Exception
{
    public UnsupportedFutureStateVersionException(
        int storedSchemaVersion,
        int supportedSchemaVersion,
        string statePath)
        : base(
            $"State schema version {storedSchemaVersion} is newer than this TaskOverlay build " +
            $"supports ({supportedSchemaVersion}). Install or run a newer TaskOverlay build. " +
            $"The state file was not changed: {statePath}")
    {
        StoredSchemaVersion = storedSchemaVersion;
        SupportedSchemaVersion = supportedSchemaVersion;
        StatePath = statePath;
    }

    public int StoredSchemaVersion { get; }
    public int SupportedSchemaVersion { get; }
    public string StatePath { get; }
}
