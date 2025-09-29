// File: TGD.CoreV2/UnitRuntimeContext.cs
using UnityEngine;

namespace TGD.CoreV2
{
    /// ��λ����ʱ�����ģ�ͳһ���� Stats/Cooldown �ȣ����ṩ���ֻ������
    [DisallowMultipleComponent]
    public sealed class UnitRuntimeContext : MonoBehaviour
    {
        [Header("Core Refs")]
        [Tooltip("�õ�λ������ս����ֵ�����л�������")]
        public StatsV2 stats = new StatsV2();
        public CooldownHubV2 cooldownHub;

        [Header("Fallbacks (for tests)")]
        [Tooltip("�� stats Ϊ��ʱ���ڲ��Ե�Ĭ�� MoveRate")]
        public float fallbackMoveRate = 5f;
        // File: TGD.CoreV2/UnitRuntimeContext.cs
        // ������������������Ա�������
        public bool Entangled => stats != null && stats.IsEntangled; // �� ����������ֻ��������¶ StatsV2


        // ========= ���ֻ�����ʣ�ͳһ��ڣ��ⲿϵͳֻ����Щ�� =========
        // ���� �ƶ� ���� 
        public int MoveRate => (stats != null) ? stats.MoveRate : Mathf.Max(1, Mathf.RoundToInt(fallbackMoveRate));
        // �� �������������٣���д��д�� Stats �� fallback��
        public int BaseMoveRate
        {
            get => stats != null ? Mathf.Max(1, stats.MoveRate)
                                 : Mathf.Max(1, Mathf.RoundToInt(fallbackMoveRate));
            set
            {
                int v = Mathf.Max(1, value);
                if (stats != null) stats.MoveRate = v;
                else fallbackMoveRate = v;
            }
        }
        //speed
        public int Speed => (stats != null) ? stats.Speed : 0;
        // ���� ���� ���� 
        public int Energy => stats != null ? stats.Energy : 0;
        public int MaxEnergy => stats != null ? stats.MaxEnergy : 0;

        // ���� ����/������/�������� StatsV2 �Ķ���ֱͨ��¶��������⹫ʽ�� ���� 
        public int Attack => stats != null ? stats.Attack : 0;
        public float PrimaryP => stats != null ? stats.PrimaryP : 0f;          // �����Ի����İٷֱȣ�С����
        public float CritChance => stats != null ? stats.CritChance : 0f;        // [0,1] ��������
        public float CritOverflow => stats != null ? stats.CritOverflow : 0f;      // ��ñ���֣�С������>0��
        public float CritMult => stats != null ? stats.CritMult : 2f;          // ���� 2.0 = 200%
        public float Mastery => stats != null ? stats.Mastery : 0f;           // ��>1 �ľ�ͨ

        // ���� ��������Ͱ/����ֱͨ����Ҫ���ã�û�ÿ��Ժ��ԣ� ���� 
        public float DmgBonusA_P => stats != null ? stats.DmgBonusA_P : 0f;
        public float DmgBonusB_P => stats != null ? stats.DmgBonusB_P : 0f;
        public float DmgBonusC_P => stats != null ? stats.DmgBonusC_P : 0f;
        public float ReduceA_P => stats != null ? stats.ReduceA_P : 0f;
        public float ReduceB_P => stats != null ? stats.ReduceB_P : 0f;
        public float ReduceC_P => stats != null ? stats.ReduceC_P : 0f;

        // ========= ���� =========
        [Header("Debug")]
        public bool debugLog = true;

        void OnValidate()
        {
            if (stats != null) stats.Clamp();
        }

        [ContextMenu("Debug/Print Snapshot")]
        public void PrintSnapshot()
        {
            if (!debugLog) return;
            Debug.Log(
                $"[UnitCtx] MR={MoveRate}  Energy={Energy}/{MaxEnergy}  " +
                $"ATK={Attack}  PrimaryP={PrimaryP:F3}  Crit={CritChance:P1} (Overflow={CritOverflow:P1})  CritMult={CritMult:F2}  " +
                $"Mastery={Mastery:F3}  BonusA={DmgBonusA_P:P0} BonusB={DmgBonusB_P:P0} BonusC={DmgBonusC_P:P0}  ReduceA={ReduceA_P:P0} ReduceB={ReduceB_P:P0} ReduceC={ReduceC_P:P0}",
                this
            );
        }
        [SerializeField] bool _permanentSlowApplied = false;
        public bool TryApplyPermanentSlowOnce(float mult, out int before, out int after)
        {
            before = MoveRate; after = before;
            if (_permanentSlowApplied) return false;

            mult = Mathf.Clamp(mult, 0.1f, 1f);
            int target = Mathf.Max(1, Mathf.FloorToInt(before * mult));
            if (BaseMoveRate > target) BaseMoveRate = target;
            after = MoveRate;
            _permanentSlowApplied = true;
            return true;
        }
    }
}

