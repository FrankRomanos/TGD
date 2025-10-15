using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public enum ActionKind
    {
        Standard,
        Derived,
        Free,
        Reaction,
        FullRound
    }

    public readonly struct ActionCostPlan
    {
        public readonly bool valid;
        public readonly int timeSeconds;
        public readonly int energy;
        public readonly int primaryTimeSeconds;
        public readonly int secondaryTimeSeconds;
        public readonly int primaryEnergy;
        public readonly int secondaryEnergy;
        public readonly string detail;

        public ActionCostPlan(
            bool valid,
            int timeSeconds,
            int energy,
            int primaryTimeSeconds = 0,
            int secondaryTimeSeconds = 0,
            int primaryEnergy = 0,
            int secondaryEnergy = 0,
            string detail = null)
        {
            this.valid = valid;
            this.timeSeconds = Math.Max(0, timeSeconds);
            this.energy = Math.Max(0, energy);
            this.primaryTimeSeconds = Math.Max(0, primaryTimeSeconds);
            this.secondaryTimeSeconds = Math.Max(0, secondaryTimeSeconds);
            this.primaryEnergy = Math.Max(0, primaryEnergy);
            this.secondaryEnergy = Math.Max(0, secondaryEnergy);
            this.detail = detail;
        }

        public static ActionCostPlan Invalid(string detail = null)
            => new(false, 0, 0, 0, 0, 0, 0, detail);
    }

    /// 任何“进入瞄准→鼠标移动预览→左键确认→右键/ESC 取消”的动作，都实现这个接口。
    public interface IActionToolV2
    {
        /// 供管理器识别/绑定（如 "Move", "Attack"）
        string Id { get; }

        ActionKind Kind { get; }

        IReadOnlyCollection<string> ChainTags { get; }

        bool CanChainAfter(string previousId, IReadOnlyCollection<string> previousTags);

        ActionCostPlan PlannedCost(Hex hex);

        /// 进入/退出“瞄准模式”时调用（工具自己做上色/预览/清理）
        void OnEnterAim();
        void OnExitAim();

        /// 鼠标 hover 的 hex（管理器每帧算好后转发）
        void OnHover(Hex hex);

        /// 左键确认：工具返回一个“实际执行”的协程（例如移动）。
        /// 管理器会启动这个协程，并在执行期间把模式切到 Busy。
        IEnumerator OnConfirm(Hex hex);
    }
}
