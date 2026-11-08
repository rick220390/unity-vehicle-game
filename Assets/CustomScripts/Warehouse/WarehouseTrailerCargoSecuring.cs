using UnityEngine;

/// <summary>
/// Physically secures every Rigidbody in a trailer's Load hierarchy.
/// Top-level cargo bodies (normally pallets) are fixed to the trailer;
/// nested cargo bodies (normally barrels/products) are fixed to their
/// nearest parent cargo Rigidbody. This is shared by AI and player coupling.
/// </summary>
public static class WarehouseTrailerCargoSecuring
{
    public static int Secure(Transform trailerRoot, Rigidbody trailerBody)
    {
        if (trailerRoot == null || trailerBody == null)
        {
            Debug.LogWarning("WarehouseTrailerCargoSecuring: Trailer root or Rigidbody is missing.");
            return 0;
        }

        Transform load = FindChild(trailerRoot, "Load");
        if (load == null)
        {
            Debug.LogWarning("WarehouseTrailerCargoSecuring: No child named 'Load' found.", trailerRoot);
            return 0;
        }

        Rigidbody[] cargoBodies = load.GetComponentsInChildren<Rigidbody>(true);
        int securedCount = 0;

        foreach (Rigidbody cargoBody in cargoBodies)
        {
            if (cargoBody == null || cargoBody == trailerBody)
                continue;

            Rigidbody parentCargoBody = FindNearestParentBody(cargoBody.transform, load);
            Rigidbody connectionBody = parentCargoBody != null ? parentCargoBody : trailerBody;

            if (connectionBody == cargoBody)
                continue;

            // Cargo must remain simulated so it can collide and react naturally,
            // while the FixedJoint prevents it separating from its support.
            cargoBody.isKinematic = false;
            cargoBody.useGravity = true;
            cargoBody.interpolation = RigidbodyInterpolation.Interpolate;
            cargoBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            FixedJoint cargoJoint = FindJointTo(cargoBody, connectionBody);
            if (cargoJoint == null)
                cargoJoint = cargoBody.gameObject.AddComponent<FixedJoint>();

            cargoJoint.connectedBody = connectionBody;
            cargoJoint.enableCollision = false;
            cargoJoint.breakForce = float.PositiveInfinity;
            cargoJoint.breakTorque = float.PositiveInfinity;
            securedCount++;
        }

        Debug.Log(
            "WarehouseTrailerCargoSecuring: Secured " + securedCount +
            " cargo Rigidbody(s) in '" + load.name +
            "' (pallets to trailer; nested items to their pallet).", trailerRoot);

        return securedCount;
    }

    private static Rigidbody FindNearestParentBody(Transform item, Transform loadRoot)
    {
        Transform current = item.parent;
        while (current != null && current != loadRoot)
        {
            Rigidbody body = current.GetComponent<Rigidbody>();
            if (body != null)
                return body;

            current = current.parent;
        }

        return null;
    }

    private static FixedJoint FindJointTo(Rigidbody body, Rigidbody connectedBody)
    {
        FixedJoint[] joints = body.GetComponents<FixedJoint>();
        foreach (FixedJoint joint in joints)
        {
            if (joint != null && joint.connectedBody == connectedBody)
                return joint;
        }

        return null;
    }

    private static Transform FindChild(Transform root, string exactName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != root && child.name == exactName)
                return child;
        }

        return null;
    }
}
