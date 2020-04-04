using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Entities;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Audio;
using Unity.Jobs.LowLevel.Unsafe;
using Random = Unity.Mathematics.Random;

//Struct containing the audio parameters, all of them ranged
[Serializable]
public struct AudioParameters
{
    [Range(0, 0.9f)]
    public float moveByPole;

    [Range(0.5f, 4f)]
    public float fallOfCurve;

    [Range(0, 3)]
    public float Volume;
}


public class AudioBarrier : EntityCommandBufferSystem { }

//Main JobComponentSystem that will handle all the audio data
public class AudioHandlerSystem : JobComponentSystem
{

    struct AdditiveState : IComponentData { }

    //Component data that saves the entity that corresponds to the target
    struct MoveByState : IComponentData
    {
        public Entity target;
    }

    /*Sound emitter parameters, it is created as a ISystemStateComponentData, which is 
     * an interface for a component type that stores system-specific data.*/
    struct AgentEmitter : ISystemStateComponentData
    {
        public float Angle;
        public float Left, Right;
        public float Volume;
        public Entity Sample;
    }

    //Component data that saves the position parameters of the entity that emitts the sound
    struct EntityPosition : IComponentData
    {
        public float prevDist;
        public float currDist;
        public float left;
        public float right;
    }

    //AudioBarrier variable
    AudioBarrier myAudioBarrier;

    //MovementInputBarrier variable, it is an EntityCommandBufferSystem and is created on the AgentAudioSystem
    MoveByInputBarrier moveByInputBarrier;

    //Groups of samples using the type EntityQuery, EntityQuery usage: https://docs.unity3d.com/Packages/com.unity.entities@0.0/manual/component_group.html
    EntityQuery newCubesGroup;//This group stores the new Cubes
    EntityQuery aliveCubesGroup;//This group stores the alive Cubes
    EntityQuery samplesGroup;//This group stores the samples
    EntityQuery deadCubesGroup;//This group stores the dead Cubes
    EntityQuery agentsGroup;//This group stores the Agents

    //The ECS framework uses archetypes to group entities that have the same structure togethe
    EntityArchetype moveByArchetype;

    //AudioManagerSystem variable, AudioManagerSystem is a JobComponentSystem created in the script that has the same name
    AudioManagerSystem audioManager;
    //AgentAudioSystem variable, AgentAudioSystem is a JobComponentSystem created in the script that has the same name
    AgentAudioSystem agentAudioSystem;

    //Transform of the listener
    Transform listenerTransform;

    //Native list that saves the sample entities
    NativeList<Entity> sampleEntities;

    NativeQueue<Entity> moveByCandidates;//NativeQueue that saves the moving candidates
    NativeList<Entity> moveByyEntityBuffer;//NativeList that enacts as a container of the movement entities
    NativeArray<Random> myRandoms;//NativeList that stores the randoms

    SoundCollection mySoundCollection;//SoundCollection type variable, this variable stores the clip IDs

    AudioParameters audioParameters;//Variable that will be used to work with the audio parameters

    //BurstCompile attribute
    [BurstCompile]
    struct CalculateMixContributions : IJobChunk //IJobChunk job that will calculate the position where the sound will come
    {
        [WriteOnly] public  NativeQueue<Entity>.ParallelWriter candidates;

        public float3 listenerPosition;//Float3 that will enact as listener position
        public float4x4 listenerWorldToLocal;//Float4x4 that will be used to transform the position from the world to the local
        public float Falloff;//Float that will determine the falloff of the sound

        public float DtPole;//Variable that saves the movement pole with the delta time
        
        [ReadOnly] public ArchetypeChunkEntityType entityType;//Chunk entity archytpe variable, used to save the entitytype
        [ReadOnly] public ArchetypeChunkComponentType<Translation> positionType;/*Chunk component archytpe variable, used to save the position 
                                                                                of the entity*/
        public ArchetypeChunkComponentType<AgentEmitter> emitterType;/*Chunk component archytpe variable, used to save the position 
                                                                                of the emitter*/
        public ArchetypeChunkComponentType<PositionMoveByData> entityMovementPositionType;/*Chunk component archytpe variable, used to save the position 
                                                                                            of the movement type*/

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            NativeArray<Entity> entities = chunk.GetNativeArray(entityType);//Adding the ArchetypeChunkEntityType of entityType to a NativeArray
            NativeArray<Translation> positions = chunk.GetNativeArray(positionType);/*Adding the ArchetypeChunkComponentType of the positions to a 
                                                                                       NativeArray*/
            NativeArray<AgentEmitter> emitters = chunk.GetNativeArray(emitterType);/*Adding the ArchetypeChunkComponentType of the emitters to a 
                                                                                       NativeArray*/
            NativeArray<PositionMoveByData> moveByPos = chunk.GetNativeArray(entityMovementPositionType);/*Adding the ArchetypeChunkComponentType 
            of the positions of the movement type to NativeArray*/

