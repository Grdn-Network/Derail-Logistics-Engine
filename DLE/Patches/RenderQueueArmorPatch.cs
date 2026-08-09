using DV.RenderTextureSystem;
using HarmonyLib;
using System;

namespace DLE.Patches
{
    /// <summary>
    /// One bad paper must never kill the printer (#209). RenderTextureSystem renders
    /// every booklet, plate and paper in the game through a single queue in its
    /// Update. A render job that throws inside Prepare unwinds out of RenderNextJob
    /// leaving currentJob set and currentRenderTexture null, and from then on the
    /// method NREs on its first line EVERY FRAME: the queue never drains, every
    /// later paper renders blank, and the log grows a stack trace per frame
    /// (observed: 191k exceptions in one client session, the event's blank-booklet
    /// bug). This finalizer resets the per-job state and swallows the throw so the
    /// queue moves on: the one failed paper stays blank, everything after it prints
    /// normally.
    ///
    /// Deliberately NOT host-gated: clients print faxed booklets too, and this
    /// guards a vanilla system rather than running any DLE host logic.
    /// </summary>
    [HarmonyPatch(typeof(RenderTextureSystem), "RenderNextJob")]
    internal static class RenderQueueArmorPatch
    {
        private static bool _reported;

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, RenderTextureSystem __instance)
        {
            if (__exception == null) return null;
            try
            {
                var rt = __instance.currentRenderTexture;
                if (rt != null) rt.Release();
                __instance.currentRenderTexture = null;
                __instance.currentJob = null;
                if (__instance.cam != null) __instance.cam.targetTexture = null;
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[Booklet] render queue reset failed ({ex.GetType().Name}: {ex.Message}).");
            }
            if (!_reported)
            {
                _reported = true;
                Main.LogAlways("[Booklet] a paper render threw " +
                    $"({__exception.GetType().Name}: {__exception.Message}); that paper stays blank and the " +
                    "print queue continues. Report this line if papers look wrong.");
            }
            else
            {
                Main.Log($"[Booklet] another paper render threw ({__exception.GetType().Name}); queue continues.");
            }
            return null;
        }
    }
}
