using ECSTest.Components;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(FlowFieldBuildCacheSystem))]
public partial struct FlowFieldCopyCacheSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<OutFlowFieldCache>();
        state.RequireForUpdate<InFlowFieldCache>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        CopyInFlowFieldJob copyInJob = new()
        {
            InFlowField = SystemAPI.GetSingletonRW<InFlowField>().ValueRW, 
            InCache = SystemAPI.GetSingletonRW<InFlowFieldCache>().ValueRW,
        };
        state.Dependency = copyInJob.Schedule(state.Dependency);
        
        CopyOutFlowFieldJob copyOutJob = new()
        {
            OutFlowField = SystemAPI.GetSingletonRW<OutFlowField>().ValueRW, 
            OutCache = SystemAPI.GetSingletonRW<OutFlowFieldCache>().ValueRW,
        };
        state.Dependency = copyOutJob.Schedule(state.Dependency);
    }
}
