using System.Collections.Concurrent;
using WildCat.Grains;

namespace WildCat.Tests;

/// <summary>Pauses only work for an explicitly selected password, inside the real worker path.</summary>
internal sealed class ControlledPasswordHashing : PasswordHashing
{
    private readonly ConcurrentDictionary<string, Gate> gates = new();

    public Gate Pause(string password)
    {
        var gate = new Gate(() => gates.TryRemove(password, out _));
        if (!gates.TryAdd(password, gate))
            throw new InvalidOperationException("Password work is already paused.");
        return gate;
    }

    protected override string Hash(string password)
    {
        WaitForRelease(password);
        return base.Hash(password);
    }

    protected override bool Verify(string password, string storedHash)
    {
        WaitForRelease(password);
        return base.Verify(password, storedHash);
    }

    private void WaitForRelease(string password)
    {
        if (gates.TryGetValue(password, out var gate))
            gate.Wait();
    }

    internal sealed class Gate(Action remove) : IDisposable
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => started.Task;

        public void Wait()
        {
            started.TrySetResult();
            // Deliberately synchronous: moving the worker onto a grain turn must fail the test.
            // The larger bound leaves time for the assertion to fail and release us in finally.
            if (!released.Task.Wait(TestSetup.Timeout * 2))
                throw new TimeoutException("The test did not release password work.");
        }

        public void Dispose()
        {
            remove();
            released.TrySetResult();
        }
    }
}
