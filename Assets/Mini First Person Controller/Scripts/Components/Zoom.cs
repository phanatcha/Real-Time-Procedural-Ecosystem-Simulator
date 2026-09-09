using UnityEngine;
using UnityEngine.InputSystem;

[ExecuteInEditMode]
public class Zoom : MonoBehaviour
{
    Camera attachedCamera;
    public float defaultFOV = 60;
    public float maxZoomFOV = 15;
    [Range(0, 1)]
    public float currentZoom;
    public float sensitivity = 1;


    void Awake()
    {
        // Get the camera on this gameObject and the defaultZoom.
        attachedCamera = GetComponent<Camera>();
        if (attachedCamera)
        {
            defaultFOV = attachedCamera.fieldOfView;
        }
    }

    void Update()
    {
        // Update the currentZoom and the camera's fieldOfView.
        float scrollY = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
        currentZoom += scrollY * sensitivity * .05f;
        currentZoom = Mathf.Clamp01(currentZoom);
        attachedCamera.fieldOfView = Mathf.Lerp(defaultFOV, maxZoomFOV, currentZoom);
    }
}
