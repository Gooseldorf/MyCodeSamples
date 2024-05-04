using ECSTest.Components;
using ECSTest.Structs;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(CompileSynchronously = true)]
public struct OutCacheBuildJob : IJob
{
    public OutFlowFieldCache OutCache;
    [ReadOnly, DeallocateOnJobCompletion] public NativeArray<PortalComponent> Portals;
    [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int2> StartPositions;
    [ReadOnly, DeallocateOnJobCompletion] public NativeArray<float> StartCosts;
    [DeallocateOnJobCompletion] public NativeArray<Cell> Cells;
    public int Width;
    public int Height;

    public void Execute()
    {
        NativeList<CellData> startCellData = new(Allocator.Temp);

        FlowFieldStaticMethods.ClearIntegrationCosts(Cells);

        for (int i = 0; i < StartPositions.Length; i++)
        {
            FlowFieldStaticMethods.FillStartCellData(Cells, Width, StartPositions[i], StartCosts[i], startCellData);
        }

        FlowFieldStaticMethods.SetIntegrationCosts(Cells, Width, Height, Portals, startCellData, nameof(OutFlowFieldCache));
        FlowFieldStaticMethods.SetDirections(OutCache.Directions, Cells, Width, Height);

        OutCache.IsReadyToCopy[0] = true;
        startCellData.Dispose();
    }
}
