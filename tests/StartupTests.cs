using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using QuitToDesktop;
using static Program;

internal static class StartupTests
{
    internal static void Run()
    {
        Check("startup does not install UI patches while waiting for Noesis", () =>
        {
            int calls = 0; var startup = new DeferredStartup(() => calls++, () => { }, new FailureBoundary((_, _) => { }));
            Require(!startup.Tick() && calls == 0 && !startup.Finished, "Waiting touched the UI installer.");
        });
        Check("nested initialization cannot install patches before the outer call and later update", () =>
        {
            int calls = 0; var startup = new DeferredStartup(() => calls++, () => { }, new FailureBoundary((_, _) => { }));
            bool outer = startup.Enter(), inner = startup.Enter();
            startup.Leave(inner, null);
            Require(!startup.Tick() && calls == 0, "A nested Init return installed UI patches.");
            startup.Leave(outer, null);
            Require(calls == 0 && !startup.Finished, "Patches were installed inside the Init completion callback.");
            Require(startup.Tick() && startup.Tick() && calls == 1, "A later update did not install exactly once.");
        });
        Check("Noesis initialization failure is logged and permanently prevents UI patch installation", () =>
        {
            int calls = 0; var errors = new List<Exception>(); var failure = new InvalidOperationException("Noesis setup failed");
            var startup = new DeferredStartup(() => calls++, () => { }, new FailureBoundary((_, e) => errors.Add(e)));
            bool outer = startup.Enter(), inner = startup.Enter();
            startup.Leave(inner, failure); startup.Leave(outer, null); startup.Tick();
            Require(startup.Finished && calls == 0 && errors.Count == 1 && ReferenceEquals(errors[0], failure) && !startup.Enter(),
                "A partially failed initialization was treated as ready or its error was hidden.");
        });
        Check("installation and rollback errors are both contained, logged, and never retried", () =>
        {
            int installs = 0, rollbacks = 0; var errors = new List<Exception>();
            var first = new InvalidOperationException("patch failed"); var second = new InvalidOperationException("rollback failed");
            var startup = new DeferredStartup(() => { installs++; throw first; }, () => { rollbacks++; throw second; },
                new FailureBoundary((_, e) => errors.Add(e)));
            startup.Leave(startup.Enter(), null); startup.Tick(); startup.Tick();
            Require(installs == 1 && rollbacks == 1 && errors.Count == 2 && errors[0] == first && errors[1] == second,
                "An installation failure leaked, was retried, or hid its rollback error.");
        });
        Check("installer reentry cannot schedule a second installation", () =>
        {
            int installs = 0; DeferredStartup startup = null!;
            startup = new DeferredStartup(() => { installs++; Require(!startup.Enter(), "Install reentry was accepted."); }, () => { }, new FailureBoundary((_, _) => { }));
            startup.Leave(startup.Enter(), null); startup.Tick(); startup.Tick();
            Require(installs == 1, "Installation was repeated.");
        });
        Check("callback errors including condition evaluation are logged and contained", () =>
        {
            var errors = new List<Exception>(); var failure = new InvalidOperationException("callback failed");
            var boundary = new FailureBoundary((_, e) => errors.Add(e));
            Require(!boundary.Run("entry", () => throw failure), "A failing callback was reported as successful.");
            Require(!boundary.Protect<bool>("key precondition", () => throw failure, false), "A failing input callback permitted the original action.");
            Require(errors.Count == 2 && errors[0] == failure && errors[1] == failure, "The callback failure lost its original exception.");
        });
        Check("an error reporter failure cannot escape to the game", () =>
        {
            var boundary = new FailureBoundary((_, _) => throw new InvalidOperationException("logger failed"));
            Require(!boundary.Run("entry", () => throw new InvalidOperationException("callback failed")), "Reporter failure escaped or became success.");
        });
    }

