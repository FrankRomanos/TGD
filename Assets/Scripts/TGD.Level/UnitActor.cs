using UnityEngine;
using TGD.Combat;
using TGD.Data;
using TGD.Core;

namespace TGD.Level
{
    public class UnitActor : MonoBehaviour
    {
        [Header("Identity")]
        public string unitId = "knight";   // 与 Unit.UnitId 对应
        public int teamId = 0;             // 0=我方, 1=敌方...（你随意约定）
        public string classId = "CL001";   // 职业ID（用来自动拿技能）

        [Header("Visual (optional)")]
        public HexSelectVisual selectVisual;    // 脚下环
        public Transform damagePivot;           // 飘字挂点

        public Unit Model { get; private set; }
        // UnitActor.cs （补充）
  
        // UnitActor.cs
        public void ShowRing(bool on) => selectVisual?.SetVisible(on);

        void OnEnable() => CombatViewBridge.Instance?.RegisterActor(unitId, this);
        void OnDisable() => CombatViewBridge.Instance?.UnregisterActor(unitId, this);

        public void Bind(Unit model)
        {
            Model = model;
            TryTintRingByTeam(model?.TeamId ?? teamId);
        }

        public void TryTintRingByTeam(int t)
        {
            if (!selectVisual) return;
            var fx = selectVisual.GetComponentInChildren<SelectRingFX>(true);
            if (!fx) return;
            var ally = new Color(0.15f, 0.8f, 1f);
            var enemy = new Color(1f, 0.36f, 0.25f);
            fx.glowColor = (t == 0) ? ally : enemy;
        }

        // 方便把 Actor 一键转换为战斗模型
        public Unit BuildUnit()
        {
            var u = new Unit
            {
                UnitId = unitId,
                ClassId = classId,
                TeamId = teamId,
                Stats = new Stats()
            };
            u.Stats.Clamp();

            // 给职业自动装技能（来自 Resources/SkillDataJason）
            var classSkills = SkillDatabase.GetSkillsForClass(classId);
            u.Skills = new System.Collections.Generic.List<SkillDefinition>(classSkills);
            return u;
        }

        public Vector3 DamageWorldPos =>
            damagePivot ? damagePivot.position : transform.position + Vector3.up * 1.6f;
    }
}
