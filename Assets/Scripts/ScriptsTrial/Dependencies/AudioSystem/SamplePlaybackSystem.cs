using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Entities;
using Unity.Audio;
using UnityEngine.Experimental.Audio;
using Unity.Collections;
using System;

//This script is used to activate the playback system
class SamplePlaybackBarrier : EntityCommandBufferSystem { } 

[UpdateInGroup(typeof(AudioFrame))]//Attribute that sets de ComponentSystem to be updated in group with the AudioFrame ComponentSystemGroup
public class SamplePlaybackSystem : ComponentSystem
{
    /*The purpose of SystemStateComponentData is to allow you to track resources internal to a system and have the opportunity to appropriately create 
     * and destroy those resources as needed without relying on individual callbacks.*/
    public struct State : ISystemStateComponentData
    {
        public ECSoundPlayerNode samplePlayer;//ECSoundPlayerNode
        public DSPConnection connection;//DSP node Connector
    }

    //Groups of samples using the type EntityQuery, EntityQuery usage: https://docs.unity3d.com/Packages/com.unity.entities@0.0/manual/component_group.html
    EntityQuery newSamplesGroup;//This group will save the new Audio Samples data
    EntityQuery aliveSamplesGroup;//This group will save the alive Audio Samples data
    EntityQuery deadSamplesGroup;//This group will save the dead Audio Samples data

    //NativeArrays that will save the entities that represent the aliveSamples and the deadSamples
    NativeArray<Entity> aliveSamples;
    NativeArray<Entity> deadSamples;

    SamplePlaybackBarrier barrier;
    AudioManagerSystem audioManager;//Initialization of the Audio Manager 

    protected override void OnUpdate()
    {
        if (!audioManager.AudioEnabled)
            return;

        DSPCommandBlock block = audioManager.FrameBlock;//Initialization of the commandblock of the audio manager for the graph usage

        const int dspBufferSize = 1024;//default size of the buffer

        UpdateEntities(block, PostUpdateCommands, dspBufferSize);/*Funtion that will update the entities if needed, the PostUpdateCommands is a default 
                                                                   variable from the ComponentSystem*/
    }

