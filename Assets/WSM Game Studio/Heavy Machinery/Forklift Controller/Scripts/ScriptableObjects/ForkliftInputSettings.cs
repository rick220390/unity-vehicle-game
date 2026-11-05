using UnityEngine;

namespace WSMGameStudio.HeavyMachinery
{
    [CreateAssetMenu(fileName = "ForkliftInputSettings", menuName = "WSM Game Studio/Heavy Machinery/Legacy Input System/Forklift Input Settings", order = 1)]
    public class ForkliftInputSettings : ScriptableObject
    {
        public KeyCode forksUp = KeyCode.Keypad4;
        public KeyCode forksDown = KeyCode.Keypad1;
        public KeyCode forksRight = KeyCode.Keypad8;
        public KeyCode forksLeft = KeyCode.Keypad7;
        public KeyCode forksOpen = KeyCode.Keypad9;
        public KeyCode forksClose = KeyCode.Keypad6;
        public KeyCode mastTiltBackwards = KeyCode.Keypad2;
        public KeyCode mastTiltForwards = KeyCode.Keypad5;

        public KeyCode[] customEventTriggers;
    }
}
