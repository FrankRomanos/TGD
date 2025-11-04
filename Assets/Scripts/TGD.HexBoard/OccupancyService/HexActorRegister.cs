using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    public sealed class HexActorRegister : MonoBehaviour
    {
        public HexOccupancyService occService;
        public HexBoardTestDriver driver;           // 直接复用你现有的 TestDriver
        public FootprintShape footprint;        // 这里拖 4 格的占位 SO

        IGridActor _actor;

        void Start()
        {
            if (occService == null || driver == null || !driver.IsReady) return;
            _actor = new UnitGridAdapter(driver.UnitRef, footprint);
            // 按当前 Unit 的位置和朝向注册到共享占位
            occService.Register(_actor, driver.UnitRef.Position, driver.UnitRef.Facing);
        }

        void OnDisable()
        {
            if (_actor != null) occService.Unregister(_actor);
            _actor = null;
        }
    }
}
