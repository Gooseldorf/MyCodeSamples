using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ECSTest.Components
{
    public struct InFlowField : IComponentData, ICustomManaged<InFlowField>
    {
        public NativeArray<float2> Directions;
        private int width;
        private int height;

        public InFlowField(int length, int width)
        {
            Directions = new NativeArray<float2>(length, Allocator.Persistent);
            this.width = width;
            height = length / width;
        }

        public float2 GetDirection(int2 position)
        {
            if (math.isnan(position).x || math.isnan(position).y || position.x <= 0 || position.y <= 0 || position.x >= width || position.y >= height)
            {
                return float2.zero;
            }
            
            return Directions[position.x + position.y * width];
        }

        public void Load(InFlowField from)
        {
            Directions.CopyFrom(from.Directions);
            width = from.width;
            height = from.height;
        }

        public InFlowField Clone()
        {
            return new InFlowField()
            {
                Directions = new NativeArray<float2>(Directions, Allocator.Persistent),
                width = this.width,
                height = this.height
            };
        }

        public void Dispose() => Directions.Dispose();
    }
}