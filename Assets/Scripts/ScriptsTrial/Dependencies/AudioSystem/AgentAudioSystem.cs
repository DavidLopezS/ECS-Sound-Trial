using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

[UpdateInGroup(typeof(AudioFrame))]
class MoveByInputBarrier : EntityCommandBufferSystem { }

struct MoveBy : IComponentData
{
    public float delay;

    public float pitchVariance;
    public SoundCollection group;
}

struct PositionMoveByData : IComponentData
{
    public float prevDist;
    public float currDist;
    public float left;
    public float right;
}

public struct SoundCollection
{
    internal int ID;
}

[Serializable]
public struct MoveByParameters
{

    [Range(0, 50)]
    public float MinSpeed;

    [Range(50, 200)]
    public float MaxSpeed;

    [Range(40, 100)]
    public float Fast;

    [Range(0.1f, 1)]
    public float FastPitchMul;

    [Range(0, 1)]
    public float Volume;

    [Range(0, 1)]
    public float lowLayer;

    [Range(0, 1)]
    public float highLayer;
}

[UpdateBefore(typeof(AudioFrame))]
class MoveByOutputBarrier : EntityCommandBufferSystem { }

[UpdateAfter(typeof(MoveByInputBarrier)), UpdateInGroup(typeof(AudioFrame))]
public class AgentAudioSystem : JobComponentSystem
{

    struct SampleInfo
    {
        public Entity playerEntity;
        public float length;
        public int intensity;
    }

    struct MoveByState : ISystemStateComponentData
    {
        public float timeOut;
        public int sampleIndex;
    }

    public int maxVoices => sampleMoveByEntities.Length;

    NativeList<SampleInfo> sampleMoveByEntities;
    NativeList<int> sampleHighFreeList;
    NativeList<int> sampleLowFreeList;

    EntityQuery newMoveByGroup;
    EntityQuery aliveMoveByGroup;
    EntityQuery deadMoveByGroup;

    MoveByOutputBarrier outputs;

    MoveByParameters myParameters = new MoveByParameters();

    public void AddClip(AudioClip clip)
    {
        SamplePlaybackSystem playbackSystem = World.GetOrCreateSystem<SamplePlaybackSystem>();
        playbackSystem.AddClip(clip);
    }

    public void AddHighMoveSounds(SoundCollection collection, AudioClip clip)
    {
        Entity sample = EntityManager.CreateEntity();
        AddClip(clip);
        EntityManager.AddComponentData(sample, new SharedAudioClip { ClipInstanceID = clip.GetInstanceID() });
        sampleMoveByEntities.Add(new SampleInfo { length = clip.length, playerEntity = sample, intensity = 1 });
        sampleHighFreeList.Add(sampleMoveByEntities.Length - 1);
    }

    public void AddLowMoveSounds(SoundCollection collection, AudioClip clip)
    {
        Entity sample = EntityManager.CreateEntity();
        AddClip(clip);
        EntityManager.AddComponentData(sample, new SharedAudioClip { ClipInstanceID = clip.GetInstanceID() });
        sampleMoveByEntities.Add(new SampleInfo { length = clip.length, playerEntity = sample, intensity = 0 });
        sampleLowFreeList.Add(sampleMoveByEntities.Length - 1);
    }

    public void ClearCollections()
    {
        for (int i = 0; i < sampleMoveByEntities.Length; ++i)
            EntityManager.DestroyEntity(sampleMoveByEntities[i].playerEntity);

        sampleMoveByEntities.Clear();
        sampleHighFreeList.Clear();
        sampleLowFreeList.Clear();
    }

    public void SetParameters(MoveByParameters parameters)
    {
        myParameters = parameters;
    }

    public SoundCollection createCollection()
    {
        return new SoundCollection();
    }

    protected override void OnDestroy()
    {
        sampleMoveByEntities.Dispose();
        sampleHighFreeList.Dispose();
        sampleLowFreeList.Dispose();

        newMoveByEnum.Dispose();
        deadMoveByEnum.Dispose();
        aliveMoveByEnum.Dispose();
    }

