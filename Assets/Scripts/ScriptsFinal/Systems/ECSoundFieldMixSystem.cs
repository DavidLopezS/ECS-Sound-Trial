using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Profiling;
using System;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;

[UpdateInGroup(typeof(AudioFrame))]
public class ECSoundFieldMixSystem : JobComponentSystem
{
    //EntityQuerys that will save the sound data
    EntityQuery my_EmitterQuery;
    EntityQuery my_SoundPlayerQuery;
    EntityQuery my_EmitterDefinitionQuery;
    EntityQuery my_FinalMixQuery;

    AudioManagerSystem my_AudioManager;//Audio Manager variable

    NativeArray<ECSoundFieldContributor> my_SubMix;//NativeArray of the Component System that stores the variables of the Panner

    [BurstCompile]
    [RequireComponentTag(typeof(ECSoundEmitter))]//Definition of the RequireComponentTag https://docs.unity3d.com/ScriptReference/RequireComponent.html
    struct CalculateMixFieldJob : IJobChunk
    {
#pragma warning disable 0649
        [NativeSetThreadIndex]
        int my_ThreadIndex;
#pragma warning restore 0649

        [ReadOnly]
        public NativeArray<Entity> my_DefinitionEntities;

        [ReadOnly]
        public NativeArray<Entity> my_SoundPlayerEntities;

        [NativeDisableParallelForRestriction]
        public NativeArray<ECSoundFieldContributor> my_SubMix;

        [ReadOnly]
        public ComponentDataFromEntity<ECSoundEmitterDefinition> my_DefinitionFromEntity;
        [ReadOnly]
        public ComponentDataFromEntity<ECSoundPlayer> my_SoundPlayerFromEntity;

        public ArchetypeChunkComponentType<ECSoundEmitter> my_EmitterChunkComponentType;

        public float4x4 my_ListenerWorldToLocal;
        public float4 my_ListenerPosition;
        public int my_NumFieldPlayers;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            int offset = my_ThreadIndex * my_NumFieldPlayers;

