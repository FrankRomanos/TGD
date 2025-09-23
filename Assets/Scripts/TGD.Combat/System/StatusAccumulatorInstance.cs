using System.Collections.Generic;
using TGD.Combat;
using TGD.Data;

namespace TGD.Combat
{
    public sealed class StatusAccumulatorInstance
    {
        readonly StatusInstance _owner;
        readonly StatusAccumulatorOpConfig _cfg;
        readonly ICombatEventBus _bus;
        readonly ICombatTime _time;
        readonly RuntimeCtx _ctx;
        readonly Queue<(float t, float amt)> _q = new();

        public StatusAccumulatorInstance(StatusInstance owner, StatusAccumulatorOpConfig cfg,
            ICombatEventBus bus, ICombatTime time, RuntimeCtx ctx)
        {
            _owner = owner; _cfg = cfg; _bus = bus; _time = time; _ctx = ctx;
            _bus.OnDamageResolved += OnDamageResolved;
            _owner.OnExpire += OnExpire;
            _owner.OnDispel += OnDispel;
        }

        void OnDamageResolved(Unit atk, Unit tgt, float post, bool isDot, DamageSchool school, float t)
        {
            if (tgt != _owner.Target) return;
            if (!_cfg.IncludeDotHot && isDot) return;
            if (_cfg.DamageSchool.HasValue && _cfg.DamageSchool.Value != school) return;

            if (_cfg.From == StatusAccumulatorContributor.CasterOnly && atk != _owner.Source) return;
            if (_cfg.From == StatusAccumulatorContributor.Allies && !atk.IsAllyOf(_owner.Source)) return;

            float now = _time.Now; float win = _cfg.WindowSeconds > 0 ? _cfg.WindowSeconds : 12f;
            while (_q.Count > 0 && now - _q.Peek().t > win) _q.Dequeue();
            _q.Enqueue((now, post));
        }

        float Sum()
        {
            float now = _time.Now; float win = _cfg.WindowSeconds > 0 ? _cfg.WindowSeconds : 12f;
            while (_q.Count > 0 && now - _q.Peek().t > win) _q.Dequeue();
            float s = 0; foreach (var e in _q) s += e.amt; return s;
        }

        void OnExpire() => FinalizeAndTrigger(onDispel: false);
        void OnDispel() => FinalizeAndTrigger(onDispel: true);

        void FinalizeAndTrigger(bool onDispel)
        {
            _bus.OnDamageResolved -= OnDamageResolved;
            _owner.OnExpire -= OnExpire;
            _owner.OnDispel -= OnDispel;
            if (onDispel) return;

            var value = Sum();
            var effCtx = _owner?.BuildEffectContext();
            if (effCtx == null)
                return;

            effCtx.ConditionOnEffectEnd = true;
            effCtx.ConditionEventTarget ??= _owner.Target;

            if (!string.IsNullOrWhiteSpace(_cfg?.VariableKey))
                effCtx.CustomVariables[_cfg.VariableKey] = value;

            if (effCtx.SkillResolver == null)
                effCtx.SkillResolver = _ctx?.SkillResolver;

            if (_ctx?.Allies != null)
            {
                foreach (var ally in _ctx.Allies)
                {
                    if (ally != null && !effCtx.Allies.Contains(ally))
                        effCtx.Allies.Add(ally);
                }
            }

            if (_ctx?.Enemies != null)
            {
                foreach (var enemy in _ctx.Enemies)
                {
                    if (enemy != null && !effCtx.Enemies.Contains(enemy))
                        effCtx.Enemies.Add(enemy);
                }
            }

            _ctx?.Logger?.Log("ACCUMULATOR_FINAL", _owner.Target?.UnitId, value);

            var preview = EffectInterpreter.InterpretSkill(effCtx);
            var ops = EffectResolver.Resolve(preview, effCtx);
            if (_ctx != null)
                EffectOpRunner.Run(ops, _ctx);
        }
    }
}
