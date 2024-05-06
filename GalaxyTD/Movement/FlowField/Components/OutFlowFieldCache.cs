using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct OutFlowFieldCache : IComponentData, ICustomManaged<OutFlowFieldCache>
{
    public NativeArray<float2> Directions;
    public NativeArray<bool> IsReadyToCopy;

    public OutFlowFieldCache(int length)
    {
        Directions = new NativeArray<float2>(length, Allocator.Persistent);
        IsReadyToCopy = new NativeArray<bool>(1, Allocator.Persistent);
    }

    public void Load(OutFlowFieldCache from)
    {
        Directions.CopyFrom(from.Directions);
    }

    public OutFlowFieldCache Clone()
    {
        return new OutFlowFieldCache()
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
