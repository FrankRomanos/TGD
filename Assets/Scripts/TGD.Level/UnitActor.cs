using UnityEngine;
using TGD.Combat;

namespace TGD.Level
{
    public class UnitActor : MonoBehaviour
    {
        [Header("Identity")]
        public string unitId;                   // 与 Unit.UnitId 一致

        [Header("Visual (optional)")]
        public HexSelectVisual selectVisual;    // 脚下环（可不填）
        public Transform damagePivot;           // 飘字挂点（可不填）

        public Unit Model { get; private set; }

        void OnEnable() => CombatViewBridge.Instance?.RegisterActor(unitId, this);
        void OnDisable() => CombatViewBridge.Instance?.UnregisterActor(unitId, this);

        public void Bind(Unit model)
        {
            Model = model;
            TryTintRingByTeam(model?.TeamId ?? 0);
        }

        public void TryTintRingByTeam(int teamId)
        {
            if (!selectVisual) return;
            var fx = selectVisual.GetComponentInChildren<SelectRingFX>(true);
            if (!fx) return;
            var ally = new Color(0.15f, 0.8f, 1f);
            var enemy = new Color(1f, 0.36f, 0.25f);
            fx.glowColor = (teamId == 0) ? ally : enemy;
        }

        public Vector3 DamageWorldPos =>
            damagePivot ? damagePivot.position : transform.position + Vector3.up * 1.6f;
    }
}