    protected override void OnCreate()
    {
        barrier = World.GetOrCreateSystem<SamplePlaybackBarrier>();//Initialization of the BarrierSystem
        audioManager = World.GetOrCreateSystem<AudioManagerSystem>();//Initialization of the audio manager system

        //Initialization of the newSamplesGroup EntityQuery, using the components SharedAudioClip and SamplePlayback
        newSamplesGroup = GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] {ComponentType.ReadOnly(typeof(SharedAudioClip)), typeof(SamplePlayback)},
            None = new ComponentType[] {typeof(State)},
            Any = Array.Empty<ComponentType>()
        });

        //Initialization of the aliveSamplesGroup EntityQuery, using the components SamplePlayback and the State
        aliveSamplesGroup = GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] {typeof(SamplePlayback), ComponentType.ReadOnly(typeof(State))},
            None = Array.Empty<ComponentType>(),
            Any = Array.Empty<ComponentType>()
        });

        //Initialization of the deadSamplesGroup EntityQuery, using the components SamplePlayback and the State
        deadSamplesGroup = GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] {typeof (State)},
            None = new ComponentType[] {ComponentType.ReadOnly(typeof(SamplePlayback))},
            Any = Array.Empty<ComponentType>()
        });
    }

    protected override void OnDestroy()
    {
        //Setting the commandblock blank
        DSPGraph audioManagerGraph = audioManager.audioGraph;

        DSPCommandBlock block = audioManagerGraph.CreateCommandBlock();
        
        //Iteration used to destroy the playback samples by using the DestroySamplePlayback function
        try
        {
            ComponentDataFromEntity<State> entityToState = GetComponentDataFromEntity<State>(false);

            ChunkUtils.ReallocateEntitiesFromChunkArray(ref aliveSamples, EntityManager, aliveSamplesGroup, Allocator.Persistent);
            for(int i = 0; i < aliveSamples.Length; ++i)
            {
                DestroySamplePlayback(block, entityToState[aliveSamples[i]]);
            }

            ChunkUtils.ReallocateEntitiesFromChunkArray(ref deadSamples, EntityManager, deadSamplesGroup, Allocator.Persistent);
            for(int i = 0; i < deadSamples.Length; ++i)
            {
                DestroySamplePlayback(block, entityToState[deadSamples[i]]);
            }
            
        }
        finally
        {
            block.Complete();
        }

        //Destroying the NativeArrays
        if (aliveSamples.IsCreated)
            aliveSamples.Dispose();
        if (deadSamples.IsCreated)
            deadSamples.Dispose();
    }

    //Dictionary that saves the audioClips added 
    Dictionary<int, AudioClip> audioClips = new Dictionary<int, AudioClip>();

    //Function that adds the audioclips to the dictionary of audioclips
    public void AddClip(AudioClip clip)
    {
        int instanceID = clip.GetInstanceID();
        if (audioClips.ContainsKey(instanceID))
            audioClips[instanceID] = clip;
    }

    /*Function called at OnUpdate, this will be used to update the samplepaybacksystem by iteratins on the EntityQuery groups created on the top of the
    component system*/
    private void UpdateEntities(DSPCommandBlock block, EntityCommandBuffer buffer, int dspBufferSize)
    {

        //ComponentDataFromEntity enacts as a nativecontainer that provides access to all instances of components of the type <T> indexed by a Entity
        ComponentDataFromEntity<State> sampleToState = GetComponentDataFromEntity<State>(false);/*This ComponentDataFromEntity stores the instances of 
                                                                                                the type State*/
        ComponentDataFromEntity<SamplePlayback> sampleToPlayback = GetComponentDataFromEntity<SamplePlayback>(false);/*This ComponentDataFromEntity 
                                                                                                    stores the instances of the type SamplePlayback*/
        ComponentDataFromEntity<SharedAudioClip> sampleToClip = GetComponentDataFromEntity<SharedAudioClip>(false);/*This ComponentDataFromEntity 
                                                                                                    stores the instances of the type SharedAudioClip*/
        
        //Creating a chunkentityenumerable from the chunkutils script
        using (ChunkEntityEnumerable newSampleEnumerable = new ChunkEntityEnumerable(EntityManager, newSamplesGroup, Allocator.TempJob))
        {
            //This iterator a sample Entity is used to iterate in the chunkentityenumerable created on the using command
            foreach(Entity sample in newSampleEnumerable)
            {
                if (!sampleToClip.Exists(sample) || !sampleToPlayback.Exists(sample))
                    continue;

                int instanceID = sampleToClip[sample].ClipInstanceID;//Saves the id of the clip instance from the ComponentDataFromEntity

                if (!audioClips.ContainsKey(instanceID))
                {
                    Debug.LogError("Cannot find AudioClip for instance ID: " + instanceID);
                    continue;
                }

                //Sets the variables clip and playback with the corresponding values and adds them to the buffer
                AudioClip clip = audioClips[instanceID];
                State playback = CreateSamplePlayback(block, sampleToPlayback[sample], clip, dspBufferSize);
                buffer.AddComponent(sample, playback);
            }
        }

        //Creating a chunkentityenumerable from the chunkutils script
        using (ChunkEntityEnumerable aliveSamplesEnumerable = new ChunkEntityEnumerable(EntityManager, aliveSamplesGroup, Allocator.TempJob))
        {
            //This iterator a sample Entity is used to iterate in the chunkentityenumerable created on the using command
            foreach (Entity sample in aliveSamplesEnumerable)
            {
                if (!sampleToState.Exists(sample) || !sampleToPlayback.Exists(sample))
                    continue;

                //Sets the variables state and sampleplayback with the corresponding values and adds them to the block
                SamplePlayback p = sampleToPlayback[sample];
                State state = sampleToState[sample];
                block.SetAttenuation(state.connection, p.Volume * p.Left, p.Volume * p.Right, dspBufferSize);
            }
        }

        //Creating a chunkentityenumerable from the chunkutils script
        using (ChunkEntityEnumerable deadSamplesEnumerable = new ChunkEntityEnumerable(EntityManager, deadSamplesGroup, Allocator.TempJob))
        {
            //This iterator a sample Entity is used to iterate in the chunkentityenumerable created on the using command
            foreach (Entity sample in deadSamplesEnumerable)
            {
                if (!sampleToState.Exists(sample))
                    continue;

                //Sets the variable state to be used to destroy the sample playback, and then removes the component from the buffer
                State state = sampleToState[sample];
                DestroySamplePlayback(block, state);
                buffer.RemoveComponent(sample, ComponentType.ReadWrite<State>());
            }
        }

    }

    //On this function the sampleplayback is created
    State CreateSamplePlayback(DSPCommandBlock block, SamplePlayback playback, AudioClip clip, int dspBufferSize)
    {
        //State that is used to create the sambleplayback base by setting the connectionwith the graph, updates and the attenuation
        State s = new State
        {
            samplePlayer = ECSoundPlayerNode.Create(block, clip),
        };

        s.connection = block.Connect(s.samplePlayer.mainDSPNode, 0, audioManager.masterChannel, 0);

        s.samplePlayer.Update(block, playback.Pitch, clip);
        block.SetAttenuation(s.connection, playback.Volume * playback.Left, playback.Volume * playback.Right, dspBufferSize);
        return s;
    }

    //Function used to destroy the sample playback
    void DestroySamplePlayback(DSPCommandBlock block, State state)
    {

        state.samplePlayer.Dispose(block);
    }

}
