using System;
using System.Threading;

namespace TaskOverlay.Core;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = @"Local\TaskOverlay.WpfV2";

    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static SingleInstanceGuard? TryAcquire(
        string mutexName = DefaultMutexName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
        {
            throw new ArgumentException("A mutex name is required.", nameof(mutexName));
        }

        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        var mutex = _mutex;
        _mutex = null;
        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Ownership can already be gone during exceptional process teardown.
        }
        finally
        {
            mutex.Dispose();
        }
    }
}
