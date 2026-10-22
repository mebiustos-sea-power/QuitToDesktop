using System;

namespace QuitToDesktop;

// Completion is observed after the outermost Init returns; installation happens on a later Unity Update.
internal sealed class DeferredStartup
{
    private readonly FailureBoundary boundary;
    private readonly Action install;
    private readonly Action rollback;
    private int depth;
    private Exception? initializationFailure;
    private bool ready;
    private bool attempted;
    internal bool Finished { get; private set; }

    internal DeferredStartup(Action install, Action rollback, FailureBoundary boundary)
    { this.install = install; this.rollback = rollback; this.boundary = boundary; }

    internal bool Enter()
    {
        if (Finished || attempted) return false;
        depth++;
        return true;
    }

    internal void Leave(bool entered, Exception? gameFailure)
    {
        if (!entered) return;
        if (depth <= 0) throw new InvalidOperationException("Noesis initialization completion has no matching entry.");
        if (gameFailure != null) initializationFailure ??= gameFailure;
        depth--;
        if (depth != 0) return;
        if (initializationFailure != null)
        {
            Finished = true;
            boundary.Report("Noesis initialization failed; Quit to Desktop patches were not installed", initializationFailure);
            return;
        }
        ready = true;
    }

    internal bool Tick()
    {
        if (Finished) return true;
        if (!ready || depth != 0 || attempted) return false;
        attempted = true;
        if (!boundary.Run("Could not install Quit to Desktop after Noesis initialization", install))
            boundary.Run("Could not roll back Quit to Desktop patches", rollback);
        Finished = true;
        return true;
    }
}
