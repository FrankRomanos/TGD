using Unity.VisualScripting;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GridSystem
{
    private int width;
    private int height;
    private float cellSize;
    private GridObject[,] gridObjectsArray;
    public GridSystem(int width,int height,float cellSize) 
    {
        this.width = width;
        this.height = height;
        this.cellSize = cellSize;

        gridObjectsArray = new GridObject[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                GridPosition gridPosition = new GridPosition(x,z);
                gridObjectsArray[gridPosition.x,gridPosition.z] = new GridObject(this, gridPosition);
            }
        }
    }

    public Vector3 GetWorldPosition(GridPosition gridPosition)
    {
        return new Vector3(gridPosition.x,0,gridPosition.z)*cellSize;
    }

    public GridPosition GetGridPosition(Vector3 worldPosition)
    {
        return new GridPosition(
            Mathf.RoundToInt(worldPosition.x / cellSize),Mathf.RoundToInt(worldPosition.z/cellSize)
            );
    } 

    public void CreatDebugObject(Transform debugPrefab)
    {
        for(int x = 0; x < width;x++)
        {
            for( int z = 0; z < height; z++)
            {
                GridPosition gridPosition = new GridPosition(x,z);

                Transform debugTransform = GameObject.Instantiate(debugPrefab,GetWorldPosition(gridPosition),Quaternion.identity);
                GridDebugObject gridDebugObject = debugTransform.GetComponent<GridDebugObject>();
                gridDebugObject.SetGridObject(GetGridObject(gridPosition));
            }
        }
    }
    public GridObject GetGridObject(GridPosition gridPosition)
    { 
        return gridObjectsArray[gridPosition.x,gridPosition.z];
    }

    public bool IsValidGridPosition(GridPosition gridPosition)
    {
        return gridPosition.x >= 0 
            && gridPosition.z >= 0 
            && gridPosition.x < width 
            && gridPosition.z < height;
    }

    public int GetWidth() { return width; }
    public int GetHeight() { return height; }

    public void hideAllGridPosition()
    {

    }

    public void showAllGridPositionList(List<GridPosition> gridPositionsList)
    {

    }
}
