using ECSTest.Components;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct DiscomfortSystem : ISystem
{
    private const float discomfortCost = 1 / 15f;
    private const float degradeRate = 0.005f;

    private int size;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BaseFlowField>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ResetDiscomfortJob resetDiscomfortJob = new()
        {
            BaseFlowField = SystemAPI.GetSingletonRW<BaseFlowField>().ValueRW
        };
        DiscomfortJob discomfortJob = new()
        {
            BaseFlowField = SystemAPI.GetSingletonRW<BaseFlowField>().ValueRW
        };

        state.Dependency = resetDiscomfortJob.Schedule(size, 64, state.Dependency);
        state.Dependency = discomfortJob.Schedule(state.Dependency);
    }

    [BurstCompile(CompileSynchronously = true)]
    private partial struct DiscomfortJob : IJobEntity
    {
        public BaseFlowField BaseFlowField;

        public void Execute(in CreepComponent creep, in PositionComponent positionComponent)
        {
            int x = (int)(positionComponent.Position.x); 
            int y = (int)(positionComponent.Position.y);

            if (x < 0 || y < 0 || x >= BaseFlowField.Width || y >= BaseFlowField.Height)
                return;

            Cell newCell = BaseFlowField[x, y];
            newCell.DiscomfortCost += discomfortCost;

            BaseFlowField[x, y] = newCell;
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    private struct ResetDiscomfortJob : IJobParallelFor
    {
        public BaseFlowField BaseFlowField;

        public void Execute(int index)
        {
            Cell newCell = BaseFlowField.Cells[index];
            if (newCell.DiscomfortCost == 0) return;

            newCell.DiscomfortCost -= newCell.DiscomfortCost * degradeRate;

            if (newCell.DiscomfortCost < .01f)
                newCell.DiscomfortCost = 0;

            BaseFlowField.Cells[index] = newCell;
        }
    }
}
