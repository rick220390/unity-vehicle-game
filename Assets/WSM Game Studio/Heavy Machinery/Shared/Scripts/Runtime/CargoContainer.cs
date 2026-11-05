using UnityEngine;

namespace WSMGameStudio.HeavyMachinery
{
    public class CargoContainer : MonoBehaviour
    {
        private Transform _transform;

        public Transform Transform
        {
            get { return _transform; }
        }

        private void Start()
        {
            _transform = GetComponent<Transform>();
        }
    } 
}