    protected override void OnCreate()
    {
        newMoveByGroup = GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] {typeof(MoveBy), ComponentType.ReadOnly(typeof(PositionMoveByData))},
            None = new ComponentType[] {typeof(MoveByState)},
            Any = Array.Empty<ComponentType>(),
        });

        aliveMoveByGroup = GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] {typeof(MoveBy), ComponentType.ReadOnly(typeof(PositionMoveByData)), typeof(MoveByState)},
            None = Array.Empty<ComponentType>(),
            Any = Array.Empty<ComponentType>(),
        });

        deadMoveByGroup = GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] {typeof(MoveByState)},
            None = new ComponentType[] {typeof(MoveBy), ComponentType.ReadOnly(typeof(PositionMoveByData))},
            Any = Array.Empty<ComponentType>(),
        });

        outputs = World.Active.GetOrCreateSystem<MoveByOutputBarrier>();

        sampleMoveByEntities = new NativeList<SampleInfo>(Allocator.Persistent);
        sampleHighFreeList = new NativeList<int>(Allocator.Persistent);
        sampleLowFreeList = new NativeList<int>(Allocator.Persistent);

        newMoveByEnum = new ChunkEntityEnumerable();
        deadMoveByEnum = new ChunkEntityEnumerable();
        aliveMoveByEnum = new ChunkEntityEnumerable();
    }

    ChunkEntityEnumerable newMoveByEnum;
    ChunkEntityEnumerable deadMoveByEnum;
    ChunkEntityEnumerable aliveMoveByEnum;

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        EntityCommandBuffer buffer = outputs.CreateCommandBuffer();

        newMoveByEnum.Setup(EntityManager, newMoveByGroup, Allocator.Persistent);
        deadMoveByEnum.Setup(EntityManager, deadMoveByGroup, Allocator.Persistent);
        aliveMoveByEnum.Setup(EntityManager, aliveMoveByGroup, Allocator.Persistent);

        SetupNewAndOldMoveBys maintenance = new SetupNewAndOldMoveBys
        {
            currSample = sampleMoveByEntities,
            deads = deadMoveByEnum,
            buffer = buffer,
            news = newMoveByEnum,
            random = new Unity.Mathematics.Random((uint)(2 + Time.time * 0xFFFF)),
            highFreeList = sampleHighFreeList,
            lowFreeList = sampleLowFreeList, 
            dt = Time.deltaTime,
            Params = myParameters,
            positionalFromEntity = GetComponentDataFromEntity<PositionMoveByData>(true)
        };

        ComponentDataFromEntity<SamplePlayback> playbackFromEntity = GetComponentDataFromEntity<SamplePlayback>(false);

        TrickCurrents update = new TrickCurrents
        {
            currSamples = sampleMoveByEntities,
            alive = aliveMoveByEnum,
            buffer = buffer,
            highFreeList = sampleHighFreeList,
            lowFreeList = sampleLowFreeList,
            dt = Time.deltaTime,
            samplePlaybackType = ComponentType.ReadWrite<SamplePlayback>(),
            playbackFromEntity = playbackFromEntity,
            stateFromEntity = GetComponentDataFromEntity<MoveByState>(false),
            positionalFromEntity = GetComponentDataFromEntity<PositionMoveByData>(true)
        };

        inputDeps = update.Schedule(inputDeps);
        inputDeps = maintenance.Schedule(inputDeps);
        outputs.AddJobHandleForProducer(inputDeps);

        return inputDeps;
    }

    struct SetupNewAndOldMoveBys : IJob
    {
        [ReadOnly] public ChunkEntityEnumerable news;
        [ReadOnly] public ChunkEntityEnumerable deads;

        [ReadOnly] public ComponentDataFromEntity<PositionMoveByData> positionalFromEntity;

        [ReadOnly] public NativeList<SampleInfo> currSample;

        public NativeList<int> highFreeList;
        public NativeList<int> lowFreeList;

        public EntityCommandBuffer buffer;

        public Unity.Mathematics.Random random;

        public float dt;

        public MoveByParameters Params;

        public void Execute()
        {
            foreach(Entity newMoveBy in news)
            {
                PositionMoveByData positional = positionalFromEntity[newMoveBy];

                float speed = positional.prevDist - positional.currDist;
                speed /= dt;

                if(speed < Params.MinSpeed)
                {
                    buffer.DestroyEntity(newMoveBy);
                    continue;
                }

                bool isFast = speed > Params.Fast;

                float intensity = (speed - Params.MinSpeed) / (Params.MaxSpeed - Params.MinSpeed);
                float pitch = 0.5f + intensity * 0.5f;
                float layerSpecificVolume = isFast ? Params.highLayer : Params.lowLayer;

                pitch *= (isFast ? Params.FastPitchMul : 1);

                NativeList<int> freeList = isFast ? highFreeList : lowFreeList;
                NativeList<int> otherList = !isFast ? highFreeList : lowFreeList;

                if(freeList.Length == 0)
                {
                    freeList = otherList;

                    if (freeList.Length == 0)
                        break;
                }

                int freeIndex = random.NextInt(freeList.Length - 1);

                int playerIndex = freeList[freeIndex];

                SampleInfo sample = currSample[playerIndex];

                float distanceAttenuation = Params.Volume * math.min(0.5f, 1 / math.sqrt(math.max(1, positional.currDist - 3)));

                buffer.AddComponent(newMoveBy, new MoveByState
                {
                    timeOut = sample.length / pitch,
                    sampleIndex = playerIndex
                });

                buffer.AddComponent(sample.playerEntity, new SamplePlayback
                {
                    Left = positional.left,
                    Right = positional.right,
                    Pitch = pitch,
                    Volume = layerSpecificVolume * math.min(1, math.sqrt(intensity)) * distanceAttenuation
                });

                freeList.RemoveAtSwapBack(freeIndex);

            }

            foreach(Entity deadFlyBy in deads)
            {
                buffer.RemoveComponent(deadFlyBy, ComponentType.ReadWrite<MoveByState>());
            }
        }

    }

    [BurstCompile]
    struct TrickCurrents : IJob
    {
        [ReadOnly] public ChunkEntityEnumerable alive;

        [ReadOnly] public ComponentDataFromEntity<PositionMoveByData> positionalFromEntity;

        [ReadOnly] public NativeList<SampleInfo> currSamples;

        public ComponentDataFromEntity<MoveByState> stateFromEntity;
        public ComponentDataFromEntity<SamplePlayback> playbackFromEntity;

        public NativeList<int> highFreeList;
        public NativeList<int> lowFreeList;

        public EntityCommandBuffer buffer;

        public float dt;

        public ComponentType samplePlaybackType;

        public void Execute()
        {
            foreach(Entity aliveMoveBy in alive)
            {
                MoveByState moveBy = stateFromEntity[aliveMoveBy];

                moveBy.timeOut -= dt;

                if(moveBy.timeOut > 0)
                {
                    Entity playerEntity = currSamples[moveBy.sampleIndex].playerEntity;

                    SamplePlayback playback = playbackFromEntity[playerEntity];
                    PositionMoveByData positional = positionalFromEntity[aliveMoveBy];
                    playback.Left = positional.left;
                    playback.Right = positional.right;
                    playbackFromEntity[playerEntity] = playback;
                }
                else
                {
                    buffer.DestroyEntity(aliveMoveBy);
                    buffer.RemoveComponent(currSamples[moveBy.sampleIndex].playerEntity, samplePlaybackType);

                    if (currSamples[moveBy.sampleIndex].intensity > 0)
                        highFreeList.Add(moveBy.sampleIndex);
                    else
                        lowFreeList.Add(moveBy.sampleIndex);
                }

                stateFromEntity[aliveMoveBy] = moveBy;
            }
        }
    }

}


