using UnityEngine;

namespace NOVR.VrCamera;

/// <summary>
/// Tracks cockpit zoom input. The projection is applied by XRPassZoomPatch at the
/// render-pipeline matrix boundary used by URP/OpenXR.
/// </summary>
internal sealed class VrZoomController : NOVRBehaviour
{
    private const float MinimumMagnification = 1f;
    private const float MaximumSupportedMagnification = 10f;
    private const float AxisDeadzone = 0.001f;

    private static float _magnification = MinimumMagnification;
    private static float _nextInputTelemetryTime;
    private static bool _loggedCockpitHook;

    internal static float Magnification => Mathf.Clamp(
        _magnification,
        MinimumMagnification,
        MaximumSupportedMagnification);

    protected override void Awake()
    {
        base.Awake();

        Debug.Log(
            $"[NOVR.Zoom] XR-pass zoom ready (speed {ModConfiguration.Instance.ZoomSpeed.Value:0.##}x/s, " +
            $"maximum {ModConfiguration.Instance.MaximumZoom.Value:0.##}x, " +
            $"instant zoom out {ModConfiguration.Instance.InstantZoomOut.Value}).");
    }

    protected override void OnDisable()
    {
        ResetZoom();
        base.OnDisable();
    }

    internal static void UpdateZoomInput(float zoomAxis)
    {
        var configuration = ModConfiguration.Instance;
        var maximumZoom = Mathf.Clamp(
            configuration.MaximumZoom.Value,
            MinimumMagnification,
            MaximumSupportedMagnification);
        var zoomSpeed = Mathf.Clamp(configuration.ZoomSpeed.Value, 0.1f, 20f);
        var axisMagnitude = Mathf.Clamp01(Mathf.Abs(zoomAxis));
        var previousMagnification = _magnification;

        _magnification = Mathf.Clamp(
            _magnification,
            MinimumMagnification,
            maximumZoom);

        if (zoomAxis > AxisDeadzone)
        {
            _magnification = Mathf.MoveTowards(
                _magnification,
                maximumZoom,
                zoomSpeed * axisMagnitude * Time.unscaledDeltaTime);
        }
        else if (zoomAxis < -AxisDeadzone)
        {
            _magnification = configuration.InstantZoomOut.Value
                ? MinimumMagnification
                : Mathf.MoveTowards(
                    _magnification,
                    MinimumMagnification,
                    zoomSpeed * axisMagnitude * Time.unscaledDeltaTime);
        }

        if (!_loggedCockpitHook ||
            (axisMagnitude > AxisDeadzone && Time.unscaledTime >= _nextInputTelemetryTime))
        {
            Debug.Log(
                $"[NOVR.Zoom.Telemetry] Cockpit axis={zoomAxis:0.###}, " +
                $"magnification={previousMagnification:0.###}->{_magnification:0.###}.");
            _loggedCockpitHook = true;
            _nextInputTelemetryTime = Time.unscaledTime + 0.5f;
        }
    }

    internal static void ResetZoom()
    {
        _magnification = MinimumMagnification;
    }

    internal static Matrix4x4 MagnifyProjection(Matrix4x4 projection)
    {
        var magnification = Magnification;

        // Narrow the frustum around its existing optical centre. Keeping m02/m12
        // unchanged preserves OpenXR's per-eye asymmetric lens projection.
        projection.m00 *= magnification;
        projection.m11 *= magnification;
        return projection;
    }
}
