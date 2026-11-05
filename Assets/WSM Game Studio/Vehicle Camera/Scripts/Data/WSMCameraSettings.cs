using UnityEngine;

namespace WSMGameStudio.Vehicles
{
    [System.Serializable]
    public struct RearViewCameraSettings
    {
        public float lookSpeed;
        public float defaultZoom;
        public float minZoom;
        public float maxZoom;
        public float zoomRate;
        public Vector3 targetOffset;
        public float defaultVerticalAngle;
        public float verticalAngleLimit;
        public float dampingSpeedMin;
        public float dampingSpeedMax;
        public float dampingMin;
        public float dampingMax;
        public float FOVDistortionSpeedLimit;
        public float FOVDistortion;
    }

    [System.Serializable]
    public struct CabinViewCameraSettings
    {
        public float lookSpeed;
        public float followSpeed;
        public Vector3 defaultLookPosition;
        public Vector3 sideLookPosition;
        public Vector3 lookBackPosition;
        public Vector3 lookDownPosition;
        public Vector3 customLookPosition;
        public Vector2 customLookRotation;
        public float horizontalAngleLimit;
        public float verticalAngleLimit;
        public float FOVDistortionSpeedLimit;
        public float FOVDistortion;
    }

    [System.Serializable]
    public struct TopDownCameraSettings
    {
        public float lookSpeed;
        public float initialYaw;
        public Vector2 positionOffset;
        public bool orthographicCam;
        public float FOVDistortionSpeedLimit;
        public float FOVDistortion;
    }

    [System.Serializable]
    public struct ActiveCameraTypes
    {
        public bool RearView;
        public bool CabinView;
        public bool TopDown;
    }
}
