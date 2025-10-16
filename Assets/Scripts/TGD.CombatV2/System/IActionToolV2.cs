using System.Collections;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    /// 任何“进入瞄准→鼠标移动预览→左键确认→右键/ESC 取消”的动作，都实现这个接口。
    public interface IActionToolV2
    {
        /// 供管理器识别/绑定（如 "Move", "Attack"）
        string Id { get; }

        /// 六大动作分类（标准/反应/派生/整轮/持续/自由）
        ActionKind Kind { get; }

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
