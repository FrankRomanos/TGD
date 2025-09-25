// Assets/Scripts/TGD.UI/BaseTurnUiBehaviour.cs
using UnityEngine;
using TGD.Combat;

namespace TGD.UI
{
    /// <summary>
    /// 统一：查找 CombatLoop、订阅/退订回合事件、在启用时若已有激活单位则补一次回调。
    /// </summary>
    public abstract class BaseTurnUiBehaviour : MonoBehaviour
    {
        [Header("Combat (optional)")]
        [SerializeField] protected CombatLoop combat;

        protected virtual void Awake()
        {
            if (!combat) combat = FindFirstObjectByTypeSafe<CombatLoop>();
        }

        protected virtual void OnEnable()
        {
            if (!combat) return;
            combat.OnTurnBegan += HandleTurnBegan;
            combat.OnTurnEnded += HandleTurnEnded;

            // 场景已在跑：补一次
            var cur = combat.GetActiveUnit();
            if (cur != null) HandleTurnBegan(cur);
        }

        protected virtual void OnDisable()
        {
            if (!combat) return;
            combat.OnTurnBegan -= HandleTurnBegan;
            combat.OnTurnEnded -= HandleTurnEnded;
        }

        protected abstract void HandleTurnBegan(Unit u);
        protected abstract void HandleTurnEnded(Unit u);

#if UNITY_2023_1_OR_NEWER
        protected static T FindFirstObjectByTypeSafe<T>() where T : Object
            => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
        protected static T FindFirstObjectByTypeSafe<T>() where T : Object
            => Object.FindObjectOfType<T>();
#endif
    }
}
