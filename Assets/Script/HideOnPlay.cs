using UnityEngine;

namespace Boreal.Core
{
    public class HideOnPlay : MonoBehaviour
    {
        void Start()
        {
            gameObject.SetActive(false);
        }

        void Update()
        {
        
        }
    }
}
