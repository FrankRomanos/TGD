namespace TGD.Core
{
    /// <summary>��С������壨������������С��Ư�ƣ���</summary>
    public class Stats
    {
        public int Level;

        // ����/�ָ�
        public int MaxHP, HP;
        public int Stamina;

        // ����
        public int Attack;

        // ����/ְҵ��Դ
        public int Energy, MaxEnergy, EnergyRegenPer2s;

        // Posture (class exclusive resource)
        public int Posture, MaxPosture;

        // ����/����
        public int Armor;
        public int Crit;            // 30 �� = 1% ����
        public int CritDamage = 200; // %������200%

        // ְҵ��ͨ
        public int Mastery;

        // �ٶȣ��룬Ӱ��غ�ʱ������ȴ����Ӱ�죩
        public int Speed;

        // ���٣���/�룩
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

