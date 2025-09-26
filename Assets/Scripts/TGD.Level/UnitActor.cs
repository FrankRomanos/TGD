using System.Collections;
using TGD.Combat;
using TGD.Data;
using TGD.Grid;
using UnityEngine;


namespace TGD.Level
{
    public class UnitActor : MonoBehaviour
    {
        [Header("Identity")]
        public string unitId = "knight";   // 必须唯一，CombatLoop/Bridge 都用它匹配
        public int teamId = 0;             // 0=玩家阵营，1=敌人
        public string classId = "CL001";   // 职业ID（用于自动装技能）

        [Header("Visual (optional)")]
        public HexSelectVisual selectVisual;    // 脚下环（可空）
        public Transform damagePivot;           // 飘字挂点（可空）

        [Header("Grid (optional)")]
        public HexGridAuthoring gridOverride;

        [Header("Stats (optional)")]
        public TGD.Core.Stats initialStats = new TGD.Core.Stats();

        public Unit Model { get; private set; }

        // —— 对外：桥会调用 —— //
        public void ShowRing(bool on) => selectVisual?.SetVisible(on);

        // 给桥/引导器使用：把场景 Actor 转成战斗 Unit
        public Unit BuildUnit()
        {
            var stats = initialStats != null ? initialStats.Clone() : new TGD.Core.Stats();
            var u = new Unit
            {
                UnitId = unitId,
                ClassId = classId,
                TeamId = teamId,
                Stats = stats
            };
            ClassResourceCatalog.ApplyDefaults(u);
            u.Stats.Clamp();

            if (TryResolveCoordinate(out var coord))
                u.Position = coord;


            // 自动装载职业技能（来自 Resources/SkillDataJason）
            var classSkills = SkillDatabase.GetSkillsForClass(classId);
            u.Skills = new System.Collections.Generic.List<SkillDefinition>(classSkills);
            return u;
        }

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
        public void SyncModelPosition()
        {
            if (Model == null)
                return;

            if (TryResolveCoordinate(out var coord))
                Model.Position = coord;
        }

        public void ApplyCoordinate(HexCoord coord)
        {
            var grid = ResolveGrid();
            if (grid?.Layout != null)
            {
                var pos = grid.Layout.GetWorldPosition(coord, grid.tileHeightOffset);
                transform.position = pos;
            }
            else
            {
                transform.position = new Vector3(coord.Q, transform.position.y, coord.R);
            }

            if (Model != null)
                Model.Position = coord;
        }
        // —— 与桥的注册（稳妥版，不会因时序丢注册）——
        Coroutine _registerRoutine;

        void OnEnable()
        {
            if (!TryRegisterImmediately())
                _registerRoutine = StartCoroutine(RegisterWhenReady());
        }

        void OnDisable()
        {
            if (_registerRoutine != null)
            {
                StopCoroutine(_registerRoutine);
                _registerRoutine = null;
            }
            CombatViewBridge.Instance?.UnregisterActor(unitId, this);
        }

        bool TryRegisterImmediately()
        {
            var bridge = CombatViewBridge.Instance;
            if (bridge == null) return false;
            bridge.RegisterActor(unitId, this);
            return true;
        }

        IEnumerator RegisterWhenReady()
        {
            while (!TryRegisterImmediately())
                yield return null;
            _registerRoutine = null;
        }

        public Vector3 DamageWorldPos =>
            damagePivot ? damagePivot.position : transform.position + Vector3.up * 1.6f;

        public HexGridAuthoring ResolveGrid()
        {
            if (gridOverride)
                return gridOverride;
            if (selectVisual && selectVisual.grid)
                return selectVisual.grid;
            return null;
        }

        public bool TryResolveCoordinate(out HexCoord coord)
        {
            var grid = ResolveGrid();
            if (grid?.Layout != null)
            {
                coord = grid.Layout.GetCoordinate(transform.position);
                return true;
            }

            coord = default;
            return false;
        }
    }
}