            NativeArray<ECSoundEmitter> emitterComponents = chunk.GetNativeArray<ECSoundEmitter>(my_EmitterChunkComponentType);
            for (int i = 0; i < emitterComponents.Length; i++)
            {
                ECSoundEmitter emitterComponent = emitterComponents[i];

                if (emitterComponent.definitionEntity == Entity.Null)
                {
                    int definitionIndex = emitterComponent.definitionIndex;

                    for (int n = 0; n < my_DefinitionEntities.Length; n++)
                    {
                        Entity e = my_DefinitionEntities[n];
                        ECSoundEmitterDefinition c = my_DefinitionFromEntity[e];
                        if (c.definitionIndex == definitionIndex)
                        {
                            emitterComponent.definitionEntity = e;
                            emitterComponents[i] = emitterComponent;
                            break;
                        }
                    }

                    if (emitterComponent.definitionEntity == Entity.Null)
                        continue;
                }

                ECSoundEmitterDefinition def = my_DefinitionFromEntity[emitterComponent.definitionEntity];

                float spatialRandom0 = emitterComponent.position.x * emitterComponent.position.y + emitterComponent.position.y * emitterComponent.position.z + emitterComponent.position.x * emitterComponent.position.z;
                float spatialRandom1 = 0.17f * spatialRandom0;
                spatialRandom1 = Mathf.Abs(spatialRandom1 - Mathf.Floor(spatialRandom1));

                if (spatialRandom1 > def.probability * 0.01f)
                    continue;

                float distanceFromCenter = math.length(emitterComponent.position - my_ListenerPosition.xyz);
                if (distanceFromCenter > 2.0f * def.maxDist)
                    continue;

                if (emitterComponent.soundPlayerEntity == Entity.Null)
                {
                    float spatialRandom2 = 0.31f * spatialRandom0;
                    spatialRandom2 = Mathf.Abs(spatialRandom2 - Mathf.Floor(spatialRandom2));

                    int soundPlayerIndex = (int)(def.soundPlayerIndexMin + spatialRandom2 * (def.soundPlayerIndexMax - def.soundPlayerIndexMin));

                    Entity soundPlayerEntity = Entity.Null;
                    for (int n = 0; n < my_SoundPlayerEntities.Length; n++)
                    {
                        Entity e = my_SoundPlayerEntities[n];
                        ECSoundPlayer p = my_SoundPlayerFromEntity[e];
                        if (p.soundPlayerIndex == soundPlayerIndex)
                        {
                            emitterComponent.soundPlayerEntity = e;
                            emitterComponents[i] = emitterComponent;
                            break;
                        }
                    }

                    if (emitterComponent.soundPlayerEntity == Entity.Null)
                        continue;
                }

                ECSoundPlayer player = my_SoundPlayerFromEntity[emitterComponent.soundPlayerEntity];
                int soundIndex = player.soundPlayerIndex;

                ECSoundFieldContributor mix = my_SubMix[offset + soundIndex];
                mix.data.CalculatePanningAdditive(
                    emitterComponent.position,
                    emitterComponent.coneDirection,
                    ref def,
                    ref my_ListenerWorldToLocal,
                    my_ListenerPosition,
                    distanceFromCenter);
                my_SubMix[offset + soundIndex] = mix;
            }
        }
    }

    [BurstCompile]
    struct MixIntegrateJob : IJob
    {
        public NativeArray<ECSoundFieldContributor> my_SubMix;
        [ReadOnly, DeallocateOnJobCompletion] public NativeArray<Entity> my_FinalMixEntities;
        public ComponentDataFromEntity<ECSoundFieldFinalMix> my_OutputMixFromEntity;

        public void Execute()
        {
            if (my_FinalMixEntities.Length == 0)
                return;

            for (int i = 0; i < my_FinalMixEntities.Length; ++i)
            {
                my_OutputMixFromEntity[my_FinalMixEntities[i]] = new ECSoundFieldFinalMix();

            }

            int n = 0;
            while (n < my_SubMix.Length)
            {
                for (int i = 0; i < my_FinalMixEntities.Length; ++i)
                {
                    ECSoundFieldFinalMix mix = my_OutputMixFromEntity[my_FinalMixEntities[i]];
                    mix.data.Add(my_SubMix[n].data);
                    my_OutputMixFromEntity[my_FinalMixEntities[i]] = mix;
                    my_SubMix[n] = new ECSoundFieldContributor();
                    ++n;
                }
            }
        }
    }

    JobHandle my_MixJobs;
    int my_NumFieldPlayers;

    NativeArray<Entity> my_DefinitionEntities;
    NativeArray<Entity> my_SoundPlayerEntities;



    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        if (!my_AudioManager.AudioEnabled)
            return inputDeps;

        // Replace these by simple GetComponentDataFromEntity

        Profiler.BeginSample("ECSoundFieldMixSystem -- Get definition entities");
        ChunkUtils.ReallocateEntitiesFromChunkArray(ref my_DefinitionEntities, EntityManager, my_EmitterDefinitionQuery, Allocator.Persistent);
        Profiler.EndSample();

        Profiler.BeginSample("ECSoundFieldMixSystem -- Get sound player entities");
        ChunkUtils.ReallocateEntitiesFromChunkArray(ref my_SoundPlayerEntities, EntityManager, my_SoundPlayerQuery, Allocator.Persistent);
        Profiler.EndSample();

        // Scheduling the jobs individually is very slow (0.2ms for 32 jobs), but there is no public API for scheduling them in batches.
        // It would probably be faster to do so, because (like IJobParallelFor) there would be less overhead in scheduling many jobs of the same type.
        Profiler.BeginSample("ECSoundFieldMixSystem -- Setup mix jobs");
        CalculateMixFieldJob mixJobs = new CalculateMixFieldJob
        {
            my_ListenerWorldToLocal = my_AudioManager.ListenerTrans.worldToLocalMatrix,
            my_ListenerPosition = new float4(my_AudioManager.ListenerTrans.position, 1),
            my_DefinitionEntities = my_DefinitionEntities,
            my_SoundPlayerEntities = my_SoundPlayerEntities,
            my_DefinitionFromEntity = GetComponentDataFromEntity<ECSoundEmitterDefinition>(true),
            my_SoundPlayerFromEntity = GetComponentDataFromEntity<ECSoundPlayer>(true),
            my_SubMix = my_SubMix,
            my_NumFieldPlayers = my_NumFieldPlayers,
            my_EmitterChunkComponentType = GetArchetypeChunkComponentType<ECSoundEmitter>()
        };
        my_MixJobs = mixJobs.Schedule(my_EmitterQuery, inputDeps);
        Profiler.EndSample();

        Profiler.BeginSample("ECSoundFieldMixSystem -- Schedule integrate job");

        NativeArray<Entity> finalMixEntities = my_FinalMixQuery.ToEntityArray(Allocator.TempJob, out JobHandle gatherMixEntities);

        MixIntegrateJob integrateJob = new MixIntegrateJob
        {
            my_SubMix = my_SubMix,
            my_FinalMixEntities = finalMixEntities,
            my_OutputMixFromEntity = GetComponentDataFromEntity<ECSoundFieldFinalMix>()

        };
        inputDeps = integrateJob.Schedule(JobHandle.CombineDependencies(gatherMixEntities, my_MixJobs));

        Profiler.EndSample();

        return inputDeps;
    }

    protected override void OnCreate()
    {
        my_AudioManager = World.GetOrCreateSystem<AudioManagerSystem>();

        my_EmitterQuery = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(ECSoundEmitter) },
                None = Array.Empty<ComponentType>(),
                Any = Array.Empty<ComponentType>(),
            });

        my_SoundPlayerQuery = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(ECSoundPlayer) },
                None = Array.Empty<ComponentType>(),
                Any = Array.Empty<ComponentType>(),
            });

        my_EmitterDefinitionQuery = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(ECSoundEmitterDefinition) },
                None = Array.Empty<ComponentType>(),
                Any = Array.Empty<ComponentType>(),
            });

        my_FinalMixQuery = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(ECSoundFieldFinalMix) },
                None = Array.Empty<ComponentType>(),
                Any = Array.Empty<ComponentType>(),
            });
    }

    public void AddFieldPlayers(int numFieldPlayers)
    {
        my_NumFieldPlayers = numFieldPlayers;

        if (my_SubMix.IsCreated)
        {
            my_SubMix.Dispose();
        }

        my_SubMix = new NativeArray<ECSoundFieldContributor>(JobsUtility.MaxJobThreadCount * my_NumFieldPlayers, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (my_SubMix.IsCreated)
        {
            my_SubMix.Dispose();
        }

        if (my_DefinitionEntities.IsCreated)
            my_DefinitionEntities.Dispose();

        if (my_SoundPlayerEntities.IsCreated)
            my_SoundPlayerEntities.Dispose();
    }

}
