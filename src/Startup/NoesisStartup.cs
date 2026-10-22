using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace QuitToDesktop;

internal static class NoesisStartup
{
    private static Harmony? observer;
    private static DeferredStartup? pending;

    internal static void Start()
    {
        var patches = new Harmony(ModInfo.Id);
        var startup = new DeferredStartup(() =>
        {
            RequireInitialized();
            GamePatches.Install(patches);
            ModLog.Info("Initialized " + ModInfo.Version + " after Noesis initialization. Quit to Desktop requires explicit confirmation.");
        }, patches.UnpatchSelf, Callbacks.Boundary);
        GameObject? driverObject = null;
        try
        {
            Observe(startup);
            driverObject = new GameObject("QuitToDesktop.Startup");
            UnityEngine.Object.DontDestroyOnLoad(driverObject);
            driverObject.AddComponent<StartupDriver>().Initialize(startup, StopObserving);
            ModLog.Info("Waiting for Noesis initialization to finish before installing UI patches.");
        }
        catch (Exception)
        {
            Callbacks.Boundary.Run("Could not remove the Noesis startup observer", StopObserving);
            if (!ReferenceEquals(driverObject, null))
                Callbacks.Boundary.Run("Could not remove the startup driver", () => UnityEngine.Object.Destroy(driverObject));
            throw;
        }
    }

    internal static MethodInfo Target => typeof(Noesis.GUI).Assembly.GetType("NoesisUnity", true)!
        .GetMethod("Init", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null)
        ?? throw new MissingMethodException("NoesisUnity", "Init");

    // This hook touches only the managed NoesisUnity initialization method, never a UI type.
    internal static void Observe(DeferredStartup startup)
    {
        if (pending != null) throw new InvalidOperationException("A Noesis startup observer is already registered.");
        var target = Target;
        var patches = new Harmony(ModInfo.Id + ".startup");
        pending = startup;
        observer = patches;
        try
        {
            patches.Patch(target, prefix: new HarmonyMethod(typeof(NoesisStartup), nameof(BeforeInit)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(NoesisStartup), nameof(AfterInit)));
        }
        catch (Exception) { Callbacks.Boundary.Run("Could not roll back the Noesis startup observer", StopObserving); throw; }
    }

    internal static void RequireInitialized()
    {
        var integration = Target.DeclaringType!;
        var unityFlag = integration.GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(integration.FullName, "_initialized");
        var coreFlag = typeof(Noesis.GUI).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(typeof(Noesis.GUI).FullName, "_initialized");
        if (!(bool)unityFlag.GetValue(null)! || !(bool)coreFlag.GetValue(null)!)
            throw new InvalidOperationException("Noesis initialization returned without initializing both the Unity integration and native core.");
    }

    internal static void StopObserving()
    {
        var old = observer;
        observer = null;
        pending = null;
        old?.UnpatchSelf();
    }

    private static void BeforeInit(out bool __state)
    {
        __state = Callbacks.Boundary.Protect("Could not observe Noesis initialization entry",
            () => pending?.Enter() ?? false, false);
    }

    // A void finalizer observes failures without suppressing the game's original exception.
    private static void AfterInit(bool __state, Exception? __exception) =>
        Callbacks.Boundary.Run("Could not observe Noesis initialization completion", () => pending?.Leave(__state, __exception));
}

public sealed class StartupDriver : MonoBehaviour
{
    private DeferredStartup? startup;
    private Action? stopObserving;
    internal void Initialize(DeferredStartup startup, Action stopObserving)
    { this.startup = startup; this.stopObserving = stopObserving; }

    private void Update() => Callbacks.Boundary.Run("Quit to Desktop startup update failed", () =>
    {
        if (startup == null || !startup.Tick()) return;
        startup = null;
        Callbacks.Boundary.Run("Could not remove the Noesis startup observer", () => stopObserving?.Invoke());
        enabled = false;
        UnityEngine.Object.Destroy(gameObject);
    });
}
