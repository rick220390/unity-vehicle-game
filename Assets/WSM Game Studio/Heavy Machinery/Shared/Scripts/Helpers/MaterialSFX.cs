using System;
using UnityEngine;

namespace WSMGameStudio.Audio
{
    [Serializable]
    public struct MaterialSFX
    {
#if UNITY_6000_0_OR_NEWER
        [SerializeField] public PhysicsMaterial physicMaterial;
#else
        [SerializeField] public PhysicMaterial physicMaterial;
#endif
        [SerializeField] public AudioSource audioSurce;
    }
}
