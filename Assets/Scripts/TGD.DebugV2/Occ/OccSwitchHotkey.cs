using UnityEngine;
using TGD.CoreV2;

public sealed class OccSwitchHotkey : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F10;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            OccRuntimeSwitch.UseIOccWrites = !OccRuntimeSwitch.UseIOccWrites;
            Debug.Log("[Occ] UseIOccWrites = " + OccRuntimeSwitch.UseIOccWrites);
        }
    }
}
