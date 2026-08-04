using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace NOVR.VrCamera;

/// <summary>
/// Applies binocular-style magnification to the headset's native per-eye projections.
/// </summary>
internal sealed class VrZoomController : NOVRBehaviour
{
    private const float MinimumMagnification = 1f;
    private const float MaximumSupportedMagnification = 10f;
    private const float AxisDeadzone = 0.001f;

    private static float _magnification = MinimumMagnification;
    private static float _nextInputTelemetryTime;
    private static bool _loggedCockpitHook;

    private Camera? _camera;
    private bool _loggedBeforeRender;
    private bool _loggedBeginCameraRendering;
    private bool _loggedEndCameraRendering;
    private float _nextBeforeRenderTelemetryTime;
    private float _nextBeginCameraTelemetryTime;
    private float _nextEndCameraTelemetryTime;

    protected override void Awake()
    {
        base.Awake();
        _camera = GetComponent<Camera>();

        Debug.Log(
            $"[NOVR.Zoom] Stereo zoom ready (speed {ModConfiguration.Instance.ZoomSpeed.Value:0.##}x/s, " +
            $"maximum {ModConfiguration.Instance.MaximumZoom.Value:0.##}x, " +
            $"instant zoom out {ModConfiguration.Instance.InstantZoomOut.Value}).");
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    protected override void OnBeforeRender()
    {
        base.OnBeforeRender();

        ApplyZoom(
            "OnBeforeRender",
            ref _loggedBeforeRender,
            ref _nextBeforeRenderTelemetryTime);
    }

    protected override void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

        if (_camera != null)
        {
            _camera.ResetStereoProjectionMatrices();
        }

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
                $"[NOVR.Zoom.Telemetry] Cockpit hook frame={Time.frameCount}, axis={zoomAxis:0.###}, " +
                $"magnification={previousMagnification:0.###}->{_magnification:0.###}, " +
                $"flightControls={GameManager.flightControlsEnabled}, mapMaximized={DynamicMap.mapMaximized}.");
            _loggedCockpitHook = true;
            _nextInputTelemetryTime = Time.unscaledTime + 0.5f;
        }
    }

    internal static void ResetZoom()
    {
        _magnification = MinimumMagnification;
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (renderingCamera == _camera)
        {
            ApplyZoom(
                "BeginCameraRendering",
                ref _loggedBeginCameraRendering,
                ref _nextBeginCameraTelemetryTime);
        }
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (renderingCamera != _camera || _camera == null)
        {
            return;
        }

        if (!_loggedEndCameraRendering ||
            (_magnification > MinimumMagnification && Time.unscaledTime >= _nextEndCameraTelemetryTime))
        {
            var left = _camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
            Debug.Log(
                $"[NOVR.Zoom.Telemetry] EndCameraRendering frame={Time.frameCount}, " +
                $"magnification={_magnification:0.###}, left=({left.m00:0.###},{left.m11:0.###}).");
            _loggedEndCameraRendering = true;
            _nextEndCameraTelemetryTime = Time.unscaledTime + 0.5f;
        }
    }

    private void ApplyZoom(string source, ref bool loggedSource, ref float nextTelemetryTime)
    {
        if (_camera == null || !_camera.enabled)
        {
            return;
        }

        // Always start from the OpenXR-provided asymmetric eye projections so zoom does not
        // accumulate from one callback or frame to the next.
        _camera.ResetStereoProjectionMatrices();

        var magnification = Mathf.Clamp(
            _magnification,
            MinimumMagnification,
            MaximumSupportedMagnification);
        var nativeLeft = _camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
        var nativeRight = _camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);

        if (magnification > MinimumMagnification)
        {
            _camera.SetStereoProjectionMatrix(
                Camera.StereoscopicEye.Left,
                MagnifyProjection(nativeLeft, magnification));
            _camera.SetStereoProjectionMatrix(
                Camera.StereoscopicEye.Right,
                MagnifyProjection(nativeRight, magnification));
        }

        if (!loggedSource ||
            (magnification > MinimumMagnification && Time.unscaledTime >= nextTelemetryTime))
        {
            var appliedLeft = _camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
            Debug.Log(
                $"[NOVR.Zoom.Telemetry] {source} frame={Time.frameCount}, camera='{_camera.gameObject.name}', " +
                $"enabled={_camera.enabled}, stereoEnabled={_camera.stereoEnabled}, targetEye={_camera.stereoTargetEye}, " +
                $"xrEnabled={XRSettings.enabled}, renderMode={XRSettings.stereoRenderingMode}, " +
                $"magnification={magnification:0.###}, left=({nativeLeft.m00:0.###},{nativeLeft.m11:0.###})" +
                $"->({appliedLeft.m00:0.###},{appliedLeft.m11:0.###}), " +
                $"right=({nativeRight.m00:0.###},{nativeRight.m11:0.###}).");
            loggedSource = true;
            nextTelemetryTime = Time.unscaledTime + 0.5f;
        }
    }

    private static Matrix4x4 MagnifyProjection(Matrix4x4 projection, float magnification)
    {

        // Scaling these terms narrows the frustum around each eye's existing optical centre.
        // Leave m02/m12 untouched to preserve the headset's asymmetric lens projection.
        projection.m00 *= magnification;
        projection.m11 *= magnification;

        return projection;
    }
}
