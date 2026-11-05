using UnityEngine;

namespace WSMGameStudio.Vehicles
{
    [CreateAssetMenu(fileName = "NewVehicleCameraSettings", menuName = "WSM Game Studio/Vehicle Camera/Vehicle Camera Settings", order = 1)]
    public class WSMVehicleCameraSettings : ScriptableObject
    {
        public RearViewCameraSettings rearView;
        public CabinViewCameraSettings cabinView;
        public TopDownCameraSettings topDown;
    } 
}
