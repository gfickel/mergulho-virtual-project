using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(Light))]
public class ARLightEstimation : MonoBehaviour
{
    [SerializeField]
    private ARCameraManager cameraManager;

    private Light directionalLight;

    void Awake()
    {
        directionalLight = GetComponent<Light>();
    }

    void OnEnable()
    {
        if (cameraManager != null)
        {
            cameraManager.frameReceived += OnFrameReceived;
        }
    }

    void OnDisable()
    {
        if (cameraManager != null)
        {
            cameraManager.frameReceived -= OnFrameReceived;
        }
    }

    private void OnFrameReceived(ARCameraFrameEventArgs args)
    {
        var le = args.lightEstimation;

        // Ambient Intensity mode
        if (le.averageBrightness.HasValue)
        {
            directionalLight.intensity = le.averageBrightness.Value;
        }
        if (le.averageColorTemperature.HasValue)
        {
            directionalLight.useColorTemperature = true;
            directionalLight.colorTemperature = le.averageColorTemperature.Value;
        }
        if (le.colorCorrection.HasValue)
        {
            directionalLight.color = le.colorCorrection.Value;
        }

        // Environmental HDR mode — overrides ambient values when available
        if (le.mainLightDirection.HasValue)
        {
            directionalLight.transform.rotation = Quaternion.LookRotation(le.mainLightDirection.Value);
        }
        if (le.mainLightColor.HasValue)
        {
            directionalLight.color = le.mainLightColor.Value;
        }
        if (le.mainLightIntensityLumens.HasValue)
        {
            directionalLight.intensity = le.mainLightIntensityLumens.Value / 1000f;
        }
        if (le.ambientSphericalHarmonics.HasValue)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientProbe = le.ambientSphericalHarmonics.Value;
        }
    }
}
