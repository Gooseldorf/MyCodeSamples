using ECSTest.Aspects;
using ECSTest.Components;
using Sirenix.OdinInspector;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct TeleportationMovingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<OutFlowField>();
        state.RequireForUpdate<InFlowField>();
        state.RequireForUpdate<RandomComponent>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new TeleportationJob()
            {
                InFlowField = SystemAPI.GetSingleton<InFlowField>(),
                OutFlowField = SystemAPI.GetSingleton<OutFlowField>(),
                DeltaTime = SystemAPI.Time.DeltaTime,
                RandomComponent = SystemAPI.GetSingleton<RandomComponent>(),
            }
            .Schedule();
    }

    [BurstCompile(CompileSynchronously = true)]
    private partial struct TeleportationJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public InFlowField InFlowField;
        [ReadOnly] public OutFlowField OutFlowField;
        public RandomComponent RandomComponent;

        public void Execute(ref TeleportationMovable teleportation, MoveAspect moveAspect, ref AnimationComponent animationComponent)
        {
            if(moveAspect.DestroyComponent.ValueRO.IsNeedToDestroy)
                return;
            
            if (moveAspect.StunComponent.ValueRO.Time > 0)
                return;

            teleportation.JumpTime += DeltaTime;

            if (teleportation.JumpTime > teleportation.MaxTime)
            {
                teleportation.JumpTime = 0;

                Random random = RandomComponent.GetRandom(JobsUtility.ThreadIndex);

                int countJumps = random.NextInt(teleportation.MinCountJumps, teleportation.MaxCountJumps + 1);

                bool isGoingIn = moveAspect.MovableComponent.ValueRO.IsGoingIn;
                int2 gridPos = new((int)moveAspect.PositionComponent.ValueRW.Position.x, (int)moveAspect.PositionComponent.ValueRW.Position.y);


                for (int i = 0; i < countJumps; i++)
                {
                    float2 cellDirection = isGoingIn ? InFlowField.GetDirection(gridPos) : OutFlowField.GetDirection(gridPos);

                    if (cellDirection.Equals(float2.zero))
                        return;

                    gridPos += (int2)math.normalize(cellDirection);
                }

                float2 outPosition = gridPos + new float2(.1f) + random.NextFloat2(new(.8f));
                RandomComponent.SetRandom(random, JobsUtility.ThreadIndex);

                moveAspect.PositionComponent.ValueRW.Position = outPosition;
                moveAspect.PositionComponent.ValueRW.Direction = isGoingIn ? InFlowField.GetDirection(gridPos) : OutFlowField.GetDirection(gridPos);
                animationComponent.Direction = moveAspect.PositionComponent.ValueRO.Direction;
            }
        }
    }
}
