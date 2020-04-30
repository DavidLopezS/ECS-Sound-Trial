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
    EntityQuery m_EmitterQuery;
    EntityQuery m_SoundPlayerQuery;
    EntityQuery m_EmitterDefinitionQuery;
    EntityQuery m_FinalMixQuery;

    AudioManagerSystem m_AudioManager;

    NativeArray<ECSoundFieldContributor> m_SubMix;

    [BurstCompile]
    [RequireComponentTag(typeof(ECSoundEmitter))]
    struct CalculateMixFieldJob : IJobChunk
    {
#pragma warning disable 0649
        [NativeSetThreadIndex]
        int m_ThreadIndex;
#pragma warning restore 0649

        [ReadOnly]
        public NativeArray<Entity> m_DefinitionEntities;

        [ReadOnly]
        public NativeArray<Entity> m_SoundPlayerEntities;

        [NativeDisableParallelForRestriction]
        public NativeArray<ECSoundFieldContributor> m_SubMix;

        [ReadOnly]
        public ComponentDataFromEntity<ECSoundEmitterDefinition> m_DefinitionFromEntity;

        [ReadOnly]
        public ComponentDataFromEntity<ECSoundPlayer> m_SoundPlayerFromEntity;

        public ArchetypeChunkComponentType<ECSoundEmitter> m_EmitterChunkComponentType;

        public float4x4 m_ListenerWorldToLocal;
        public float4 m_ListenerPosition;
        public int m_NumFieldPlayers;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            int offset = m_ThreadIndex * m_NumFieldPlayers;

            NativeArray<ECSoundEmitter> emitterComponents = chunk.GetNativeArray<ECSoundEmitter>(m_EmitterChunkComponentType);
            for (int i = 0; i < emitterComponents.Length; i++)
            {
                ECSoundEmitter emitterComponent = emitterComponents[i];

                if (emitterComponent.definitionEntity == Entity.Null)
                {
                    int definitionIndex = emitterComponent.definitionIndex;

                    for (int n = 0; n < m_DefinitionEntities.Length; n++)
                    {
                        Entity e = m_DefinitionEntities[n];
                        ECSoundEmitterDefinition c = m_DefinitionFromEntity[e];
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

                ECSoundEmitterDefinition def = m_DefinitionFromEntity[emitterComponent.definitionEntity];

                float spatialRandom0 = emitterComponent.position.x * emitterComponent.position.y + emitterComponent.position.y * emitterComponent.position.z + emitterComponent.position.x * emitterComponent.position.z;
                float spatialRandom1 = 0.17f * spatialRandom0;
                spatialRandom1 = Mathf.Abs(spatialRandom1 - Mathf.Floor(spatialRandom1));

                if (spatialRandom1 > def.probability * 0.01f)
                    continue;

                float distanceFromCenter = math.length(emitterComponent.position - m_ListenerPosition.xyz);
                if (distanceFromCenter > 2.0f * def.maxDist)
                    continue;

                if (emitterComponent.soundPlayerEntity == Entity.Null)
                {
                    float spatialRandom2 = 0.31f * spatialRandom0;
                    spatialRandom2 = Mathf.Abs(spatialRandom2 - Mathf.Floor(spatialRandom2));

                    int soundPlayerIndex = (int)(def.soundPlayerIndexMin + spatialRandom2 * (def.soundPlayerIndexMax - def.soundPlayerIndexMin));

                    Entity soundPlayerEntity = Entity.Null;
                    for (int n = 0; n < m_SoundPlayerEntities.Length; n++)
                    {
                        Entity e = m_SoundPlayerEntities[n];
                        ECSoundPlayer p = m_SoundPlayerFromEntity[e];
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

                ECSoundPlayer player = m_SoundPlayerFromEntity[emitterComponent.soundPlayerEntity];
                int soundIndex = player.soundPlayerIndex;

                ECSoundFieldContributor mix = m_SubMix[offset + soundIndex];
                mix.data.CalculatePanningAdditive(
                    emitterComponent.position,
                    emitterComponent.coneDirection,
                    ref def,
                    ref m_ListenerWorldToLocal,
                    m_ListenerPosition,
                    distanceFromCenter);
                m_SubMix[offset + soundIndex] = mix;
            }
        }
    }

    [BurstCompile]
    struct MixIntegrateJob : IJob
    {
        public NativeArray<ECSoundFieldContributor> m_SubMix;
        [ReadOnly, DeallocateOnJobCompletion] public NativeArray<Entity> m_FinalMixEntities;
        public ComponentDataFromEntity<ECSoundFieldFinalMix> m_OutputMixFromEntity;

        public void Execute()
        {
            if (m_FinalMixEntities.Length == 0)
                return;

            for (int i = 0; i < m_FinalMixEntities.Length; ++i)
            {
                m_OutputMixFromEntity[m_FinalMixEntities[i]] = new ECSoundFieldFinalMix();

            }

            int n = 0;
            while (n < m_SubMix.Length)
            {
                for (int i = 0; i < m_FinalMixEntities.Length; ++i)
                {
                    ECSoundFieldFinalMix mix = m_OutputMixFromEntity[m_FinalMixEntities[i]];
                    mix.data.Add(m_SubMix[n].data);
                    m_OutputMixFromEntity[m_FinalMixEntities[i]] = mix;
                    m_SubMix[n] = new ECSoundFieldContributor();
                    ++n;
                }
            }
        }
    }

    JobHandle m_MixJobs;
    int m_NumFieldPlayers;

    NativeArray<Entity> m_DefinitionEntities;
    NativeArray<Entity> m_SoundPlayerEntities;



    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        if (!m_AudioManager.AudioEnabled)
            return inputDeps;

        // Replace these by simple GetComponentDataFromEntity

        Profiler.BeginSample("ECSoundFieldMixSystem -- Get definition entities");
        ChunkUtils.ReallocateEntitiesFromChunkArray(ref m_DefinitionEntities, EntityManager, m_EmitterDefinitionQuery, Allocator.Persistent);
        Profiler.EndSample();

        Profiler.BeginSample("ECSoundFieldMixSystem -- Get sound player entities");
        ChunkUtils.ReallocateEntitiesFromChunkArray(ref m_SoundPlayerEntities, EntityManager, m_SoundPlayerQuery, Allocator.Persistent);
        Profiler.EndSample();

        // Scheduling the jobs individually is very slow (0.2ms for 32 jobs), but there is no public API for scheduling them in batches.
        // It would probably be faster to do so, because (like IJobParallelFor) there would be less overhead in scheduling many jobs of the same type.
        Profiler.BeginSample("ECSoundFieldMixSystem -- Setup mix jobs");
        CalculateMixFieldJob mixJobs = new CalculateMixFieldJob
        {
            m_ListenerWorldToLocal = m_AudioManager.ListenerTrans.worldToLocalMatrix,
            m_ListenerPosition = new float4(m_AudioManager.ListenerTrans.position, 1),
            m_DefinitionEntities = m_DefinitionEntities,
            m_SoundPlayerEntities = m_SoundPlayerEntities,
            m_DefinitionFromEntity = GetComponentDataFromEntity<ECSoundEmitterDefinition>(true),
            m_SoundPlayerFromEntity = GetComponentDataFromEntity<ECSoundPlayer>(true),
            m_SubMix = m_SubMix,
            m_NumFieldPlayers = m_NumFieldPlayers,
            m_EmitterChunkComponentType = GetArchetypeChunkComponentType<ECSoundEmitter>()
        };
        m_MixJobs = mixJobs.Schedule(m_EmitterQuery, inputDeps);
        Profiler.EndSample();

        Profiler.BeginSample("ECSoundFieldMixSystem -- Schedule integrate job");

        NativeArray<Entity> finalMixEntities = m_FinalMixQuery.ToEntityArray(Allocator.TempJob, out JobHandle gatherMixEntities);

        MixIntegrateJob integrateJob = new MixIntegrateJob
        {
            m_SubMix = m_SubMix,
            m_FinalMixEntities = finalMixEntities,
            m_OutputMixFromEntity = GetComponentDataFromEntity<ECSoundFieldFinalMix>()

        };
        inputDeps = integrateJob.Schedule(JobHandle.CombineDependencies(gatherMixEntities, m_MixJobs));

        Profiler.EndSample();

        return inputDeps;
    }

    protected override void OnCreate()
    {
        m_AudioManager = World.GetOrCreateSystem<AudioManagerSystem>();

        m_EmitterQuery = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(ECSoundEmitter) },
                None = Array.Empty<ComponentType>(),
                Any = Array.Empty<ComponentType>(),
            });

        m_SoundPlayerQuery = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(ECSoundPlayer) },
                None = Array.Empty<ComponentType>(),
                Any = Array.Empty<ComponentType>(),
            });

        m_EmitterDefinitionQuery = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(ECSoundEmitterDefinition) },
                None = Array.Empty<ComponentType>(),
                Any = Array.Empty<ComponentType>(),
            });

        m_FinalMixQuery = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(ECSoundFieldFinalMix) },
                None = Array.Empty<ComponentType>(),
                Any = Array.Empty<ComponentType>(),
            });
    }

    public void AddFieldPlayers(int numFieldPlayers)
    {
        m_NumFieldPlayers = numFieldPlayers;

        if (m_SubMix.IsCreated)
        {
            m_SubMix.Dispose();
        }

        m_SubMix = new NativeArray<ECSoundFieldContributor>(JobsUtility.MaxJobThreadCount * m_NumFieldPlayers, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (m_SubMix.IsCreated)
        {
            m_SubMix.Dispose();
        }

        if (m_DefinitionEntities.IsCreated)
            m_DefinitionEntities.Dispose();

        if (m_SoundPlayerEntities.IsCreated)
            m_SoundPlayerEntities.Dispose();
    }

}
