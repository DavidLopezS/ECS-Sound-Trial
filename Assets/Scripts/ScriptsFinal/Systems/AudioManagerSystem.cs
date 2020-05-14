using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Audio;
using Unity.Jobs;
using Unity.Entities;
using System;

//This script works as the global AudioManagerSystem that will be used to edit everything related to the audio system parameters and functionality

//Creation a componentsystemgroup that will be used later on to determine the audio frames that will be updated before and after the frame has been working
//[UpdateAfter(typeof(AudioBeginFrame)), UpdateBefore(typeof(AudioEndFrame))]
public class AudioFrame : ComponentSystemGroup { }

//This struct contains all the parameters that will define de audiosystem, this variables will be editable form the inspector anytime an instance of the struct has been made
[Serializable]
public struct MyAudioMasterParameters
{
    //Set of the variables of the Limiter with its limited range
    [Serializable]
    public struct Limitter
    {
        [Range(-60, 20)]
        public float preGainDbs /* = 1.0f */;
        [Range(0, 1000)]
        public float releaseMs/* = 50*/;
        [Range(-60, 0)]
        public float thersholdDBs /* = -1.5f*/;
    }

    //Instance of the limiter in order to be able to edit the limiter parameters
    public Limitter myLimitterParams;

    //Instance of the master volume with a set range
    [Range(0, 5)]
    public float masterVolume /* = 1 */;

    //function that will set a default value to each variable of the audio parameters struct
    public static MyAudioMasterParameters Defaults()
    {
        MyAudioMasterParameters ret;

        ret.masterVolume = 1;

        ret.myLimitterParams.preGainDbs = 1.0f;
        ret.myLimitterParams.releaseMs = 50;
        ret.myLimitterParams.thersholdDBs = -1.5f;

        return ret;
    }
}

//JobComponentSystem that will act as the main body of the Manager
public class AudioManagerSystem : JobComponentSystem
{
    //Transforms that will get the position of the player and the listener
    public Transform PlayerTrans { get; set; }
    public Transform ListenerTrans { get; set; }

    //Funtion that will set the variables that regard the player to true in order to check the transforms of the player
    public bool AudioEnabled
    {
        get { return myAudioEnabled && PlayerTrans != null && ListenerTrans != null; }
        set { myAudioEnabled = value; }
    }

    //Main variables of the graph
    internal DSPGraph audioGraph;
    internal DefaultDSPGraphDriver driver;
    internal AudioOutputHandle audioOutputHandle;

    internal DSPNode masterChannel => limitterNode;

    //DSPCommandBlock for Graph initializer
    internal DSPCommandBlock FrameBlock
    {
        get
        {
            if (!blockIsAlive)
                throw new InvalidOperationException("Audio Manager Frame Block not available");

            return block;
        }
    }

    //Extra graph variables that regard the Nodes, connections and commands that will take the graph into account
    DSPConnection connectionConverter;
    DSPNode convertNode;
    DSPConnection masterConnection;
    DSPNode masterGainNode;
    DSPNode limitterNode;
    bool myAudioEnabled = false;
    DSPCommandBlock block;
    bool blockIsAlive = false;

    //Variable that will save the default audio parameters in a variable to be used later on
    MyAudioMasterParameters masterParameters = MyAudioMasterParameters.Defaults();

