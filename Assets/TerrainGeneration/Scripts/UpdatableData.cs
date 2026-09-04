using UnityEngine;
public class UpdatableData : ScriptableObject
{
    public event System.Action OnValuesUpdated;
    public bool autoUpdate;

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        if (autoUpdate)
        {
            UnityEditor.EditorApplication.update += NotifyOfUpdatedValues;
        }
    }
#endif

    public void NotifyOfUpdatedValues()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= NotifyOfUpdatedValues;
#endif
        if (OnValuesUpdated != null)
        {
            OnValuesUpdated();
        }
    }
}