    private static int simulatedDepth;
    private static int installs;
    private static bool failInitialization;
    private static DeferredStartup active = null!;
    private static readonly Exception originalFailure = new InvalidOperationException("simulated original Noesis initialization error");
    private static FieldInfo UnityFlag => NoesisStartup.Target.DeclaringType!.GetField("_initialized", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static FieldInfo CoreFlag => typeof(Noesis.GUI).GetField("_initialized", BindingFlags.Static | BindingFlags.NonPublic)!;

    // Only the Unity settings/license/provider work is simulated. Native GUI.Init and product hooks are real.
    private static bool SimulateInitialization()
    {
        simulatedDepth++;
        try
        {
            UnityFlag.SetValue(null, true);
            if (failInitialization) throw originalFailure;
            if (simulatedDepth == 1)
            {
                NoesisStartup.Target.Invoke(null, null);
                Require(!active.Tick() && installs == 0, "Nested initialization installed UI patches.");
                Noesis.GUI.Init();
            }
            return false;
        }
        finally { simulatedDepth--; }
    }

    internal static void RunCold()
    {
        Require(!(bool)CoreFlag.GetValue(null)!, "The cold-start checks were invoked after native Noesis initialization.");
        Check("the real startup observer attaches before native Noesis without installing any UI hooks", () =>
        {
            int calls = 0;
            var startup = new DeferredStartup(() => calls++, () => { }, new FailureBoundary((_, _) => { }));
            try
            {
                NoesisStartup.Observe(startup);
                startup.Tick();
                Require(!(bool)CoreFlag.GetValue(null)! && calls == 0, "Startup initialized native Noesis or installed UI hooks early.");
                Require(Harmony.GetPatchInfo(NoesisStartup.Target)!.Owners.Contains(ModInfo.Id + ".startup"), "The bootstrap observer was not attached.");
                var setter = AccessTools.PropertySetter(typeof(SeapowerUI.ViewModels.PauseMenuViewModel), "PauseMenuButtons");
                Require(Harmony.GetPatchInfo(setter) == null, "The cold startup path touched a product UI hook.");
            }
            finally { NoesisStartup.StopObserving(); }
        });
        var simulation = new Harmony("mebiustos.quittodesktop.tests.unity-boundary");
        try
        {
            simulation.Patch(NoesisStartup.Target, prefix: new HarmonyMethod(typeof(StartupTests), nameof(SimulateInitialization)) { priority = Priority.Last });
            Check("the actual startup finalizer preserves an original initialization error and blocks UI installation", () =>
            {
                failInitialization = true; installs = 0; var errors = new List<Exception>();
                active = new DeferredStartup(() => installs++, () => { }, new FailureBoundary((_, e) => errors.Add(e)));
                try
                {
                    NoesisStartup.Observe(active);
                    try { NoesisStartup.Target.Invoke(null, null); throw new Exception("The original initialization exception was swallowed."); }
                    catch (TargetInvocationException e) { Require(ReferenceEquals(e.InnerException, originalFailure), "The original game exception was replaced."); }
                    active.Tick();
                    Require(installs == 0 && errors.Count == 1 && errors[0] == originalFailure && !(bool)CoreFlag.GetValue(null)!,
                        "The early integration flag was accepted as initialization completion.");
                }
                finally { NoesisStartup.StopObserving(); UnityFlag.SetValue(null, false); }
            });
            Check("the actual startup observer defers real UI hooks until native Noesis and the outer Init have completed", () =>
            {
                failInitialization = false; installs = 0; var errors = new List<Exception>();
                var runtime = new Harmony("mebiustos.quittodesktop.tests.deferred-product");
                active = new DeferredStartup(() => { NoesisStartup.RequireInitialized(); GamePatches.Install(runtime); installs++; }, runtime.UnpatchSelf,
                    new FailureBoundary((_, e) => errors.Add(e)));
                try
                {
                    NoesisStartup.Observe(active);
                    NoesisStartup.Target.Invoke(null, null);
                    Require(installs == 0 && (bool)CoreFlag.GetValue(null)!, "UI patches were installed inside initialization or native Noesis was not initialized.");
                    Require(active.Tick() && installs == 1 && errors.Count == 0, "The first later update did not install the real product hooks.");
                    var setter = AccessTools.PropertySetter(typeof(SeapowerUI.ViewModels.PauseMenuViewModel), "PauseMenuButtons");
                    Require(Harmony.GetPatchInfo(setter)!.Owners.Contains(runtime.Id), "The actual native UI setter was not patched.");
                }
                finally { runtime.UnpatchSelf(); NoesisStartup.StopObserving(); }
            });
        }
        finally { simulation.UnpatchSelf(); }
    }
}
