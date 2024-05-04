using ECSTest.Components;
using ECSTest.Structs;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace ECSTest.Systems
{
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(MovingSystemBase))]
    public partial struct FlowFieldBuildCacheSystem : ISystem
    {
        public const float BaseCost = 10;
        public const float LockCost = 20000;
        private const float baseCostPerEnergyCore = 10;
        private const float exitZoneBaseCost = 0;

        private EntityQuery baseCostChangedEventQuery;
        private EntityQuery powerCellsQuery;
        private EntityQuery energyCoreQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BaseFlowField>();
            state.RequireForUpdate<InFlowFieldCache>();
            state.RequireForUpdate<OutFlowFieldCache>();
            
            energyCoreQuery= new EntityQueryBuilder(Allocator.Temp).WithAll<EnergyCoreComponent, GridPositionComponent>().Build(ref state);
            powerCellsQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<PowerCellComponent, PositionComponent,DestroyComponent>().Build(ref state);
            baseCostChangedEventQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<BaseCostChangedEvent>().Build(ref state);
        }

        public static void Init(World world, Cell[,] cells)
        {
            NativeArray<Cell> nativeCells = new(cells.Length, Allocator.Persistent);

            int width = cells.GetLength(0);
            int height = cells.GetLength(1);

            MatrixToArray(cells, width, height, nativeCells);

            world.EntityManager.CreateSingleton(new BaseFlowField(nativeCells, width, height), "BaseFlowField");

            world.EntityManager.CreateSingleton(new InFlowFieldCache(cells.Length), "InFlowFieldCache");
            world.EntityManager.CreateSingleton(new OutFlowFieldCache(cells.Length), "OutFlowFieldCache");

            world.EntityManager.CreateSingleton(new InFlowField(cells.Length, width), "InFlowField");
            world.EntityManager.CreateSingleton(new OutFlowField(cells.Length, width), "OutFlowField");

            Entity eventEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(eventEntity, new BaseCostChangedEvent());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.CompleteDependencyBeforeRW<BaseFlowField>();
            BaseFlowField baseField;
            if (!baseCostChangedEventQuery.IsEmpty)
            {
                baseField = UpdateBaseCosts(ref state);
                state.EntityManager.DestroyEntity(baseCostChangedEventQuery);
            }
            else
                baseField = SystemAPI.GetSingletonRW<BaseFlowField>().ValueRO;

            ScheduleCacheBuild(ref state, baseField);
        }

        private BaseFlowField UpdateBaseCosts(ref SystemState state)
        {
            BaseFlowField baseFlowField = SystemAPI.GetSingletonRW<BaseFlowField>().ValueRW;

            foreach ((PowerableComponent powerable, GateComponent gateComponent) in SystemAPI.Query<PowerableComponent, GateComponent>())
            {
                baseFlowField.ChangeFlowField(gateComponent.StartPosition, true);
                baseFlowField.ChangeFlowField(gateComponent.EndPosition, true);

                baseFlowField.ChangeFlowField(gateComponent.MiddlePosition, powerable.IsTurnedOn);
            }

            foreach ((GridPositionComponent grid, PowerableComponent powerable) in SystemAPI.Query<GridPositionComponent, PowerableComponent>().WithAll<BridgeComponent>())
            {
                baseFlowField.ChangeFlowField(grid.Value, !powerable.IsTurnedOn);
            }

            return baseFlowField;
        }

        private void ScheduleCacheBuild(ref SystemState state, BaseFlowField baseField)
        {
            NativeList<int2> startPositionsIn = GetPowerCellPoints(out NativeList<float> startCostsIn, baseField);
            InCacheBuildJob inJob = new()
            {
                InCache = SystemAPI.GetSingletonRW<InFlowFieldCache>().ValueRW,
                Cells = new NativeArray<Cell>(baseField.Cells, Allocator.TempJob),
                Height = baseField.Height,
                Width = baseField.Width,
                Portals = GetPortalsForJob(ref state).ToArray(Allocator.TempJob),
                StartPositions = startPositionsIn.ToArray(Allocator.TempJob),
                StartCosts = startCostsIn.ToArray(Allocator.TempJob)
            };

            JobHandle dependencyIn = inJob.Schedule(state.Dependency);

            NativeList<int2> startPositionsOut = GetExitPoints(ref state, out NativeList<float> startCostsOut);

            OutCacheBuildJob outJob = new()
            {
                OutCache = SystemAPI.GetSingletonRW<OutFlowFieldCache>().ValueRW,
                Cells = new NativeArray<Cell>(baseField.Cells, Allocator.TempJob),
                Height = baseField.Height,
                Width = baseField.Width,
                Portals = GetPortalsForJob(ref state).ToArray(Allocator.TempJob),
                StartPositions = startPositionsOut.ToArray(Allocator.TempJob),
                StartCosts = startCostsOut.ToArray(Allocator.TempJob)
            };

            JobHandle dependencyOut = outJob.Schedule(state.Dependency);
            state.Dependency = JobHandle.CombineDependencies(dependencyIn, dependencyOut);
        }
        
        private NativeList<PortalComponent> GetPortalsForJob(ref SystemState state)
        {
            NativeList<PortalComponent> portals = new(Allocator.Temp);
            foreach ((PortalComponent portal, PowerableComponent powerable) in SystemAPI.Query<PortalComponent, PowerableComponent>())
            {
                if (powerable.IsTurnedOn)
                    portals.Add(portal);
            }

            return portals;
        }

        private NativeList<int2> GetPowerCellPoints(out NativeList<float> costs, BaseFlowField baseField)
        {
            NativeList<int2> positions = new(Allocator.Temp);

            costs = new NativeList<float>(Allocator.Temp);

            FillEnergyCorePoints(costs, positions);
            FillFreePowerCellPoints(ref costs, baseField, positions);

            return positions;
        }

        private void FillEnergyCorePoints(NativeList<float> costs, NativeList<int2> positions)
        {
            int activeCoresCount = 0;
            
            NativeArray<EnergyCoreComponent> energyCoreComponents = energyCoreQuery.ToComponentDataArray<EnergyCoreComponent>(Allocator.Temp);
            NativeArray<GridPositionComponent> gridPositionComponents = energyCoreQuery.ToComponentDataArray<GridPositionComponent>(Allocator.Temp);

            for (int i = 0; i < energyCoreComponents.Length; i++)
            {
                if (energyCoreComponents[i].PowerCellCount <= 0)
                    continue;

                NativeList<int2> points = GetAllPositionsFromGridPositionsData(gridPositionComponents[i].Value);

                for (int index = 0; index < points.Length; index++)
                {
                    int2 position = points[index];
                    float cost = -(energyCoreComponents[i].PowerCellCount + baseCostPerEnergyCore + activeCoresCount);

                    positions.Add(position);
                    costs.Add(cost);
                }

                activeCoresCount++;

                points.Dispose();
            }

            energyCoreComponents.Dispose();
            gridPositionComponents.Dispose();
        }

        private void FillFreePowerCellPoints(ref NativeList<float> nativeList, BaseFlowField baseFlowField, NativeList<int2> positions)
        {
            NativeList<int2> movablePositions = new(Allocator.Temp);
            NativeArray<PowerCellComponent> powerCells = powerCellsQuery.ToComponentDataArray<PowerCellComponent>(Allocator.Temp);
            NativeArray<PositionComponent> cellPositions = powerCellsQuery.ToComponentDataArray<PositionComponent>(Allocator.Temp);
            NativeArray<DestroyComponent> destroyComponents = powerCellsQuery.ToComponentDataArray<DestroyComponent>(Allocator.Temp);

            for (int i = 0; i < powerCells.Length; i++)
            {
                if(destroyComponents[i].IsNeedToDestroy)
                    continue;
            
                int2 pos = (int2)cellPositions[i].Position;

                if (powerCells[i].IsMoves && IsPositionInsideMap(pos, baseFlowField))
                {
                    if (powerCells[i].Creep == Entity.Null)
                        AddToPositions(ref nativeList, pos);
                    else
                        movablePositions.Add(pos);
                }
            }

            if (positions.Length == 0 && movablePositions.Length > 0)
            {
                foreach (int2 position in movablePositions)
                    AddToPositions(ref nativeList, position);
            }
        
            powerCells.Dispose();
            cellPositions.Dispose();
            destroyComponents.Dispose();
            movablePositions.Dispose();
            

            bool IsPositionInsideMap(int2 pos, BaseFlowField baseField) =>
                pos.x > 0 && pos.y > 0 && baseField.Width > pos.x && baseField.Height > pos.y;

            void AddToPositions(ref NativeList<float> costs, int2 position)
            {
                int index = positions.IndexOf(position);
                if (index == -1)
                {
                    positions.Add(position);
                    costs.Add(-baseCostPerEnergyCore);
                }
                else
                    costs[index] -= baseCostPerEnergyCore;
            }
        }

        private NativeList<int2> GetExitPoints(ref SystemState state, out NativeList<float> startCosts)
        {
            NativeList<int2> result = new(Allocator.Temp);
            startCosts = new(Allocator.Temp);

            foreach ((ExitPointComponent, GridPositionComponent) data in SystemAPI.Query<ExitPointComponent, GridPositionComponent>())
            {
                NativeList<int2> points = GetAllPositionsFromGridPositionsData(data.Item2.Value);
                foreach (int2 position in points)
                {
                    startCosts.Add(exitZoneBaseCost);
                    result.Add(position);
                }

                points.Dispose();
            }

            return result;
        }

        private NativeList<int2> GetAllPositionsFromGridPositionsData(GridPositionStruct gridPosition)
        {
            NativeList<int2> points = new(Allocator.Temp);
            for (int x = 0; x < gridPosition.GridSize.x; x++)
            {
                for (int y = 0; y < gridPosition.GridSize.y; y++)
                {
                    int2 position = new int2(x, y) + gridPosition.GridPos;
                    points.Add(position);
                }
            }

            return points;
        }
        
        private static void MatrixToArray(Cell[,] cells, int width, int height, NativeArray<Cell> nativeCells)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    nativeCells[x + y * width] = cells[x, y];
                }
            }
        }
    }
}