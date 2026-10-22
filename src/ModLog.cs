using System;
using System.Diagnostics;
using BepInEx.Logging;

namespace QuitToDesktop;

internal static class ModLog
{
    private static ManualLogSource? source;
    internal static void Info(string message) => Write(LogLevel.Info, message, null);
    internal static void Error(string message, Exception error) => Write(LogLevel.Error, message, error);

    private static void Write(LogLevel level, string message, Exception? original)
    {
        try
        {
            string details = original == null ? message : message + ": " + original;
            (source ??= Logger.CreateLogSource(ModInfo.Name)).Log(level, details);
        }
        catch (Exception loggingFailure)
        {
            try { Trace.TraceError(ModInfo.Name + ": " + message + "; exception type=" + original?.GetType().FullName + "; logging failed=" + loggingFailure.GetType().FullName); }
            catch (Exception) { }
        }
    }
}
