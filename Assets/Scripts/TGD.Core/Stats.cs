namespace TGD.Core
{
    /// <summary>最小属性面板（整数化，避免小数漂移）。</summary>
    public class Stats
    {
        public int Level;

        // 生命/恢复
        public int MaxHP, HP;
        public int Stamina;

        // 攻击
        public int Attack;

        // 能量/职业资源
        public int Energy, MaxEnergy, EnergyRegenPer2s;

        // Posture (class exclusive resource)
        public int Posture, MaxPosture;

        // 防御/暴击
        public int Armor;
        public int Crit;            // 30 点 = 1% 暴击
        public int CritDamage = 200; // %，基线200%

        // 职业精通
        public int Mastery;

        // 速度（秒，影响回合时长；冷却不受影响）
        public int Speed;

        // 移速（格/秒）
        public int MoveSpeed;

        public void Clamp()
        {
            if (HP > MaxHP) HP = MaxHP;
            if (HP < 0) HP = 0;
            if (Energy > MaxEnergy) Energy = MaxEnergy;
            if (Energy < 0) Energy = 0;
        }
    }
}

