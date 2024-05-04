using ECSTest.Components;
using ECSTest.Structs;
using ECSTest.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public static class FlowFieldStaticMethods
{
    /// <summary>
    /// Fills the start cell data in the given cells array with the specified parameters.
    /// </summary>
    public static void FillStartCellData(NativeArray<Cell> cells, int width, int2 position, float cost, NativeList<CellData> startCellData)
    {
        int cellIndex = GetIndexInMatrix(position, width);
        if (cellIndex >= cells.Length || cellIndex < 0)
        { 
            Debug.LogError($"CellIndex invalid!"); 
            return;
        }
        Cell cell = cells[cellIndex];
        cell.IntegrationCost = cost;
        cell.IsCanWalk = true;
        cells[position.x + position.y * width] = cell;
        startCellData.Add(new CellData(cells[cellIndex], position));
    }

    /// <summary>
    /// Clears the integration costs and sets the walkability flag of each cell in the given array.
    /// </summary>
    public static void ClearIntegrationCosts(NativeArray<Cell> cells)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            Cell cell = cells[i];
            cell.IntegrationCost = FlowFieldBuildCacheSystem.LockCost;
            cell.IsCanWalk = false;
            cells[i] = cell;
        }
    }

    /// <summary>
    /// Sets integration costs for given cells based on their proximity to portals.
    /// </summary>
    public static void SetIntegrationCosts(NativeArray<Cell> cells, int width, int height, NativeArray<PortalComponent> portals, NativeList<CellData> cellsData, string direction)
    {
        while (cellsData.Length > 0)
        {
            GetNearestCells(cells, width, height, portals, cellsData[0], cellsData);
            cellsData.RemoveAt(0);
        }
    }

    /// <summary>
    /// Sets the directions of the cells in the given directions array based on the conditions of the cells (isWalkable, isWall).
    /// </summary>
    public static void SetDirections(NativeArray<float2> directions, NativeArray<Cell> cells, int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int cellIndex = GetIndexInMatrix(x, y, width);

                if (cells[cellIndex].IsWall || !cells[cellIndex].IsCanWalk)
                {
                    directions[cellIndex] = float2.zero;
                    continue;
                }

                SetCellDirection(directions, cells, width, height, new int2(x, y));
            }
        }
    }

    /// <summary>
    /// Retrieves the nearest cells to a given cell and updates their properties based on certain conditions.
    /// </summary>
    private static void GetNearestCells(NativeArray<Cell> cells, int width, int height, NativeArray<PortalComponent> portals, CellData cellData, NativeList<CellData> newCellsData)
    {
        if (IsInOutPortal(cellData.Position, portals, out PortalComponent portal))
        {
            int2 position = portal.In.GridPos;

            int x, y, neighbourIndex;

            for (int i = 0; i < portal.In.GridSize.x; i++)
            {
                for (int j = 0; j < portal.In.GridSize.y; j++)
                {
                    x = position.x + i;
                    y = position.y + j;
                    neighbourIndex = GetIndexInMatrix(x, y, width);

                    CellData neighbourData = new(cells[neighbourIndex], new int2(x, y));

                    if (!neighbourData.Cell.IsWall && neighbourData.Cell.IntegrationCost > cellData.Cell.IntegrationCost)
                    {
                        neighbourData.Cell.IntegrationCost = cellData.Cell.IntegrationCost;

                        Cell cell = cells[neighbourIndex];
                        cell.IntegrationCost = cellData.Cell.IntegrationCost;
                        cell.IsCanWalk = true;
                        cells[neighbourIndex] = cell;

                        newCellsData.Add(neighbourData);
                    }
                }
            }

            return;
        }

        for (int i = -1; i < 2; i++)
        {
            for (int j = -1; j < 2; j++)
            {
                SetNeighbour(cells, width, height, portals, cellData, i, j, newCellsData);
            }
        }
    }

    /// <summary>
    /// Sets the neighbour for a given cell.
    /// </summary>
    private static void SetNeighbour(NativeArray<Cell> cells, int width, int height, NativeArray<PortalComponent> portals, CellData cellData, int offsetX, int offsetY, NativeList<CellData> newCellsData)
    {
        if (offsetX == 0 && offsetY == 0)
            return;

        int x = offsetX + cellData.Position.x;
        int y = offsetY + cellData.Position.y;
        if (x < 0 || x >= width || y < 0 || y >= height)
        {
            return;
        }

        if (offsetX * offsetY != 0 &&
            cells[GetIndexInMatrix(x, cellData.Position.y, width)].IsWall &&
            cells[GetIndexInMatrix(cellData.Position.x, y, width)].IsWall)
        {
            return;
        }

        int cellIndex = GetIndexInMatrix(x, y, width);

        if (cells[cellIndex].IsWall)
            return;

        if (IsInPortal(new int2(x, y), portals, out _))
            return;

        float modificator = (offsetX * offsetY == 0) ? 1 : 1.4f;
        CellData neighbour = new(cells[cellIndex], new int2(x, y));
        AddToCellDataList(cells, width, cellData, newCellsData, neighbour, modificator);
    }

    /// <summary>
    /// Adds a cell to the cell data list.
    /// </summary>
    private static void AddToCellDataList(NativeArray<Cell> cells, int width, CellData cellData, NativeList<CellData> newCellsData, CellData neighbourData, float modificator)
    {
        float calculatedIntegrationCost = cellData.Cell.IntegrationCost + (neighbourData.Cell.BaseCost + neighbourData.Cell.DiscomfortCost) * modificator;

        if (!neighbourData.Cell.IsWall && neighbourData.Cell.IntegrationCost > calculatedIntegrationCost)
        {
            neighbourData.Cell.IntegrationCost = calculatedIntegrationCost;
            int cellIndex = GetIndexInMatrix(neighbourData.Position.x, neighbourData.Position.y, width);

            Cell cell = cells[cellIndex];
            cell.IntegrationCost = calculatedIntegrationCost;
            cell.IsCanWalk = true;
            cells[cellIndex] = cell;

            for (int i = 0; i < newCellsData.Length; i++)
            {
                if (newCellsData[i].Position.x == neighbourData.Position.x && newCellsData[i].Position.y == neighbourData.Position.y)
                {
                    if (newCellsData[i].Cell.IntegrationCost > neighbourData.Cell.IntegrationCost)
                    {
                        newCellsData[i] = neighbourData;
                        return;
                    }

                    return;
                }
            }

            newCellsData.Add(neighbourData);
        }
    }

    /// <summary>
    /// Sets the direction of the given cells based on the neighboring cells.
    /// </summary>
    private static void SetCellDirection(NativeArray<float2> directions, NativeArray<Cell> cells, int width, int height, int2 position)
    {
        NativeList<int2> pairList = new(Allocator.Temp);

        for (int i = -1; i < 2; i++)
            for (int j = -1; j < 2; j++)
                TryGetNeighbors(width, height, position, i, j, pairList);

        ChooseMinimalCost(directions, cells, width, pairList, position);
        pairList.Dispose();
    }

    /// <summary>
    /// Chooses the minimal cost direction to move in a grid based on integration and discomfort costs.
    /// </summary>
    private static void ChooseMinimalCost(NativeArray<float2> directions, NativeArray<Cell> cells, int width, NativeList<int2> pairList, int2 currentPosition)
    {
        float minimalCost = float.MaxValue;
        int2 minimalCostPosition = new();

        for (int i = 0; i < pairList.Length; i++)
        {
            Cell cell = cells[GetIndexInMatrix(pairList[i], width)];
            if (cell.IsWall) continue;

            if (cell.IntegrationCost + cell.DiscomfortCost < minimalCost)
            {
                minimalCost = cell.IntegrationCost + cell.DiscomfortCost;
                minimalCostPosition = pairList[i];
            }
        }

        float2 direction = math.normalize(minimalCostPosition - currentPosition);

        directions[GetIndexInMatrix(currentPosition, width)] = direction;
    }

    /// <summary>
    /// Tries to get the neighbor at an offset position.
    /// </summary>
    /// <param name="positions">A list to store the resulting neighbor positions.</param>
    private static void TryGetNeighbors(int width, int height, int2 position, int offsetX, int offsetY, NativeList<int2> positions)
    {
        if ((offsetX == 0 && offsetY == 0) || (offsetX != 0 && offsetY != 0))
            return;

        int x = offsetX + position.x;
        int y = offsetY + position.y;

        if (x < 0 || x >= width || y < 0 || y >= height)
            return;

        positions.Add(new int2(x, y));
    }

    /// <summary>
    /// Calculates the index of a 2D coordinate in a 1D array.
    /// </summary>
    private static int GetIndexInMatrix(int2 xy, int width)
    {
        return xy.x + xy.y * width;
    }

    /// <summary>
    /// Calculates the index of a 2D coordinate in a 1D array.
    /// </summary>
    private static int GetIndexInMatrix(int x, int y, int width) => x + y * width;

    /// <summary>
    /// Checks if the target position is inside any of the in portals.
    /// </summary>
    public static bool IsInPortal(int2 targetPosition, NativeArray<PortalComponent> portals, out PortalComponent foundedPortal)
    {
        foreach (PortalComponent portal in portals)
        {
            int2 position = portal.In.GridPos;

            for (int i = 0; i < portal.In.GridSize.x; i++)
            {
                for (int j = 0; j < portal.In.GridSize.y; j++)
                {
                    if (position.x + i == targetPosition.x && position.y + j == targetPosition.y)
                    {
                        foundedPortal = portal;
                        return true;
                    }
                }
            }
        }

        foundedPortal = default;
        return false;
    }

    /// <summary>
    /// Checks if the target position is inside any of the out portals.
    /// </summary>
    private static bool IsInOutPortal(int2 targetPosition, NativeArray<PortalComponent> portals, out PortalComponent foundedPortal)
    {
        foreach (PortalComponent portal in portals)
        {
            int2 position = portal.Out.GridPos;

            for (int i = 0; i < portal.Out.GridSize.x; i++)
            {
                for (int j = 0; j < portal.Out.GridSize.y; j++)
                {
                    if (position.x + i == targetPosition.x && position.y + j == targetPosition.y)
                    {
                        foundedPortal = portal;
                        return true;
                    }
                }
            }
        }

        foundedPortal = default;
        return false;
    }
}
