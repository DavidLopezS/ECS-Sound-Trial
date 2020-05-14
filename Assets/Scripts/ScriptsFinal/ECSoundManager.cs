using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Audio;

enum NoProvs
{

}
//Main MonoBehaviour that serves to initialize the assignation of the sound to the different entities renderized on screen
public class ECSoundManager : MonoBehaviour
{
    EntityManager entityManager;//Entity Manager variable that will be used to check all the Entities in the world

    //Array of a ScriptableObject class type that will save the information to assign to the different entities and the sound elements
    public ECSoundEmitterDefinitionAsset[] soundDefinitions;

    //Audio clips to be assigned to the variables
    public AudioClip[] clips;

    ECSoundFieldMixSystem mixSystem;//JobComponentSystem variable type that will be used to calculate the mix of the sound on the Field
    ECSoundSystem soundSystem;//ComponentSystem variable type that will be used to calculate the sound 
    AudioManagerSystem audioManagerSystem;//AudioManagerSystem variable 
    World world;//Definition of the World variable

    public void OnEnable()
    {
        world = World.Active;//Initialization of the world variable

        entityManager = world.EntityManager;//Initializing the entitymanager using the world variable
        
        //Iterator that will add the entitymanager to each sound emitter definition
        for (int i = 0; i < soundDefinitions.Length; i++)
            soundDefinitions[i].Reflect(entityManager);

        audioManagerSystem = world.GetOrCreateSystem<AudioManagerSystem>();//Initializing the AudioManagerSystem

        DSPCommandBlock block = audioManagerSystem.audioGraph.CreateCommandBlock();/*Creating the DSP graph command block so the audiomanager graph 
                                                                                     can be created to be used*/
        
        /*Try/Finally that creates the the soundsystem and soundfieldsystem and adjudicates the pertinent needed variables, and when that is done 
         * the graph gets completed*/
        try
        {
            soundSystem = world.GetOrCreateSystem<ECSoundSystem>();
            soundSystem.AddFieldPlayers(block, clips);

            mixSystem = world.GetOrCreateSystem<ECSoundFieldMixSystem>();
            mixSystem.AddFieldPlayers(clips.Length);
        }
        finally
        {
            block.Complete();
        }

    }

    public void OnDisable()
    {
        //Clean up the players once it is disabled
        if (world.IsCreated)
            soundSystem.CleanupFieldPlayers(audioManagerSystem.FrameBlock);
    }

    public void Update()
    {
        //Updates the entities on every update
        foreach(ECSoundEmitterDefinitionAsset def in soundDefinitions)
        {
            def.Reflect(entityManager);
        }
    }
}
