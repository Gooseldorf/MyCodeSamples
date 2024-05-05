using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct OutFlowField : IComponentData, ICustomManaged<OutFlowField>
{
    public NativeArray<float2> Directions; 
    private int width;
    private int height;

    public OutFlowField(int length, int width)
    {
        Directions = new NativeArray<float2>(length, Allocator.Persistent);
        this.width = width;
        height = length / width;;
    }

    public float2 GetDirection(int2 position)
    {
        if (math.isnan(position).x || math.isnan(position).y || position.x <= 0 || position.y <= 0 || position.x >= width || position.y >= height)
        {
            return float2.zero;
        }

        return Directions[position.x + position.y * width];
    }

    public void Load(OutFlowField from)
    {
        Directions.CopyFrom(from.Directions);
        width = from.width;
        height = from.height;
    }

    public OutFlowField Clone()
    {
        return new OutFlowField()
        {
            Directions = new NativeArray<float2>(Directions, Allocator.Persistent),
            width = this.width,
            height = this.height
        };
    }

    public void Dispose() => Directions.Dispose();
}
