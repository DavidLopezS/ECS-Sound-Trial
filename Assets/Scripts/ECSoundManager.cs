using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Audio;

enum NoProvs
{

}

public class ECSoundManager : MonoBehaviour
{
    EntityManager entityManager;

    public ECSoundEmitterDefinitionAsset[] soundDefinitions;

    public AudioClip[] clips;

    ECSoundFieldMixSystem mixSystem;
    ECSoundSystem soundSystem;
    AudioManagerSystem audioManagerSystem;
    World world;

    public void OnEnable()
    {
        world = World.Active;

        for (int i = 0; i < soundDefinitions.Length; i++)
            soundDefinitions[i].Reflect(entityManager);

        audioManagerSystem = world.GetOrCreateSystem<AudioManagerSystem>();

        DSPCommandBlock block = audioManagerSystem.audioGraph.CreateCommandBlock();

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
        if (world.IsCreated)
            soundSystem.CleanupFieldPlayers(audioManagerSystem.FrameBlock, clips);
    }

    public void Update()
    {
        foreach(ECSoundEmitterDefinitionAsset def in soundDefinitions)
        {
            def.Reflect(entityManager);
        }
    }
}
