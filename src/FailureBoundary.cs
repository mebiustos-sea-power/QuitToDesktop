using System;

namespace QuitToDesktop;

// Only failures in mod-owned callbacks are contained. Original game exceptions retain their normal route.
internal sealed class FailureBoundary
{
    private readonly Action<string, Exception> error;
    internal FailureBoundary(Action<string, Exception> error) => this.error = error;

    internal bool Run(string context, Action action)
    {
        try { action(); return true; }
        catch (Exception failure) { Report(context, failure); return false; }
    }

    internal T Protect<T>(string context, Func<T> action, T failureResult)
    {
        try { return action(); }
        catch (Exception failure) { Report(context, failure); return failureResult; }
    }

    internal void Report(string context, Exception failure)
    {
        try { error(context, failure); }
        catch (Exception loggingFailure) { ModLog.Error("Could not report " + context, loggingFailure); }
    }
}

internal static class Callbacks
{
    internal static readonly FailureBoundary Boundary = new(ModLog.Error);
}
