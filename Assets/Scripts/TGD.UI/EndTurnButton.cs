using UnityEngine;
using UnityEngine.UI;
using TGD.Combat;

public class EndTurnButton : MonoBehaviour
{
    public CombatLoop combat;
    void Awake() { if (!combat) combat = FindFirstObjectByType<CombatLoop>(); }
    public void EndTurn() => combat?.EndActiveTurn();
}
