using UnityEngine;
namespace TGD.Data
{

    public enum ActionType
    {
        None,
        Standard,    // 标准动作
        Reaction,    // 反应动作
        Derived,     // 派生动作
        Continuous,  // 持续动作
        Free,        // 自由动作
        FullRound    // 整轮动作
    }
}
