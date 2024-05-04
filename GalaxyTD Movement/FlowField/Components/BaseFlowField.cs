using ECSTest.Structs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


public struct BaseFlowField : IComponentData, ICustomManaged<BaseFlowField>
{
    public const int CellSize = 1;

    public NativeArray<Cell> Cells;
    public int Width;
    public int Height;
    
    private readonly float offset;

    public BaseFlowField(NativeArray<Cell> cells, int width, int height)
    {
        Cells = cells;
        Width = width;
        Height = height;
        offset = (float)CellSize / 2;
    }
    
    public Cell this[int x, int y]
    {
        get => Cells[x + y * Width];
        set => Cells[x + y * Width] = value;
    }

    public void Dispose() => Cells.Dispose();

    public BaseFlowField Clone()
    {
        return new BaseFlowField()
        {
            Cells = new NativeArray<Cell>(Cells, Allocator.Persistent),
            Height = Height,
            Width = Width
        };
    }
    public void Load(BaseFlowField from)
    {
        Cells.CopyFrom(from.Cells);
        Height = from.Height;
        Width = from.Width;
    }

    public static float3 GetCenterPosition(int2 gridPos, int2 gridSize) =>
        new(gridPos + (float2)gridSize / 2, 0);

    public void ChangeFlowField(GridPositionStruct gridPosition, bool isLockState)
    {
        float2 startPosition = GetCenterPosition(gridPosition.GridPos, new int2(CellSize, CellSize)).xy + new float2(-offset, -offset);
        float2 leftPoint = startPosition + gridPosition.GridSize;

        int2 topLeftPoint = (int2)startPosition;
        int2 botRightPoint = (int2)leftPoint;

        for (int x = topLeftPoint.x; x < botRightPoint.x; x++)
        {
            for (int y = topLeftPoint.y; y < botRightPoint.y; y++)
            {
                Cell cell = this[x, y];
                if (isLockState && !cell.IsWall)
                {
                    cell.SetLockCost();
                }
                else if (cell.IsWall && !isLockState)
                {
                    cell.SetDefaultCost();
                }

                this[x, y] = cell;
            }
        }
    }
    
    public float GetMoveSpeedModifier(int2 position)
    {
#if UNITY_EDITOR
        if (math.isnan(position).x || math.isnan(position).y ||
            position.x <= 0 || position.y <= 0)
        {
            Debug.LogError($"position is invalid: (Critical Error)| position: {position}");
            return 1;
        }
#endif
        return this[position.x, position.y].MoveSpeedModifier;
    }
}
