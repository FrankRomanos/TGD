using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class GridSystemVisual : MonoBehaviour
{
    [SerializeField] private Transform gridSystemVisualSinglePrefab;
    public static GridSystemVisual Instance { get; private set; }
    private GridSystemVisualSingle[,] gridSystemVisualSingleArray;
    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogError("There's more than one UnitAction!" + transform + "-" + Instance);
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    private void Start()
    {
        gridSystemVisualSingleArray = new GridSystemVisualSingle[LevelGrid.Instance.GetWidth(),LevelGrid.Instance.GetHeight()];

        for (int x = 0; x < LevelGrid.Instance.GetWidth(); x++)
        {
            for(int z = 0; z < LevelGrid.Instance.GetHeight(); z++)
            {
                GridPosition gridPosition = new GridPosition(x,z);
                Transform gridSystemVisualSingleTransform =
                    Instantiate(gridSystemVisualSinglePrefab, LevelGrid.Instance.GetWorldPosition(gridPosition),Quaternion.identity);
                gridSystemVisualSingleArray[x,z] = gridSystemVisualSingleTransform.GetComponent<GridSystemVisualSingle>();
            }
        }
    }

    private void Update()
    {
        UpdateGridVisual();
    }

    public void HideAllGridPosition()
    {
        for (int x = 0; x < LevelGrid.Instance.GetWidth(); x++)
        {
            for (int z = 0; z < LevelGrid.Instance.GetHeight(); z++)
            {
                gridSystemVisualSingleArray[x, z].Hide();
            }
        }
    }

    public void ShowAllGridPositionList(List<GridPosition> girdPositionList)
    {
        foreach (GridPosition gridPosition in girdPositionList )
        {
            gridSystemVisualSingleArray[gridPosition.x,gridPosition.z].Show();
        }
    } 

    public void UpdateGridVisual()
    {
        HideAllGridPosition();

        BaseAction selectedAction = UnitAction.Instance.GetSelectedAction();
        // ���������ü�飺ֻ�е�ѡ������Ч����ʱ������ʾ����
        if (selectedAction != null)
        {
            ShowAllGridPositionList(selectedAction.GetValidActionGridPosition());
        }
    }
}
