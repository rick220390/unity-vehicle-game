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

        // Seat the kingpin exactly onto the fifth-wheel pivot BEFORE creating
        // the joint. Merely creating a joint between separated world-space anchors
        // makes PhysX pull the trailer into place in one solver step, which can
        // launch the trailer or tractor. Move the trailer Rigidbody as a whole so
        // the authored kingpin marker and the receiver pivot are coincident.
        Vector3 seatingOffset = fifthWheelPivot.position - kingpinPivot.position;
        trailerBody.position += seatingOffset;
        Physics.SyncTransforms();

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
        // Let the trailer move vertically relative to the fifth wheel. This prevents
        // a mismatched pivot height from lifting the trailer and unloading its wheels.
        // The tractor still pulls it horizontally and controls its yaw articulation.
        joint.yMotion = ConfigurableJointMotion.Free;
        joint.zMotion = ConfigurableJointMotion.Locked;
        joint.angularXMotion = ConfigurableJointMotion.Locked;
        joint.angularYMotion = ConfigurableJointMotion.Free;
        joint.angularZMotion = ConfigurableJointMotion.Locked;

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