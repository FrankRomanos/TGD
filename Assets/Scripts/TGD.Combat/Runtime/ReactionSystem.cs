using System.Collections.Generic;
using TGD.Data;

namespace TGD.Combat
{
    /// <summary>
    /// Reaction ˫�׶Σ�
    /// 1) �ռ� Reaction + Free
    /// 2) ����ֻ��ʾ Free ������
    /// ����˳���� Free����ѡ��˳�򣩣��� Reaction�����򣩣����ԭ����
    /// ������ռλ�Ǽܡ�
    /// </summary>
    public static class ReactionSystem
    {
        public static void CollectAndResolve(Unit actor, SkillDefinition skill)
        {
            // TODO: �ռ����ڣ�����UIѡ��
            // var pickedReactions = CollectReactions(...);
            // var pickedFrees     = CollectFrees(...);

            // TODO: ����˳��Free �� Reaction���� �� ԭ������
            // ��ʱ���գ��� ActionSystem.Apply ȥ��ԭ����Ч��
        }

        // private static List<SkillDefinition> CollectReactions(Unit ...){...}
        // private static List<SkillDefinition> CollectFrees(Unit ...){...}
    }
}
