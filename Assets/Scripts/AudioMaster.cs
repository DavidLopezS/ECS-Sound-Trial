using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

//Audio Master MonoBehaviour that edits the parameters of the AudioManagerSystem into the game
public class AudioMaster : MonoBehaviour
{
    //Initialization of the audio parameters
#pragma warning disable 0649
    public MyAudioMasterParameters parameters;
#pragma warning restore 0649

    //Boolean that activates the fadeIn 
    public bool autoFadeIn = false;

    //initialization of the AudioManagerSystem variable
    AudioManagerSystem audioManager;
    //Boolean that checks if the game has started
    bool gameStarted;

    //Funciton used when the game has started, it sets the needed variables to true
    public void GameStarted()
    {
        gameStarted = true;
        audioManager.SetActive(true);
    }

    /*OnEnable function that creates the AudioManagerSystem and enables it. It also checks if the fade-in and the game started are enable to set the set active function to true 
      in order to set the parameters to the master node*/
    private void OnEnable()
    {
        audioManager = World.Active.GetOrCreateSystem<AudioManagerSystem>();
        audioManager.AudioEnabled = true;

        if (gameStarted || autoFadeIn)
            audioManager.SetActive(true);
    }

    //Updates the parameters of the audio manager from the Unity Inspector
    private void Update()
    {
        audioManager.SetParameters(parameters);
    }

    //Extra checks that are needed to guard against the MonoBehaviour destruction order that could cause some errors
    private void OnDisable()
    {
        if(World.Active != null && World.Active.IsCreated)
        {
            AudioManagerSystem system = World.Active.CreateSystem<AudioManagerSystem>();
            if (system != null)
                system.AudioEnabled = false;

            audioManager.SetActive(false);
        }
    }

}
