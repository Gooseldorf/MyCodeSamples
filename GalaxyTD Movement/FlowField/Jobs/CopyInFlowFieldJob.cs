using ECSTest.Components;
using Unity.Burst;
using Unity.Jobs;

namespace ECSTest.Systems
{
    [BurstCompile]
    public struct CopyInFlowFieldJob : IJob
    {
        public InFlowField InFlowField;
        public InFlowFieldCache InCache;

        public void Execute()
        {
            if (!InCache.IsReadyToCopy[0]) return;
            
            InFlowField.Directions.CopyFrom(InCache.Directions);
            InCache.IsReadyToCopy[0] = false;
        }
    }
}