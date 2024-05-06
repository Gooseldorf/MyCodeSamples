using ECSTest.Components;
using Unity.Burst;
using Unity.Jobs;

[BurstCompile]
public struct CopyOutFlowFieldJob : IJob
{
    public OutFlowField OutFlowField;
    public OutFlowFieldCache OutCache;
    
    public void Execute()
    {
        if (!OutCache.IsReadyToCopy[0]) return;
        
        OutFlowField.Directions.CopyFrom(OutCache.Directions);
        OutCache.IsReadyToCopy[0] = false;
    }
}
