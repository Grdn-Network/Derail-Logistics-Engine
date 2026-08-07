using System;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Dispatch
{
    /// <summary>
    /// DLE's link to the DV Signals mod. Signals are ITS objects, not ours: they exist in
    /// the world, drivers read them, and the mod drives them on its own automatic logic.
    /// DLE reads them and, when a dispatcher sets a road, takes one to Manual and clears
    /// it; cancelling hands it straight back to automatic.
    ///
    /// The API lives in Signals.API.dll, which is only present when that mod is installed,
    /// so every type touching it sits in DleSignalsBridge.dll and this core assembly holds
    /// nothing but plain data and delegates (#163 taught that lesson the hard way). No
    /// signals mod means no signals on the board: DLE does not invent its own.
    /// </summary>
    public static class SignalsLink
    {
        /// <summary>Plain snapshot of one signal, free of any Signals.API type.</summary>
        public class SignalInfo
        {
            public string Id;
            public float X;
            public float Z;
            public string Aspect;     // S1 stop, S2 clear, S4/S6 caution, null when off
            public bool IsOn;
            public bool Manual;
            public string Type;       // Mainline, IntoYard, Shunting, Distant, Other
            public string Direction;  // Out, In, None
            public string TrackId;
            public string YardId;
            public string JunctionId;  // the mod's own junction key, for diagnosis
        }

        // Filled in by the bridge; all null when the Signals mod is absent.
        public static Func<List<SignalInfo>> GetAllFn;
        public static Func<string, string, bool> SetAspectFn;
        public static Func<string, bool> SetAutomaticFn;
        public static Func<string, bool> SetManualFn;

        public static bool Available => GetAllFn != null;
        public static bool Armed { get; private set; }
        private static bool _reported;

        /// <summary>Aspect ids as the Signals mod names them (German practice).</summary>
        public const string AspectClear = "S2";
        public const string AspectStop = "S1";

        public static List<SignalInfo> All()
        {
            if (GetAllFn == null) return new List<SignalInfo>();
            try { return GetAllFn() ?? new List<SignalInfo>(); }
            catch (Exception ex)
            {
                Main.Log($"[Signals] read failed: {ex.Message}");
                return new List<SignalInfo>();
            }
        }

        /// <summary>
        /// Arm the link if the Signals mod is loaded. Callable repeatedly: the API
        /// assembly loads lazily, so a first attempt at mod load can legitimately find
        /// nothing and a later one succeed.
        /// </summary>
        public static void TryInit()
        {
            if (Armed) return;
            try
            {
                var api = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Signals.API");
                if (api == null)
                {
                    if (!_reported)
                    {
                        _reported = true;
                        Main.Log("[Signals] the DV Signals mod is not loaded; the Rails map will show switches only.");
                    }
                    return;
                }
                var dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                var path = System.IO.Path.Combine(dir, "DleSignalsBridge.dll");
                if (!System.IO.File.Exists(path))
                {
                    Main.LogAlways("[Signals] DleSignalsBridge.dll is missing from the mod folder; signals disabled.");
                    return;
                }
                var asm = System.Reflection.Assembly.LoadFrom(path);
                var type = asm.GetType("DLE.Dispatch.SignalsBridge");
                var init = type?.GetMethod("Init", System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static);
                if (init == null)
                {
                    Main.LogAlways("[Signals] DleSignalsBridge.dll has no entry point; signals disabled.");
                    return;
                }
                init.Invoke(null, null);
                Armed = true;
                Main.LogAlways($"[Signals] DV Signals mod linked; {All().Count} signal(s) visible to dispatch.");
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[Signals] link failed ({ex.GetType().Name}: {ex.Message}); signals disabled.");
            }
        }
    }
}
