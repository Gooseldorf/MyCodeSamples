using CardTD.Utilities;
using ECSTest.Aspects;
using ECSTest.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct MovingSystem : ISystem
{
    private BaseFlowField baseFlowField;
    private InFlowField inFlowField;
    private OutFlowField outFlowField;
    
    private EntityQuery query;
    
    private const float knockbackDegradeSpeed = 0.1f;
    private const float smallDistance = 0.000000001f;
    
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<RandomComponent>();
        state.RequireForUpdate<CreepsLocator>();
        state.RequireForUpdate<OutFlowField>();
        state.RequireForUpdate<InFlowField>();
        state.RequireForUpdate<BaseFlowField>();
        
        MovementSettingsSO settings = GameServices.Instance.MovementSettingsSo;

        state.EntityManager.AddComponent<MovementSettings>(state.SystemHandle);
        SystemAPI.SetComponent(state.SystemHandle,
            new MovementSettings
            {
                CollisionRangeMultiplier = settings.CollisionRangeMultiplier,
                EvasionForceMultiplier = settings.EvasionForceMultiplier,
                WallsCollisionRangeMultiplier = settings.WallsCollisionRangeMultiplier,
                WallsPushForceMultiplier = settings.WallsPushForceMultiplier,
                CollisionAvoidanceForceMultiplier = settings.CollisionAvoidanceForceMultiplier,
                SeparationForceMultiplier = settings.SeparationForceMultiplier,
                AlignmentForceMultiplier = settings.AlignmentForceMultiplier,
                CohesionForceMultiplier = settings.CohesionForceMultiplier,
            });

        query = new EntityQueryBuilder(Allocator.Temp)
            .WithAspect<MoveAspect>()
            .WithAll<SharedCreepData>()
            .Build(ref state);
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BeginFixedStepSimulationEntityCommandBufferSystem.Singleton singleton = SystemAPI.GetSingleton<BeginFixedStepSimulationEntityCommandBufferSystem.Singleton>();

        state.EntityManager.GetAllUniqueSharedComponents(out NativeList<SharedCreepData> creepStats, Allocator.Temp);

        foreach (SharedCreepData creep in creepStats)
        {
            query.SetSharedComponentFilter(creep);
            new MovementJob()
            {
                BaseFlowField = SystemAPI.GetSingleton<BaseFlowField>(),
                InFlowField = SystemAPI.GetSingleton<InFlowField>(),
                OutFlowField = SystemAPI.GetSingleton<OutFlowField>(),
                Portals = GetPortalsForJob(ref state).ToArray(Allocator.TempJob),
                CreepsLocator = SystemAPI.GetSingleton<CreepsLocator>(),
                DeltaTime = SystemAPI.Time.DeltaTime,
                RandomComponent = SystemAPI.GetSingleton<RandomComponent>(),
                EntityCommandBuffer = singleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter(),
                Settings = SystemAPI.GetComponent<MovementSettings>(state.SystemHandle),
                CreepShared = creep,
                KnockbackDegrade = math.pow(knockbackDegradeSpeed, SystemAPI.Time.DeltaTime)
            }.ScheduleParallel(query);
        }

        creepStats.Dispose();
        query.ResetFilter();
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
    
    [BurstCompile(CompileSynchronously = true)]
    public partial struct MovementJob : IJobEntity
    {
   
        [ReadOnly] public BaseFlowField BaseFlowField;
        [ReadOnly] public InFlowField InFlowField;
        [ReadOnly] public OutFlowField OutFlowField;
        [ReadOnly] public SharedCreepData CreepShared;
        [ReadOnly, DeallocateOnJobCompletion] public NativeArray<PortalComponent> Portals;
        [NativeDisableParallelForRestriction] public CreepsLocator CreepsLocator;
        public RandomComponent RandomComponent;

        internal EntityCommandBuffer.ParallelWriter EntityCommandBuffer;
        public MovementSettings Settings;
        public float DeltaTime;
        public float KnockbackDegrade;
        
        public void Execute([ChunkIndexInQuery] int chunkIndex, MoveAspect moveAspect, Entity thisCreep)
        {
            if(moveAspect.DestroyComponent.ValueRO.IsNeedToDestroy)
                return;
            
            float2 currentPosition = moveAspect.PositionComponent.ValueRW.Position;
            int2 gridPosition = new((int)moveAspect.PositionComponent.ValueRW.Position.x, (int)moveAspect.PositionComponent.ValueRW.Position.y);
            int sortKey = chunkIndex;
            
            if (!IsCreepOnMap(thisCreep, gridPosition, sortKey, currentPosition)) 
                return;
            
            if (FlowFieldStaticMethods.IsInPortal(gridPosition, Portals, out PortalComponent portal))
            {
                Teleport(moveAspect, portal, sortKey, currentPosition);
                return;
            }

            if (moveAspect.StunComponent.ValueRO.Time > 0 || CreepShared.Speed == 0)
            {
                if (CreepShared.Speed != 0)
                    moveAspect.PositionComponent.ValueRW.Direction = math.normalizesafe(moveAspect.PositionComponent.ValueRW.Direction) * 0.0000000001f;
                PushOutOfWalls(ref moveAspect.PositionComponent.ValueRW, CreepShared.CollisionRange * Settings.WallsCollisionRangeMultiplier, Settings.WallsPushForceMultiplier, ref moveAspect.Knockback.ValueRW, ref EntityCommandBuffer, thisCreep, sortKey);
            }
            else
            {
                float mass = moveAspect.CreepComponent.ValueRO.Mass;
                float maxForce = CreepShared.MaxForce * mass;
                bool isGoingIn = moveAspect.MovableComponent.ValueRO.IsGoingIn;
                float maxSpeed = BaseFlowField.GetMoveSpeedModifier(gridPosition) * CreepShared.Speed * moveAspect.MovableComponent.ValueRO.MoveSpeedModifer * (1 - moveAspect.SlowComponent.ValueRO.Percent);

                float2 cellDirection = isGoingIn ? InFlowField.GetDirection(gridPosition) : OutFlowField.GetDirection(gridPosition);
                if (cellDirection.Equals(float2.zero))
                {
                    KillCreep(ref EntityCommandBuffer, thisCreep, sortKey, currentPosition);
                    return;
                }
                
                if (moveAspect.FearComponent.ValueRO.Time > 0)
                    cellDirection = -cellDirection;
                
                float2 desiredVelocity = cellDirection * maxSpeed;
                float2 currentVelocity = moveAspect.PositionComponent.ValueRO.Direction;
                float2 velocityChange = desiredVelocity - currentVelocity;

                PushOutOfWalls(ref moveAspect.PositionComponent.ValueRW, CreepShared.CollisionRange * Settings.WallsCollisionRangeMultiplier, Settings.WallsPushForceMultiplier, ref moveAspect.Knockback.ValueRW, ref EntityCommandBuffer, thisCreep, sortKey);

                NativeList<CreepInfo> nearestCreeps = new(Allocator.Temp);

                for (int i = -1; i <= +1; i++)
                {
                    for (int j = -1; j <= +1; j++)
                    {
                        int index = CreepsLocator.Index(gridPosition.x + i, gridPosition.y + j);
                        if (index == -1 || !CreepsLocator.FastMap.ContainsKey(index)) continue;
                        NativeParallelMultiHashMap<int, CreepInfo>.Enumerator enumerator = CreepsLocator.FastMap.GetValuesForKey(index);
                        foreach (CreepInfo item in enumerator)
                        {
                            if (item.Entity == thisCreep) continue;
                            nearestCreeps.Add(item);
                        }
                    }
                }
                
                NativeArray<float> nearestCreepsDistances = new(nearestCreeps.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                NativeArray<bool> neighborsInFront = new(nearestCreeps.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                float2 relativePosition;
                for (int i = 0; i < nearestCreeps.Length; i++)
                {
                    relativePosition = nearestCreeps[i].Position - currentPosition;
                    nearestCreepsDistances[i] = math.length(relativePosition);
                    neighborsInFront[i] = math.dot(currentVelocity, relativePosition) > 0;
                }

                Collision(ref nearestCreeps, ref nearestCreepsDistances, ref neighborsInFront, mass, isGoingIn, ref moveAspect.PositionComponent.ValueRW, CreepShared.CollisionRange);

                float2 steeringForce = (velocityChange / maxSpeed) * maxForce;
                steeringForce += CollisionAvoidance(ref nearestCreeps, currentPosition, currentVelocity, mass, isGoingIn, maxForce, maxSpeed) * Settings.CollisionAvoidanceForceMultiplier;
                steeringForce += Evasion(ref nearestCreeps, ref nearestCreepsDistances, ref neighborsInFront, ref moveAspect.PositionComponent.ValueRW, currentVelocity, maxForce, CreepShared.CollisionRange * 2) * Settings.EvasionForceMultiplier;
                steeringForce += Separation(ref nearestCreeps, mass, currentPosition, currentVelocity, maxForce, CreepShared.NeighborRange, CreepShared.CollisionRange) * Settings.SeparationForceMultiplier;
                steeringForce += Cohesion(ref nearestCreeps, mass, currentPosition, maxSpeed, currentVelocity, maxForce, isGoingIn) * Settings.CohesionForceMultiplier;
                steeringForce += Alignment(ref nearestCreeps, mass, maxSpeed, currentVelocity, maxForce, isGoingIn) * Settings.AlignmentForceMultiplier;
                
                PushForward(nearestCreeps, isGoingIn, ref moveAspect.PositionComponent.ValueRW);

                velocityChange = steeringForce / mass * DeltaTime;
                velocityChange = currentVelocity + velocityChange;

                float2 realAcceleration = (velocityChange - currentVelocity) / DeltaTime;
                float2 addPosition = realAcceleration * DeltaTime * DeltaTime / 2 + currentVelocity * DeltaTime;

                moveAspect.PositionComponent.ValueRW.Position += addPosition;
                moveAspect.PositionComponent.ValueRW.Direction = velocityChange;

                nearestCreeps.Dispose();
                PushOutOfWalls(ref moveAspect.PositionComponent.ValueRW, CreepShared.CollisionRange * Settings.WallsCollisionRangeMultiplier, Settings.WallsPushForceMultiplier, ref moveAspect.Knockback.ValueRW, ref EntityCommandBuffer, thisCreep, sortKey);
            }

            Knockback(moveAspect);
        }

        private bool IsCreepOnMap(Entity thisCreep, int2 gridPosition, int sortKey, float2 currentPosition)
        {
            if (gridPosition.x <= 0 || gridPosition.y <= 0 || gridPosition.x >= BaseFlowField.Width || gridPosition.y >= BaseFlowField.Height)
            {
                KillCreep(ref EntityCommandBuffer, thisCreep, sortKey, currentPosition);
                return false;
            }

            return true;
        }
        
        private void Knockback(MoveAspect moveAspect)
        {
            moveAspect.PositionComponent.ValueRW.Position += moveAspect.Knockback.ValueRW.Direction * DeltaTime;

            if (!moveAspect.PositionComponent.ValueRO.Direction.Equals(float2.zero) && math.length(moveAspect.PositionComponent.ValueRO.Direction) < smallDistance)
                moveAspect.PositionComponent.ValueRW.Direction = float2.zero;

            //Knockback degradation
            moveAspect.Knockback.ValueRW.Direction *= KnockbackDegrade;
        }
        
        private void Teleport(MoveAspect moveAspect, PortalComponent portal, int sortKey, float2 currentPosition)
        {
            Random random = RandomComponent.GetRandom(JobsUtility.ThreadIndex);
            float2 outPosition = portal.RandomOutPosition(ref random, offset: new float2(.5f));
            RandomComponent.SetRandom(random, JobsUtility.ThreadIndex);

            moveAspect.PositionComponent.ValueRW.Position = outPosition;
            moveAspect.PositionComponent.ValueRW.Direction = new float2(0.01f);
            moveAspect.Knockback.ValueRW.Direction = float2.zero;

            //spawn Event for teleportation Visual
            Entity idOfEntityToBeCreated = EntityCommandBuffer.CreateEntity(sortKey);
            EntityCommandBuffer.AddComponent(sortKey, idOfEntityToBeCreated, new PortalUseEvent { InPosition = currentPosition, OutPosition = outPosition, Portal = portal });
        }
        
        private void Collision(ref NativeList<CreepInfo> neighbors, ref NativeArray<float> neighborsDistances, ref NativeArray<bool> neighborsInFront,
            float mass, bool isGoingIn, ref PositionComponent positionComponent, float collisionRange)
        {
            float pushSpeed = 7f;
            for (int i = 0; i < neighbors.Length; i++)
            {
                float overlap = collisionRange + neighbors[i].CollisionRange - neighborsDistances[i];
                //PowerCell holders are not affected by other creeps
                bool shouldSkip = (!isGoingIn && neighbors[i].IsGoingIn) || neighborsDistances[i] == 0 || overlap <= 0;
                if (shouldSkip)
                    continue;

                float halfPlaneModifier = neighborsInFront[i] ? 0.5f : 1.5f;
                float massFactor = neighbors[i].Mass / (mass + neighbors[i].Mass);
                float2 displacement = DeltaTime * pushSpeed * halfPlaneModifier * overlap * massFactor / neighborsDistances[i] * (positionComponent.Position - neighbors[i].Position);
                positionComponent.Position += displacement;
            }
        }
        
        private void PushForward(NativeList<CreepInfo> neighbors, bool isGoingIn, ref PositionComponent positionComponent)
        {
            for (int i = 0; i < neighbors.Length; i++)
            {
                if (isGoingIn != neighbors[i].IsGoingIn) continue;
                if (math.dot(neighbors[i].Velocity, neighbors[i].Position - positionComponent.Position) > 0.0f) continue;

                float behindSearchDeviationAngle = 90f;
                if (!AreVectorsInSameDirection(positionComponent.Direction, neighbors[i].Velocity, behindSearchDeviationAngle)) continue;

                positionComponent.Position += positionComponent.Direction * DeltaTime;
            }
        }
        
        private bool AreVectorsInSameDirection(float2 vector1, float2 vector2, float deviation)
        {
            float dotProduct = math.dot(vector1, vector2);
            float magnitudeProduct = math.length(vector1) * math.length(vector2);

            float angle = Mathf.Acos(dotProduct / magnitudeProduct);

            float angleDegrees = angle * 180 / Mathf.PI;

            return angleDegrees <= deviation;
        }
        
        private float2 Evasion(ref NativeList<CreepInfo> neighbors, ref NativeArray<float> neighborsDistances,
            ref NativeArray<bool> neighborsInFront, ref PositionComponent positionComponent, float2 myVelocity, float maxForce, float evasionRadius)
        {

            float2 force = float2.zero;
            float closestDistance = float.MaxValue;
            float2 lookAhead1;
            float2 lookAhead2;
            int closestNeighborIndex = -1;
            float2 closestRelativeVelocity = float2.zero;
            for (int i = 0; i < neighbors.Length; i++)
            {
                float2 relativeVelocity = myVelocity - neighbors[i].Velocity;
                if (!neighborsInFront[i] || neighborsDistances[i] == 0 || neighborsDistances[i] > evasionRadius
                    || relativeVelocity.Equals(float2.zero) || neighborsDistances[i] > closestDistance)
                    continue;

                lookAhead1 = relativeVelocity + positionComponent.Position;
                lookAhead2 = relativeVelocity / 2 + positionComponent.Position;

                float dist1 = math.lengthsq(lookAhead1 - neighbors[i].Position);
                float dist2 = math.lengthsq(lookAhead2 - neighbors[i].Position);
                float dist3 = math.lengthsq(positionComponent.Position - neighbors[i].Position);

                if (dist1 < neighbors[i].CollisionRange || dist2 < neighbors[i].CollisionRange || dist3 < neighbors[i].CollisionRange)
                {
                    closestDistance = neighborsDistances[i];
                    closestNeighborIndex = i;
                    closestRelativeVelocity = relativeVelocity;
                }
            }
            if (force.Equals(float2.zero)) return float2.zero;
            
            float2 result = closestRelativeVelocity + positionComponent.Position - neighbors[closestNeighborIndex].Position;
            if (result.Equals(float2.zero)) return float2.zero;
            return math.normalize(result) * maxForce;
        }
        
        private bool PushOutOfWalls(ref PositionComponent positionComponent, float range, float pushForce, ref Knockback knockback, ref EntityCommandBuffer.ParallelWriter ecb, Entity creep, int sortKey)
        {
            float2 finalPosition = positionComponent.Position;
            int x = (int)finalPosition.x;
            int y = (int)finalPosition.y;
            float2 remainder = finalPosition % BaseFlowField.CellSize;

            if (x < 0 || y < 0 || x >= BaseFlowField.Width || y >= BaseFlowField.Height || BaseFlowField[x, y].IsWall)
            {
                KillCreep(ref ecb, creep, sortKey, finalPosition);
                return true;
            }

            bool forcedInTheWall = false;

            int maxX = BaseFlowField.Width - 1;
            int maxY = BaseFlowField.Height - 1;

            if (CheckXPosition(out int xStep, out float xPos))
            {
                if (BaseFlowField[x + xStep, y].IsWall)
                {
                    finalPosition.x = xPos;
                    positionComponent.Direction.x = 0;
                    forcedInTheWall = true;
                }
            }

            if (CheckYPosition(out int yStep, out float yPos) && (BaseFlowField[x, y + yStep].IsWall))
            {
                finalPosition.y = yPos;
                positionComponent.Direction.y = 0;
                forcedInTheWall = true;
            }

            if (forcedInTheWall)
                SquishCreeps(ref knockback, ref ecb, creep, sortKey, finalPosition);

            bool CheckXPosition(out int xFactor, out float xPosition)
            {
                xFactor = 0;
                xPosition = 0;
                if (x > 0 && remainder.x < range)
                {
                    xPosition = x * BaseFlowField.CellSize + range;
                    xFactor = -1;
                }
                else if (x < maxX && (BaseFlowField.CellSize - remainder.x) < range)
                {
                    xPosition = (x + 1) * BaseFlowField.CellSize - range;
                    xFactor = 1;
                }

                return xFactor != 0;
            }

            bool CheckYPosition(out int yFactor, out float yPosition)
            {
                yFactor = 0;
                yPosition = 0;
                if (y > 0 && remainder.y < range)
                {
                    yPosition = y * BaseFlowField.CellSize + range;
                    yFactor = -1;
                }
                else if (y < maxY && (BaseFlowField.CellSize - remainder.y) < range)
                {
                    yPosition = (y + 1) * BaseFlowField.CellSize - range;
                    yFactor = 1;
                }

                return yFactor != 0;
            }

            float2 pushDirection = finalPosition - positionComponent.Position;
            positionComponent.Position += pushDirection * DeltaTime * pushForce;
            return forcedInTheWall;
        }
        
        private static void KillCreep(ref EntityCommandBuffer.ParallelWriter ecb, Entity creep, int sortKey, float2 finalPosition) => KnockBackWallDamage(ref ecb, creep, sortKey, finalPosition, Entity.Null);
        
        private static void KnockBackWallDamage(ref EntityCommandBuffer.ParallelWriter ecb, Entity creep, int sortKey, float2 position, Entity tower, float speed = float.MaxValue)
        {
            KnockBackWallDamageEvent wallDamageEvent = new() { Creep = creep, Position = position, OriginTower = tower, Speed = speed };
            Entity eventEntity = ecb.CreateEntity(sortKey);
            ecb.AddComponent(sortKey, eventEntity, wallDamageEvent);
        }
        
        private static void SquishCreeps(ref Knockback knockback, ref EntityCommandBuffer.ParallelWriter ecb, Entity creep, int sortKey, float2 finalPosition)
        {
            if (knockback.Exists)
            {
                KnockBackWallDamage(ref ecb, creep, sortKey, finalPosition, knockback.OriginTower, math.length(knockback.Direction));
                knockback.Direction = float2.zero;
                knockback.OriginTower = Entity.Null;
            }
        }
        
        private float2 CollisionAvoidance(ref NativeList<CreepInfo> neighbors, float2 position, float2 myVelocity, float mass, bool isGoingIn, float maxForce, float speed)
        {
            if (neighbors.Length == 0 || myVelocity is {x: 0, y: 0} ) return float2.zero;

            float2 force = float2.zero;
            float2 myVelocityNorm = math.normalize(myVelocity);
            float2 leftDir = myVelocityNorm.GetNormal();
            float2 rightDir = (-myVelocityNorm).GetNormal();

            int count = 0;
            for (int i = 0; i < neighbors.Length; i++)
            {
                if (!isGoingIn)
                {
                    if (neighbors[i].IsGoingIn)
                    {
                        continue;
                    }
                }

                if (neighbors[i].Mass < mass) continue; 

                if (math.dot(neighbors[i].Velocity, neighbors[i].Position - position) < 0.0f) continue;

                float2 relativeSpeed = neighbors[i].Velocity - myVelocity;
                float relativeSpeedProjection = math.dot(relativeSpeed, myVelocityNorm);

                if (relativeSpeedProjection > 0) continue; 
                count++;
                
                if (Utilities.IsLeft(position, myVelocity, neighbors[i].Position))
                    force += rightDir * relativeSpeedProjection;
                else
                    force += leftDir * relativeSpeedProjection;
            }

            if (count == 0) return float2.zero;

            float2 result = force / count * maxForce / speed;
            return result;
        }
        
        private float2 Separation(ref NativeList<CreepInfo> neighbors, float myMass, float2 myPosition, float2 myVelocity, float maxForce, float neighborRadius, float myRadius)
        {
            if (neighbors.Length == 0) return float2.zero;

            float2 force = float2.zero;
            float myKineticEnergy = math.lengthsq(myVelocity) * myMass; 
            if (myKineticEnergy == 0) return float2.zero;
            
            for (int i = 0; i < neighbors.Length; i++)
            {
                if (neighbors[i].Mass < myMass) continue;
                float2 pushForce = myPosition - neighbors[i].Position;
                float length = math.length(pushForce);

                if (length == 0 || length > neighborRadius) 
                {
                    continue;
                }

                float scaledForce = ((neighborRadius - length) / neighborRadius);

                force += math.normalize(pushForce) * scaledForce;
            }
            
            float2 result = force / neighbors.Length * maxForce;
            
            return float.IsNaN(result.x) || float.IsNaN(result.y) ? float2.zero : result;
        }
        
        private float2 Alignment(ref NativeList<CreepInfo> neighbors, float myMass, float speed, float2 myVelocity, float maxForce, bool isGoingIn)
        {
            if (neighbors.Length == 0) return float2.zero;
            float2 avgHeading = myVelocity * myMass;
            float mass = myMass;
            for (int i = 0; i < neighbors.Length; i++)
            {
                if (neighbors[i].IsGoingIn != isGoingIn) continue;
                avgHeading += neighbors[i].NormVelocity * neighbors[i].Mass;
                mass += neighbors[i].Mass;
            }

            avgHeading /= mass;
            float2 desired = avgHeading * speed;
            float2 force = desired - myVelocity;

            return force * maxForce / speed;
        }
        
        private float2 Cohesion(ref NativeList<CreepInfo> neighbors, float myMass, float2 myPosition, float speed, float2 myVelocity, float maxForce, bool isGoingIn)
        {
            if (neighbors.Length == 0) return float2.zero;

            float2 massCenter = myPosition * myMass;
            float mass = myMass;

            for (int i = 0; i < neighbors.Length; i++)
            {
                if (neighbors[i].IsGoingIn != isGoingIn) continue;

                massCenter += neighbors[i].Position * neighbors[i].Mass;
                mass += neighbors[i].Mass;
            }

            if (mass == 0) return float2.zero;
            massCenter /= mass;

            float2 desired = massCenter - myPosition;
            float length = math.length(desired);
            if (length != 0)
                desired *= speed / math.length(desired);

            float2 force = desired - myVelocity;
            return force * maxForce / speed;
        }
    }
}
