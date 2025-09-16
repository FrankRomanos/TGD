using UnityEngine;
using UnityEngine.Rendering;

public class Testing : MonoBehaviour
{
    [SerializeField] private Unit unit;

    private void Start()
    {
        //gridSystem = new GridSystem(10, 10, 2f);
        //Debug.Log(new GridPosition(5, 7));
        //gridSystem.CreatDebugObject(gridDebugObjectPrefab);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            GridSystemVisual.Instance.HideAllGridPosition();
            GridSystemVisual.Instance.ShowAllGridPositionList(
                unit.GetMoveAction().GetValidActionGridPosition());
        }
    }

}
