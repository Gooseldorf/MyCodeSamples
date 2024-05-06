using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct InFlowFieldCache : IComponentData, ICustomManaged<InFlowFieldCache>
{
    public NativeArray<float2> Directions;
    public NativeArray<bool> IsReadyToCopy;

    public InFlowFieldCache(int length)
    {
        Directions = new NativeArray<float2>(length, Allocator.Persistent);
        IsReadyToCopy = new NativeArray<bool>(1, Allocator.Persistent);
    }

    public void Load(InFlowFieldCache from) => Directions.CopyFrom(from.Directions);

    public InFlowFieldCache Clone()
    {
        return new InFlowFieldCache()
        {
            Directions = new NativeArray<float2>(Directions, Allocator.Persistent)
        };
    }

    public void Dispose()
    {
        Directions.Dispose();
        IsReadyToCopy.Dispose();
    }
}
