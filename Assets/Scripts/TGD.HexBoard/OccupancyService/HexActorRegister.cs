using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    public sealed class HexActorRegister : MonoBehaviour
    {
        public HexOccupancyService occService;
        public HexBoardTestDriver driver;           // 直接复用你现有的 TestDriver
        public FootprintShape footprint;        // 这里拖 4 格的占位 SO

        UnitGridAdapter _adapter;

        void Start()
        {
            if (occService == null || driver == null || !driver.IsReady) return;
            _adapter = driver.GetComponent<UnitGridAdapter>();
            if (_adapter == null)
                _adapter = driver.gameObject.AddComponent<UnitGridAdapter>();

            _adapter.Unit = driver.UnitRef;
            if (footprint != null)
                _adapter.Footprint = footprint;

            // 按当前 Unit 的位置和朝向注册到共享占位
            occService.Register(_adapter, driver.UnitRef.Position, driver.UnitRef.Facing);
        }

        void OnDisable()
        {
            if (_adapter != null)
                occService.Unregister(_adapter);
            _adapter = null;
        }
    }
}
