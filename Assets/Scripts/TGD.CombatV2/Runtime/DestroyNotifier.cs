using System;
using UnityEngine;

public sealed class DestroyNotifier : MonoBehaviour
{
    public event Action OnDestroyed;

    void OnDestroy()
    {
        try
        {
            OnDestroyed?.Invoke();
        }
        catch
        {
        }
        OnDestroyed = null;
    }
}