            //In this iterator the calculus to determine the sound position are made
            for (int i = 0; i < chunk.Count; ++i)
            {
                AgentEmitter emitter = emitters[i];

                float3 aroundOrigin = positions[i].Value - listenerPosition;
                float3 rotated = math.transform(listenerWorldToLocal, positions[i].Value);

                emitter.Angle = (float)math.PI - math.atan2(rotated.z, rotated.x);

                float cosine = math.cos(emitter.Angle);

                emitter.Left = 0.5f + 0.5f * cosine;
                emitter.Right = 0.5f + 0.5f * (1 - cosine);

                float distance = math.length(aroundOrigin);

                float newVolume = 1.0f / (0.4f + math.pow(distance - 3, Falloff));
                newVolume = math.select(newVolume, Falloff, distance < 3.0f) / Falloff;
                emitter.Volume = math.select(emitter.Volume * DtPole, newVolume, newVolume > emitter.Volume);

                PositionMoveByData positional = moveByPos[i];

                positional.left = emitter.Left;
                positional.right = emitter.Right;
                positional.prevDist = positional.currDist;
                positional.currDist = distance;

                moveByPos[i] = positional;

                emitters[i] = emitter;

                if (distance < 15f)
                {
                    candidates.Enqueue(entities[i]);
                }

            }
        }
    }

    //IComparer, used to compare the distance between two entities
    struct DistanceComparitor : IComparer<Entity>
    {
        public ComponentDataFromEntity<PositionMoveByData> emitterFromEntity;

        public int Compare(Entity x, Entity y)
        {
            PositionMoveByData a = emitterFromEntity[x];
            PositionMoveByData b = emitterFromEntity[y];

            return (int)(a.currDist - b.currDist);
        }
    }

    //This job will select the moving sound emitters
    struct SelectMoveByEmitters : IJob
    {
        [ReadOnly] public ComponentDataFromEntity<PositionMoveByData> emitterFromEntity;/*ComponentDataFromEntity container that saves the distance 
                                                                                            between the emitter and the entity*/ 

        public NativeQueue<Entity> moveByCandidates;//NativeQueue that saves the moving candidates
        public NativeList<Entity> outputMoveBys;//NativeList that saves the outputs of the moving entities

        [ReadOnly] public NativeArray<Entity> activeMoveByEntities;//NativeArray that saves the active moving entities

        public ComponentDataFromEntity<MoveByState> agentMoveByEntities;//ComponentDataFromEntity container that saves the moving entities

        public int maxMoveBys;//Max entities moving 

        public void Execute()
        {
            int currCandidateCapacity = maxMoveBys - activeMoveByEntities.Length;//Variable that saves the capacity of candidates stored

            if(currCandidateCapacity == 0)
            {
                moveByCandidates.Clear();
                return;
            }

            int candidates = moveByCandidates.Count;//Number of candidates

            NativeArray<Entity> sortedCandidates = new NativeArray<Entity>(candidates, Allocator.Temp);//Native array that stores the number of candidates

            for(int i = 0; i < candidates; ++i)
            {
                sortedCandidates[i] = moveByCandidates.Dequeue();
            }

            //Distance comparitor variable
            DistanceComparitor comparitor = new DistanceComparitor { emitterFromEntity = emitterFromEntity };

            //Sorting the Distancecomparitor variables into the NativeArray of the sortedCandidates
            sortedCandidates.Sort(comparitor);

            int maxEmittersToConsider = math.min(sortedCandidates.Length, currCandidateCapacity);//Max number of emitters

            /*This iteration enacts in order to add the emitters to the output by checking if they do or do not exist*/
            for(int i = 0, c = 0; i < sortedCandidates.Length; ++i)
            {
                if (c >= maxEmittersToConsider)
                    break;

                Entity emitter = sortedCandidates[i];

                bool alreadyExists = false;

                for(int e = 0; e < activeMoveByEntities.Length; ++e)
                {
                    if(emitter == activeMoveByEntities[e])
                    {
                        alreadyExists = true;
                        break;
                    }
                }

                if (alreadyExists)
                    continue;

                c++;
                outputMoveBys.Add(emitter);
            }

            moveByCandidates.Clear();
            sortedCandidates.Dispose();

        }
    }

    /*Job tkhat updates the panning of the moving agents, panning definition:swing (a video or movie camera) in a horizontal or vertical plane, 
     * typically to give a panoramic effect or follow a subject.*/
    [BurstCompile]
    struct UpdatePanningForMoveBys : IJob
    {
        public ComponentDataFromEntity<PositionMoveByData> positionalFromEntity;

        [ReadOnly, DeallocateOnJobCompletion] public NativeArray<Entity> activeMoveBys;

        public ComponentDataFromEntity<MoveByState> moveByStateFromEntity;

        public void Execute()
        {
            for(int i = 0; i < activeMoveBys.Length; ++i)
            {
                Entity moveBySelf = activeMoveBys[i];
                Entity emitter = moveByStateFromEntity[moveBySelf].target;

                if(positionalFromEntity.Exists(emitter) && positionalFromEntity.Exists(moveBySelf))
                {
                    PositionMoveByData positional = positionalFromEntity[emitter];
                    positionalFromEntity[moveBySelf] = positional;
                }
            }
        }
    }

    //Job that setsup the new agents 
    struct SetupNewMoveBys : IJob
    {
        public NativeList<Entity> inputMoveBysToCreate;
        public ComponentDataFromEntity<PositionMoveByData> positionalFromEntity;

        public EntityCommandBuffer buffer;
        public SoundCollection soundGroup;
        public EntityArchetype moveByArchetype;

        public void Execute()
        {
            for(int i = 0; i < inputMoveBysToCreate.Length; ++i)
            {
                Entity entity = buffer.CreateEntity(moveByArchetype);
                buffer.SetComponent(entity, new MoveBy { group = soundGroup });
                buffer.SetComponent(entity, new MoveByState { target = inputMoveBysToCreate[i] });
                buffer.SetComponent(entity, positionalFromEntity[inputMoveBysToCreate[i]]);
            }
            inputMoveBysToCreate.Clear();
        }
    }

    //Sets the audio parameters to the playback system
    [BurstCompile]
    struct FoldEmitterFieldsToSamples : IJobForEachWithEntity<AgentEmitter>
    {

        public ComponentDataFromEntity<SamplePlayback> samplePlaybackFromAliveEntities;
        public EntityCommandBuffer buffer;
        public ComponentType emitterType;
        public ComponentType positionalType;

        public float generalVolume;

        public void Execute(Entity entity, int index, [ReadOnly] ref AgentEmitter agentEmitter)
        {
            if (samplePlaybackFromAliveEntities.Exists(agentEmitter.Sample))
            {
                SamplePlayback playback = samplePlaybackFromAliveEntities[agentEmitter.Sample];
                playback.Left += agentEmitter.Left;
                playback.Right += agentEmitter.Right;
                playback.Volume = generalVolume;
                samplePlaybackFromAliveEntities[agentEmitter.Sample] = playback;
            }
            else
            {
                buffer.RemoveComponent(entity, emitterType);
                buffer.RemoveComponent(entity, positionalType);
            }
        }
    }

    //Clears the playback params
    [BurstCompile]
    struct ClearPlayBackAdditiveStates : IJobForEach<SamplePlayback>
    {
        public void Execute(ref SamplePlayback playback)
        {
            playback.Left = 0;
            playback.Right = 0;
            playback.Volume = 0;
            playback.Pitch = 1;
        }
    }

    //Attaches the sound to the cubes
    struct AttachToNewCubes : IJobChunk
    {
        [ReadOnly] public NativeArray<Entity> samplePlaybackEntities;

        [NativeSetThreadIndex]
        int nativeSetThreadIndex;

        [NativeDisableContainerSafetyRestriction]
        public NativeArray<Random> randoms;

        public EntityCommandBuffer.Concurrent concurrentBuffer;

        [ReadOnly] public ArchetypeChunkEntityType entityType;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            if (samplePlaybackEntities.Length == 0)
                return;

            NativeArray<Entity> entities = chunk.GetNativeArray(entityType);
            Random random = randoms[nativeSetThreadIndex];

            for(int i = 0; i < chunk.Count; ++i)
            {
                AgentEmitter newAgentEmitter = new AgentEmitter
                {
                    Sample = samplePlaybackEntities[random.NextInt(0, samplePlaybackEntities.Length)]
                };

                concurrentBuffer.AddComponent(chunkIndex, entities[i], newAgentEmitter);
                concurrentBuffer.AddComponent(chunkIndex, entities[i], new PositionMoveByData());

            }

            randoms[nativeSetThreadIndex] = random;

        }

    }

    //Detaches the sound from cubes
    [BurstCompile]
    struct DetachDeadCubes : IJobForEachWithEntity<AgentEmitter>
    {
        public EntityCommandBuffer buffer;
        public ComponentType emitterType;

        public void Execute(Entity entity, int index, [ReadOnly] ref AgentEmitter agentEmitter)
        {
            buffer.RemoveComponent(entity, emitterType);
        }
    }

    //Adds the audioclip to the playback system
    void AddClip(AudioClip clip)
    {
        SamplePlaybackSystem playbackSystem = World.Active.GetOrCreateSystem<SamplePlaybackSystem>();
        playbackSystem.AddClip(clip);
    }

    //Function that adds the clip to the sampleplayback, and the sample to the nativearray of entities by using the entitymanager
    public void AddDistributedSamplePlayback(AudioClip clip)
    {
        Entity sample = EntityManager.CreateEntity();
        AddClip(clip);
        EntityManager.AddComponentData(sample, new AdditiveState());
        EntityManager.AddComponentData(sample, new SamplePlayback { Volume = 1, Loop = 1, Pitch = 1 });
        EntityManager.AddComponentData(sample, new SharedAudioClip { ClipInstanceID = clip.GetInstanceID() });
        sampleEntities.Add(sample);
    }

    //Funciton that clears the sampleentities if needed
    public void ClearSamplePlaybacks()
    {
        for (int i = 0; i < sampleEntities.Length; ++i)
            EntityManager.DestroyEntity(sampleEntities[i]);

        sampleEntities.Clear();
    }

    //Removes the sound components from the agents
    public void DetachFromAllAgents()
    {
        using (NativeArray<Entity> groupEntitites = aliveCubesGroup.ToEntityArray(Allocator.TempJob))
        {
            for(int i = 0; i < groupEntitites.Length; ++i)
            {
                EntityManager.RemoveComponent<AgentEmitter>(groupEntitites[i]);
                EntityManager.RemoveComponent<PositionMoveByData>(groupEntitites[i]);
            }
        }

        using (NativeArray<Entity> moveBys = agentsGroup.ToEntityArray(Allocator.TempJob))
        {
            for (int i = 0; i < moveBys.Length; ++i)
            {
                EntityManager.DestroyEntity(moveBys[i]);
            }
        }
    }

    //Function that sets the parametes in case they are updated
    public void SetParameters(AudioParameters parameters)
    {
        audioParameters = parameters;
    }

    //Attach the sounds to the groups
    public void SetMoveBySoundGroup(SoundCollection group)
    {
        mySoundCollection = group;
    }

    protected override void OnDestroy()
    {
        moveByyEntityBuffer.Dispose();
        sampleEntities.Dispose();
        moveByCandidates.Dispose();
        myRandoms.Dispose();
    }

    protected override void OnCreate()
    {
        myAudioBarrier = World.GetOrCreateSystem<AudioBarrier>();
        moveByInputBarrier = World.GetOrCreateSystem<MoveByInputBarrier>();
        audioManager = World.GetOrCreateSystem<AudioManagerSystem>();
        agentAudioSystem = World.GetOrCreateSystem<AgentAudioSystem>();

        moveByArchetype = EntityManager.CreateArchetype(typeof(MoveBy), typeof(MoveByState), typeof(PositionMoveByData));

        sampleEntities = new NativeList<Entity>(Allocator.Persistent);
        moveByCandidates = new NativeQueue<Entity>(Allocator.Persistent);
        moveByyEntityBuffer = new NativeList<Entity>(Allocator.Persistent);
        myRandoms = new NativeArray<Random>(JobsUtility.MaxJobThreadCount, Allocator.Persistent);

        Random seedRandom = new Random((uint)(2 + UnityEngine.Time.time * 0xFFFF));

        for (int i = 0; i < myRandoms.Length; ++i)
            myRandoms[i] = new Random(seedRandom.NextUInt());

        newCubesGroup = GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] {typeof(Translation), typeof(Rotation)},
            None = new ComponentType[] {typeof(AgentEmitter), typeof(PositionMoveByData)},
            Any = Array.Empty<ComponentType>(),
        });

        aliveCubesGroup = GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] {typeof(Translation), typeof(Rotation), typeof(AgentEmitter), typeof(PositionMoveByData)},
            None = Array.Empty<ComponentType>(),
            Any = Array.Empty<ComponentType>(),
        });

        samplesGroup = GetEntityQuery(typeof(SamplePlayback), typeof(AdditiveState));

        deadCubesGroup = GetEntityQuery(typeof(AgentEmitter));

        agentsGroup = GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] {typeof(MoveBy), typeof(MoveByState)},
            None = Array.Empty<ComponentType>(),
            Any = Array.Empty<ComponentType>(),
        });

    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {

        if (!audioManager.AudioEnabled || sampleEntities.Length == 0)
            return inputDeps;

        DetachDeadCubes deadSetup = new DetachDeadCubes
        {
            buffer = moveByInputBarrier.CreateCommandBuffer(),
            emitterType = ComponentType.ReadWrite<AgentEmitter>()
        };

        AttachToNewCubes newSetup = new AttachToNewCubes
        {
            entityType = GetArchetypeChunkEntityType(),
            samplePlaybackEntities = sampleEntities,
            concurrentBuffer = moveByInputBarrier.CreateCommandBuffer().ToConcurrent(),
            randoms = myRandoms,
        };

        inputDeps = JobHandle.CombineDependencies(deadSetup.ScheduleSingle(deadCubesGroup, inputDeps), newSetup.Schedule(newCubesGroup, inputDeps));

        CalculateMixContributions mixContributions = new CalculateMixContributions
        {
            entityType = GetArchetypeChunkEntityType(),
            listenerPosition = listenerTransform.position,
            listenerWorldToLocal = listenerTransform.worldToLocalMatrix,
            Falloff = audioParameters.fallOfCurve,
            DtPole = Mathf.Pow(audioParameters.moveByPole, Time.deltaTime),
            candidates = moveByCandidates.AsParallelWriter(),
            emitterType = GetArchetypeChunkComponentType<AgentEmitter>(),
            entityMovementPositionType = GetArchetypeChunkComponentType<PositionMoveByData>(),
            positionType = GetArchetypeChunkComponentType<Translation>()
            
        };

        NativeArray<Entity> moveByEntities = agentsGroup.ToEntityArray(Allocator.TempJob, out JobHandle moveByEntitiesJobHandle);

        ComponentDataFromEntity<MoveByState> moveByStateFromEntity = GetComponentDataFromEntity<MoveByState>();
        ComponentDataFromEntity<PositionMoveByData> positionalFromEntity = GetComponentDataFromEntity<PositionMoveByData>();

        SelectMoveByEmitters moveBySelection = new SelectMoveByEmitters
        {
            moveByCandidates = moveByCandidates,
            emitterFromEntity = positionalFromEntity,
            outputMoveBys = moveByyEntityBuffer,
            activeMoveByEntities = moveByEntities,
            agentMoveByEntities = moveByStateFromEntity,
            maxMoveBys = agentAudioSystem.maxVoices
        };

        SetupNewMoveBys moveBysCreation = new SetupNewMoveBys
        {
            inputMoveBysToCreate = moveByyEntityBuffer,
            buffer = moveByInputBarrier.CreateCommandBuffer(),
            soundGroup = mySoundCollection,
            positionalFromEntity = positionalFromEntity,
            moveByArchetype = moveByArchetype
        };

        UpdatePanningForMoveBys moveBysPanning = new UpdatePanningForMoveBys
        {
            activeMoveBys = moveByEntities,
            moveByStateFromEntity = moveByStateFromEntity,
            positionalFromEntity = positionalFromEntity
        };

        ComponentDataFromEntity<SamplePlayback> samplePlaybackFromEntity = GetComponentDataFromEntity<SamplePlayback>();

        FoldEmitterFieldsToSamples foldingMix = new FoldEmitterFieldsToSamples
        {
            samplePlaybackFromAliveEntities = samplePlaybackFromEntity,
            buffer = myAudioBarrier.CreateCommandBuffer(),
            emitterType = ComponentType.ReadWrite<AgentEmitter>(),
            positionalType = ComponentType.ReadWrite<PositionMoveByData>(),
            generalVolume = audioParameters.Volume
        };

        JobHandle mixDeps = mixContributions.Schedule(aliveCubesGroup, inputDeps);
        JobHandle moveByCalcDeps = moveBySelection.Schedule(JobHandle.CombineDependencies(mixDeps, moveByEntitiesJobHandle));
        JobHandle moveByDeps = moveBysCreation.Schedule(moveByCalcDeps);

        moveByInputBarrier.AddJobHandleForProducer(moveByDeps);

        moveByDeps = moveBysPanning.Schedule(moveByDeps);

        inputDeps = new ClearPlayBackAdditiveStates().Schedule(samplesGroup, mixDeps);

        inputDeps = foldingMix.ScheduleSingle(this, inputDeps);

        myAudioBarrier.AddJobHandleForProducer(inputDeps);

        return JobHandle.CombineDependencies(inputDeps, moveByDeps);


    }

}

