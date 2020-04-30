using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

/*This is the base class that will be attached to the gameobject that will produce the sound, this code will save the audioclips and initialize them
 on the audiohandlersystem, that will make the audio be heard*/ 
public class AudioHandlerSettings : MonoBehaviour
{

    public AudioClip[] audioClips;//Array of audioclips

    public float Falloff = 2.1f;

    [Range(0, 1)]
    public float Volume = 0.5f;//Default volume of the audioclips, ranged from 0 to 1

    public MoveByParameters moveByParameters;//Movement parameters to determine the proximity or the distance of the agents
    public AudioParameters audioParameters;//Audio parameters that should be taken into account depending on the character pos

    AudioHandlerSystem handlerSystem;//Variable that represents the AudioHandlerSystem

    AgentAudioSystem moveBySystem;//Variable that represents the AgentAudioSystem

    SoundCollection sounds;//This variable gets the IDs of the clips

    void OnEnable()
    {

        handlerSystem = World.Active.GetOrCreateSystem<AudioHandlerSystem>();//Initialization of the AudioHandlerSystem
        moveBySystem = World.Active.GetOrCreateSystem<AgentAudioSystem>();//Initialization of the AgentAudioSystem

        sounds = moveBySystem.createCollection();//Initializes the IDs collection

        //Iteratior that is used on the audioclips array to add the audioclips to the sampleplayback system to reproduce them
        foreach (AudioClip clip in audioClips)
            handlerSystem.AddDistributedSamplePlayback(clip);

    }

    //This function will enact as an updater of the main parameters when altered
    void Update()
    {
        handlerSystem.SetParameters(audioParameters);
        moveBySystem.SetParameters(moveByParameters);
    }

    //This function will enact as a disabler in order to detach and clear the containers used
    void OnDisable()
    {
        if (World.Active != null && World.Active.IsCreated)
        {
            handlerSystem.DetachFromAllAgents();
            handlerSystem.ClearSamplePlaybacks();
            moveBySystem.ClearCollections();
        }
    }
}
