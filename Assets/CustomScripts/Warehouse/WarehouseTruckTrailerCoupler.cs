using UnityEngine;

/// <summary>
/// Creates a physical fifth-wheel/kingpin connection between independent
/// tractor and trailer rigidbodies. The authored marker positions are authoritative:
/// this component never moves either vehicle to compensate for misplaced markers.
/// </summary>
[DisallowMultipleComponent]
public sealed class WarehouseTruckTrailerCoupler : MonoBehaviour
{
    public static bool Connect(
        Rigidbody tractorBody,
        Transform fifthWheelPivot,
        Rigidbody trailerBody,
        Transform kingpinPivot,
        float maximumHorizontalMismatch,
        float maximumVerticalMismatch,
        out ConfigurableJoint joint)
    {
        joint = null;

        if (tractorBody == null || fifthWheelPivot == null ||
            trailerBody == null || kingpinPivot == null)
        {
            Debug.LogError("WarehouseTruckTrailerCoupler: Tractor Rigidbody, FithWheelPivot, trailer Rigidbody and kingpinPivot are all required.");
            return false;
        }

        // Never reposition the trailer here. Silently shifting its root makes the
        // markers appear to be ignored and can put the kingpin below the fifth wheel.
        // Both physical markers must already be within the configured seating tolerance.
        Vector3 markerOffset = fifthWheelPivot.position - kingpinPivot.position;
        float horizontalMismatch = new Vector2(markerOffset.x, markerOffset.z).magnitude;
        float verticalMismatch = Mathf.Abs(markerOffset.y);
        if (horizontalMismatch > maximumHorizontalMismatch ||
            verticalMismatch > maximumVerticalMismatch)
        {
            Debug.LogError(
                "WarehouseTruckTrailerCoupler: Markers are outside the seating tolerance. Horizontal=" +
                horizontalMismatch.ToString("0.000") + "m (max " +
                maximumHorizontalMismatch.ToString("0.000") + "m), vertical=" +
                verticalMismatch.ToString("0.000") + "m (max " +
                maximumVerticalMismatch.ToString("0.000") + "m). Fifth wheel world=" +
                fifthWheelPivot.position.ToString("F3") + ", kingpin world=" +
                kingpinPivot.position.ToString("F3") + ", delta=" + markerOffset.ToString("F3") +
                ". Fifth-wheel reference: " + DescribeTransform(fifthWheelPivot) +
                ". Kingpin reference: " + DescribeTransform(kingpinPivot) +
                ". Tractor Rigidbody transform: " + DescribeTransform(tractorBody.transform) +
                ". Trailer Rigidbody transform: " + DescribeTransform(trailerBody.transform) +
                ". Fifth-wheel marker under tractor Rigidbody=" + fifthWheelPivot.IsChildOf(tractorBody.transform) +
                "; kingpin marker under trailer Rigidbody=" + kingpinPivot.IsChildOf(trailerBody.transform) +
                ". No vehicle was moved and no joint was created.");
            return false;
        }

        joint = trailerBody.GetComponent<ConfigurableJoint>();
        if (joint == null)
            joint = trailerBody.gameObject.AddComponent<ConfigurableJoint>();

        joint.connectedBody = tractorBody;
        joint.autoConfigureConnectedAnchor = false;
        joint.configuredInWorldSpace = false;
        joint.swapBodies = false;

        // Store each marker in its own Rigidbody's local space.
        joint.anchor = trailerBody.transform.InverseTransformPoint(kingpinPivot.position);
        joint.connectedAnchor = tractorBody.transform.InverseTransformPoint(fifthWheelPivot.position);

        // Local Y is the vertical axis; allow yaw only. All three linear axes must
        // be locked so the kingpin cannot slide down/back when the force direction
        // changes. The previous Free Y setting allowed exactly that failure.
        joint.axis = Vector3.right;
        joint.secondaryAxis = Vector3.up;
        joint.xMotion = ConfigurableJointMotion.Locked;
        joint.yMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Locked;
        joint.angularXMotion = ConfigurableJointMotion.Locked;
        joint.angularYMotion = ConfigurableJointMotion.Free;
        joint.angularZMotion = ConfigurableJointMotion.Locked;

        joint.xDrive = new JointDrive();
        joint.yDrive = new JointDrive();
        joint.zDrive = new JointDrive();
        joint.angularXDrive = new JointDrive();
        joint.angularYZDrive = new JointDrive();
        joint.targetPosition = Vector3.zero;
        joint.targetVelocity = Vector3.zero;
        joint.targetAngularVelocity = Vector3.zero;

        joint.enableCollision = false;
        joint.enablePreprocessing = true;
        joint.projectionMode = JointProjectionMode.None;
        joint.massScale = 1f;
        joint.connectedMassScale = 1f;

        trailerBody.isKinematic = false;
        trailerBody.useGravity = true;
        trailerBody.interpolation = RigidbodyInterpolation.Interpolate;
        trailerBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        return true;
    }
}
