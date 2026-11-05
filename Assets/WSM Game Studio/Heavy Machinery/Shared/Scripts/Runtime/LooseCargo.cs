using UnityEngine;

namespace WSMGameStudio.HeavyMachinery
{
    public class LooseCargo : MonoBehaviour
    {
        private Transform _transform;
        private CargoContainer _parentCargoContainer;

        private void Start()
        {
            _transform = GetComponent<Transform>();
        }

        private void OnTriggerEnter(Collider other)
        {
            CargoContainer cargoContainer = other.GetComponent<CargoContainer>();

            if (cargoContainer != null)
            {
                _parentCargoContainer = cargoContainer;
                _transform.SetParent(_parentCargoContainer.Transform);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            CargoContainer cargoContainer = other.GetComponent<CargoContainer>();

            if (cargoContainer != null && _parentCargoContainer != null)
            {
#if UNITY_6000_4_OR_NEWER
                if (_parentCargoContainer.gameObject.GetEntityId() == cargoContainer.gameObject.GetEntityId())
#else
                if (_parentCargoContainer.gameObject.GetInstanceID() == cargoContainer.gameObject.GetInstanceID())
#endif
                {
                    _parentCargoContainer = null;
                    _transform.SetParent(null);
                }
            }
        }
    }
}
