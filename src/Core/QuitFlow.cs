using System;

namespace QuitToDesktop;

// A token belongs to one presentation. Closing it invalidates every retained callback.
internal sealed class QuitFlow
{
    private readonly Action quit;
    private long sequence;
    internal long ActiveToken { get; private set; }

    internal QuitFlow(Action quit) => this.quit = quit ?? throw new ArgumentNullException(nameof(quit));

    internal long Begin()
    {
        if (ActiveToken != 0) return ActiveToken;
        return ActiveToken = checked(++sequence);
    }

    internal bool Cancel(long token)
    {
        if (token == 0 || token != ActiveToken) return false;
        ActiveToken = 0;
        return true;
    }

    internal bool Confirm(long token)
    {
        if (token == 0 || token != ActiveToken) return false;
        ActiveToken = 0;
        quit();
        return true;
    }
}

internal sealed class PauseInputFence
{
    private long throughFrame;
    internal bool Pending { get; private set; }

    internal void Mark(int frame) { throughFrame = (long)frame + 1; Pending = true; }
    internal bool Blocks(int frame)
    {
        if (!Pending) return false;
        if (frame <= throughFrame) return true;
        Pending = false;
        return false;
    }
}
