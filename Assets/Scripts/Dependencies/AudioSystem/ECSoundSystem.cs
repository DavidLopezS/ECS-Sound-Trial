using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Audio;
using UnityEngine.Profiling;
using Unity.Burst;
using System;

[BurstCompile]
[UpdateInGroup(typeof(AudioFrame)), UpdateAfter(typeof(ECSoundFieldMixSystem))]
public class ECSoundSystem : ComponentSystem
{
    EntityQuery soundPlayerEntityQuery;

    AudioManagerSystem audioManager;

    NativeArray<Entity> soundFieldPlayerEntities;

    ECSoundPlayerNode[] soundPlayerNodes;
    StateVariableFilter[] playerLPF;

    public void AddFieldPlayers(DSPCommandBlock block, AudioClip[] allClips)
    {
        soundPlayerNodes = new ECSoundPlayerNode[allClips.Length];
        playerLPF = new StateVariableFilter[allClips.Length];

        NativeArray<Entity> soundPlayerEntities = new NativeArray<Entity>(allClips.Length, Allocator.Persistent);

        DSPNode rootNode = audioManager.masterChannel;

        for(int n = 0; n < allClips.Length; n++)
        {
            Entity entity = EntityManager.CreateEntity();
            ECSoundPlayer player = new ECSoundPlayer();
            EntityManager.AddComponentData(entity, player);
            EntityManager.AddComponentData(entity, new ECSoundFieldFinalMix());

            soundPlayerNodes[n] = ECSoundPlayerNode.Create(block, allClips[n]);
            player.source = soundPlayerNodes[n].mainDSPNode;

            playerLPF[n] = StateVariableFilter.Create(block);
            player.LPF = playerLPF[n].node;
            block.AddInletPort(player.LPF, 2, SoundFormat.Stereo);
            block.AddOutletPort(player.LPF, 2, SoundFormat.Stereo);

            player.soundPlayerIndex = n;

            player.directMixConnection = block.Connect(player.source, 0, rootNode, 0);
            DSPConnection lpfConnection = block.Connect(player.source, 0, player.LPF, 0);
            player.directLPFConnection = block.Connect(player.LPF, 0, rootNode, 0);

            // A lot of sources start at full level (1.0), resulting in a mix that clips. So
            // explicitly ease in from 0.0, since the default connection attenuation is 1.0.
            block.SetAttenuation(player.directMixConnection, 0.0F);
            block.SetAttenuation(player.directLPFConnection, 0.0F);

            EntityManager.SetComponentData(entity, player);

            soundPlayerEntities[n] = entity;
        }

        soundFieldPlayerEntities = soundPlayerEntities;

    }

    public void CleanupFieldPlayers(DSPCommandBlock block, AudioClip[] allClips)
    {
        try
        {
            // Make sure no DSP graphs are running at this point that consume data from this array.
            ComponentDataFromEntity<ECSoundPlayer> playerFromEntity = GetComponentDataFromEntity<ECSoundPlayer>(true);
            for (int n = soundFieldPlayerEntities.Length - 1; n >= 0; n--)
            {
                Entity entity = soundFieldPlayerEntities[n];
                ECSoundPlayer player = playerFromEntity[entity];
                playerLPF[n].Dispose(block);
                soundPlayerNodes[n].Dispose(block);
            }

            for (int i = 0; i < soundFieldPlayerEntities.Length; ++i)
            {
                EntityManager.DestroyEntity(soundFieldPlayerEntities[i]);
            }

            Entities.ForEach ((Entity e, ref ECSoundEmitter c) =>
             {
                 c.soundPlayerEntity = Entity.Null;
             }
            );
        }
        finally
        {
            if (soundFieldPlayerEntities.IsCreated)
                soundFieldPlayerEntities.Dispose();

            soundPlayerNodes = null;
            playerLPF = null;
        }
    }

    protected override void OnUpdate()
    {
        if (!audioManager.AudioEnabled)
            return;

        DSPCommandBlock block = audioManager.FrameBlock;

        Profiler.BeginSample("ECSoundSystem Apply Mix -- Chunk iteration");
        ComponentDataFromEntity<ECSoundFieldFinalMix> finalMixFromEntity = GetComponentDataFromEntity<ECSoundFieldFinalMix>(true); // This has to happen here and doesn't work when used as a member variable initialized in OnCreateManager.
        ComponentDataFromEntity<ECSoundPlayer> playerFromEntity = GetComponentDataFromEntity<ECSoundPlayer>(true);
        using (ChunkEntityEnumerable entityIterable = new ChunkEntityEnumerable(EntityManager, soundPlayerEntityQuery, Allocator.TempJob))
        {
            foreach (Entity entity in entityIterable)
            {
                ECSoundFieldFinalMix mix = finalMixFromEntity[entity];
                ECSoundPlayer player = playerFromEntity[entity];
                Profiler.BeginSample("ECSoundSystem Apply Mix -- DSP Apply");
                ApplyConnectionAttenuation(block, ref player, ref mix, 1024);
                Profiler.EndSample();
            }
        }
        Profiler.EndSample();
    }


    protected override void OnCreate()
    {
        audioManager = World.GetOrCreateSystem<AudioManagerSystem>();

        soundPlayerEntityQuery = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(ECSoundFieldFinalMix) },
                None = Array.Empty<ComponentType>(),
                Any = Array.Empty<ComponentType>(),
            });
    }

    protected override void OnDestroy()
    {
        DSPCommandBlock block = audioManager.audioGraph.CreateCommandBlock();
        AudioClip[] audioClipDestory = null;
        try
        {
            CleanupFieldPlayers(block, audioClipDestory);
        }
        finally
        {
            block.Complete();
        }
    }

    void ApplyConnectionAttenuation(DSPCommandBlock block, ref ECSoundPlayer player, ref ECSoundFieldFinalMix mix, uint lerpSamples)
    {
        block.SetAttenuation(player.directMixConnection, mix.data.directL, mix.data.directR, lerpSamples);
        block.SetAttenuation(player.directLPFConnection, mix.data.directL_LPF, mix.data.directR_LPF, lerpSamples);
    }
}
