using UnityEngine;
using UnityEngine.UI;
using TGD.Combat;

public class EndTurnButton : MonoBehaviour
{
    public CombatLoop combat;

    void Awake()
    {
        if (!combat)
            combat = FindCombatLoop();
    }

    public void EndTurn() => combat?.EndActiveTurn();

    static CombatLoop FindCombatLoop()
    {
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindFirstObjectByType<CombatLoop>(FindObjectsInactive.Include);
#else
        return UnityEngine.Object.FindObjectOfType<CombatLoop>();
#endif
    }
}