    //Function that creates the graph and all its dependencies
    protected override void OnCreate()
    {
        AudioConfiguration audioConfig = AudioSettings.GetConfiguration();

        audioGraph = DSPGraph.Create(SoundFormat.Stereo, 2, audioConfig.dspBufferSize, audioConfig.sampleRate);
        driver = new DefaultDSPGraphDriver { Graph = audioGraph };
        audioOutputHandle = driver.AttachToDefaultOutput();

        block = audioGraph.CreateCommandBlock();
        blockIsAlive = true;

        masterGainNode = block.CreateDSPNode<GainNode.Parameters, GainNode.Providers, GainNode>();
        block.AddInletPort(masterGainNode, 2, SoundFormat.Stereo);
        block.AddOutletPort(masterGainNode, 2, SoundFormat.Stereo);
        block.SetFloat<GainNode.Parameters, GainNode.Providers, GainNode>(masterGainNode, GainNode.Parameters.Vol1, 0, 0);
        block.SetFloat<GainNode.Parameters, GainNode.Providers, GainNode>(masterGainNode, GainNode.Parameters.Vol2, 0, 0);

        masterConnection = block.Connect(masterGainNode, 0, audioGraph.RootDSP, 0);

        limitterNode = block.CreateDSPNode<LimiterNode.Parameters, LimiterNode.Providers, LimiterNode>();
        block.AddInletPort(limitterNode, 2, SoundFormat.Stereo);
        block.AddOutletPort(limitterNode, 2, SoundFormat.Stereo);
        block.Connect(limitterNode, 0, masterGainNode, 0);

        SpawnBuffers();

    }
    
    //Function that when called will input the variables needed on each instance
    public void SetParameters(MyAudioMasterParameters parameters)
    {
        masterParameters = parameters;
    }

    //Function that restarts the graph when update of the parameters is needed
    internal void SpawnBuffers()
    {
        UpdateParameters();
        block.Complete();
        block = audioGraph.CreateCommandBlock();
    }

    //OnDestroy function that will destroy the graph in case that the block is no alive
    protected override void OnDestroy()
    {
        if (blockIsAlive)
        {
            block.ReleaseDSPNode(masterGainNode);
            block.ReleaseDSPNode(limitterNode);
            block.Complete();
            block = audioGraph.CreateCommandBlock();
        }
        audioOutputHandle.Dispose();
    }

    //Main JobComponentSystem function 
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        return inputDeps;
    }
    
    //Update of the graph parameters in case it is needed to be updated
    internal void UpdateParameters(int lerpLength = 1024)//We set the default input value as 1024
    {
        if (blockIsAlive)
        {
            block.SetFloat<LimiterNode.Parameters, LimiterNode.Providers, LimiterNode>(limitterNode, LimiterNode.Parameters.PreGainDBs, masterParameters.myLimitterParams.preGainDbs, lerpLength);

            block.SetFloat<LimiterNode.Parameters, LimiterNode.Providers, LimiterNode>(limitterNode, LimiterNode.Parameters.LimitingThersholdDBs, 
                                                                                        masterParameters.myLimitterParams.thersholdDBs, lerpLength);

            block.SetFloat<LimiterNode.Parameters, LimiterNode.Providers, LimiterNode>(limitterNode, LimiterNode.Parameters.ReleaseMs, masterParameters.myLimitterParams.releaseMs, lerpLength);

            block.SetAttenuation(masterConnection, masterParameters.masterVolume, masterParameters.masterVolume, 0);
        }
    }

    //Function that will set the volume of the gain node depending if it is active or not
    public void SetActive(bool value)
    {
        int volume = value ? 1 : 0;
        block.SetFloat<GainNode.Parameters, GainNode.Providers, GainNode>(masterGainNode, GainNode.Parameters.Vol1, volume, 5 * 44100);
        block.SetFloat<GainNode.Parameters, GainNode.Providers, GainNode>(masterGainNode, GainNode.Parameters.Vol2, volume, 5 * 44100);
    }

}

//Class that defines the begining of the audioframe
[UpdateAfter(typeof(AudioManagerSystem))]
class AudioBeginFrame : JobComponentSystem
{
    AudioManagerSystem audioManager;
    
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        return inputDeps;    
    }

    protected override void OnCreate()
    {
        audioManager = World.GetOrCreateSystem<AudioManagerSystem>();
    }
}

//Class that defines the end of the frame
[UpdateAfter(typeof(AudioManagerSystem)), AlwaysUpdateSystem]
class AudioEndFrame : JobComponentSystem
{
    AudioManagerSystem audioManager;

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        return inputDeps;
    }

    protected override void OnCreate()
    {
        audioManager = World.GetOrCreateSystem<AudioManagerSystem>();
    }
}
