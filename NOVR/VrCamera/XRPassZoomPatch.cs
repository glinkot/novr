using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace NOVR.VrCamera;

/// <summary>
/// URP renders XR views from XRPass rather than Camera's stereo projection properties.
/// Magnify the matrix at that final pipeline boundary without modifying the stored
/// OpenXR matrix, avoiding both silent Camera API no-ops and cumulative scaling.
/// </summary>
[HarmonyPatch(typeof(XRPass), nameof(XRPass.GetProjMatrix))]
internal static class XRPassZoomPatch
{
    private static float _nextTelemetryTime;
    private static bool _loggedHook;

    [HarmonyPostfix]
    private static void Postfix(int viewIndex, ref Matrix4x4 __result)
    {
        var magnification = VrZoomController.Magnification;
        if (magnification <= 1f)
        {
            return;
        }

        var nativeM00 = __result.m00;
        var nativeM11 = __result.m11;
        __result = VrZoomController.MagnifyProjection(__result);

        if (!_loggedHook || Time.unscaledTime >= _nextTelemetryTime)
        {
            Debug.Log(
                $"[NOVR.Zoom.XRPass] view={viewIndex}, magnification={magnification:0.###}, " +
                $"projection=({nativeM00:0.###},{nativeM11:0.###})" +
                $"->({__result.m00:0.###},{__result.m11:0.###}).");
            _loggedHook = true;
            _nextTelemetryTime = Time.unscaledTime + 0.5f;
        }
    }
}
