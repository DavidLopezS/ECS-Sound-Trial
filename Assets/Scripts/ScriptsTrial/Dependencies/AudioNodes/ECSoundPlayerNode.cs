using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Audio;
using Unity.Audio;

class AudioClipRegister
{
    static AudioClipRegister instance;

    List<AudioClip> providers = new List<AudioClip>();

    public static AudioClipRegister Instance
    {
        get
        {
            if (instance == null)
                instance = new AudioClipRegister();

            return instance;
        }
    }

    public void Register(AudioClip clip)
    {

        providers.Add(clip);
    }

    public void Unregister(AudioClip clip)
    {
        providers.Remove(clip);
    }
}

//This struct is used to play the clip
public struct ECSoundPlayerNode
{
    public DSPNode mainDSPNode;//main dsp node
    int IDProvider;//ID of the provider of the clip
    
    //The following two functions enact as constructors of the struct
    public static ECSoundPlayerNode Create(DSPCommandBlock block, AudioClip clip, float initialPitch = 1)
    {
        return new ECSoundPlayerNode(block, clip, initialPitch);
    }

    ECSoundPlayerNode(DSPCommandBlock block, AudioClip clip, float initialPitch)
    {
        AudioClipRegister.Instance.Register(clip);

        mainDSPNode = block.CreateDSPNode<ECSoundPlayerComponent.Parameters, ECSoundPlayerComponent.Providers, ECSoundPlayerComponent>();
        block.AddOutletPort(mainDSPNode, 2, SoundFormat.Stereo);

        IDProvider = clip.GetInstanceID();

        SetupResampling(block, clip == null ? AudioSettings.outputSampleRate : (int)clip.frequency, initialPitch);

        block.SetSampleProvider<ECSoundPlayerComponent.Parameters, ECSoundPlayerComponent.Providers, ECSoundPlayerComponent>(clip, mainDSPNode, ECSoundPlayerComponent.Providers.Sample, 0);

        SoundPlayerComponentUpdate updateKernel = new SoundPlayerComponentUpdate();
        block.CreateUpdateRequest<SoundPlayerComponentUpdate, ECSoundPlayerComponent.Parameters, ECSoundPlayerComponent.Providers, ECSoundPlayerComponent>(updateKernel, mainDSPNode, req =>
        {
            req.Dispose();
        });

    }

    //Function that enacts as the setup resampling, where the rate of the sample is calculated
    public void SetupResampling(DSPCommandBlock block, int sampleRate, float pitch)
    {
        float resamplingFactor = 1f;
        resamplingFactor = (float)sampleRate / (float)AudioSettings.outputSampleRate;
        float factor = resamplingFactor * pitch;
        block.SetFloat<ECSoundPlayerComponent.Parameters, ECSoundPlayerComponent.Providers, ECSoundPlayerComponent>(mainDSPNode, ECSoundPlayerComponent.Parameters.Factor, factor, 0);

    }

    //Funcion used to enact as disposer of the struct
    public void Dispose(DSPCommandBlock block)
    {
        //AudioClip audioClipDestroy;

        //block.SetSampleProvider<ECSoundPlayerComponent.Parameters, ECSoundPlayerComponent.Providers, ECSoundPlayerComponent>(audioClipDestroy, mainDSPNode, ECSoundPlayerComponent.Providers.Sample);

        IDProvider = 0;
        SoundPlayerDisposeJob disposer = new SoundPlayerDisposeJob();
        block.CreateUpdateRequest<SoundPlayerDisposeJob, ECSoundPlayerComponent.Parameters, ECSoundPlayerComponent.Providers, ECSoundPlayerComponent>(disposer, mainDSPNode, (request) =>
        {
            request.Dispose();
        });

        block.ReleaseDSPNode(mainDSPNode);

        
    }

    //Function used to enact as the update of the struct
    public void Update(DSPCommandBlock block, float pitch, AudioClip clip)
    {
        if (IDProvider == 0)
            return;

        float resamplingFactor = (float)clip.frequency / (float)AudioSettings.outputSampleRate;
        float factor = resamplingFactor * pitch;
        block.SetFloat<ECSoundPlayerComponent.Parameters, ECSoundPlayerComponent.Providers, ECSoundPlayerComponent>(mainDSPNode, ECSoundPlayerComponent.Parameters.Factor, factor, 1000);
    }

    public struct SoundPlayerDisposeJob : IAudioKernelUpdate<ECSoundPlayerComponent.Parameters, ECSoundPlayerComponent.Providers, ECSoundPlayerComponent>
    {
        public void Update(ref ECSoundPlayerComponent audioComp)
        {

        }
    }
}
