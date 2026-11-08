using UnityEngine;

/// <summary>
/// Creates the fifth-wheel/kingpin articulation between an independent
/// tractor Rigidbody and trailer Rigidbody.
/// </summary>
[DisallowMultipleComponent]
public sealed class WarehouseTruckTrailerCoupler : MonoBehaviour
{
    public static bool Connect(
        Rigidbody tractorBody,
        Transform fifthWheelPivot,
        Rigidbody trailerBody,
        Transform kingpinPivot,
        out ConfigurableJoint joint)
    {
        joint = null;

        if (tractorBody == null || fifthWheelPivot == null ||
            trailerBody == null || kingpinPivot == null)
        {
            Debug.LogError("WarehouseTruckTrailerCoupler: Tractor Rigidbody, FithWheelPivot, trailer Rigidbody and kingpinPivot are all required.");
            return false;
        }

        // The trailer root is positioned so both pivots coincide before this
        // joint is created. Keep the joint anchors in each body's local space.
        joint = trailerBody.GetComponent<ConfigurableJoint>();
        if (joint == null)
            joint = trailerBody.gameObject.AddComponent<ConfigurableJoint>();

        joint.connectedBody = tractorBody;
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = trailerBody.transform.InverseTransformPoint(kingpinPivot.position);
        joint.connectedAnchor = tractorBody.transform.InverseTransformPoint(fifthWheelPivot.position);

        // Define the joint's local Y angular axis as the articulation/yaw axis.
        joint.axis = Vector3.right;
        joint.secondaryAxis = Vector3.up;

        joint.xMotion = ConfigurableJointMotion.Locked;
        // Keep vertical articulation free because trailer prefab kingpin heights
        // can differ. Horizontal anchors must still be closely aligned before coupling.
        joint.yMotion = ConfigurableJointMotion.Free;
        joint.zMotion = ConfigurableJointMotion.Locked;
        // Allow articulation around the vertical axis (yaw), while permitting a
        // small amount of pitch and roll for uneven ground without free tipping.
        joint.angularXMotion = ConfigurableJointMotion.Limited;
        joint.angularYMotion = ConfigurableJointMotion.Free;
        joint.angularZMotion = ConfigurableJointMotion.Limited;
        joint.lowAngularXLimit = new SoftJointLimit { limit = -8f };
        joint.highAngularXLimit = new SoftJointLimit { limit = 8f };
        joint.angularZLimit = new SoftJointLimit { limit = 8f };

        joint.enableCollision = false;
        joint.enablePreprocessing = true;
        // Avoid projection snapping the trailer body upward/downward at connection.
        joint.projectionMode = JointProjectionMode.None;
        joint.projectionDistance = 0.05f;
        joint.projectionAngle = 5f;
        joint.massScale = 1f;
        joint.connectedMassScale = 1f;

        trailerBody.isKinematic = false;
        trailerBody.useGravity = true;
        trailerBody.interpolation = RigidbodyInterpolation.Interpolate;
        trailerBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        return true;
    }
}