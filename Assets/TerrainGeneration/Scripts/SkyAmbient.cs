using UnityEngine;

// Keeps the ambient light and reflections RenderSettings takes from the sky in step with the sky actually shown.
// Nothing is baked for the procedural world, so without this they come from whatever probe Unity last stored.
// Sits on the sun, which the procedural sky also uses to place its sun disc.
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class SkyAmbient : MonoBehaviour
{
    void OnEnable()
    {
        if (RenderSettings.sun == null)
        {
            RenderSettings.sun = GetComponent<Light>();
        }

        DynamicGI.UpdateEnvironment();
    }
}
