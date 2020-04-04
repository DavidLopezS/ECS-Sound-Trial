using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

/*Node that will work as a limiter: A limiter takes compression to the extreme and provides more use in the mastering process than during mixing. In fact, 
 * a limiter is a type of compressor with a really high ratio. As its name suggests, limiting sets a limit, or ceiling to the output level. In other words, 
 * no sound beyond that threshold can get through. So while normal compression begins once a threshold is passed, limiting doesn’t allow that crossing at all. 
 * It’s more of a brick wall (the most potent limiters are even called brick wall limiters). This levels the playing field (so to speak) so that the song remains even throughout.
 Link for a more extendet definition: https://www.masteringbox.com/es/audio-limiter/ */
[BurstCompile]
public struct LimiterNode : IAudioKernel<LimiterNode.Parameters, LimiterNode.Providers>//We create the node as an AudioKernel as it does the same as the IAudioJob from the original demo
{
    public enum Parameters
    {
        PreGainDBs, 
        LimitingThersholdDBs,
        ReleaseMs
    }

    public enum Providers { }

    float envelopeFollower;

    float sample;
    float gain;

    public void Initialize()
    {
        sample = 0.0f;
        gain = 1.0f;
    }

    public void Execute(ref ExecuteContext<Parameters, Providers> context)
    {
        NativeArray<float> ins = context.Inputs.GetSampleBuffer(0).Buffer;
        NativeArray<float> side = ins;

        SampleBuffer outsb = context.Outputs.GetSampleBuffer(0);
        NativeArray<float> outs = outsb.Buffer;
        int channels = outsb.Channels;

        int sampleFrames = outsb.Samples;

        float preGain = math.pow(10, context.Parameters.GetFloat(Parameters.PreGainDBs, 0) / 20);
        float thershold = math.pow(10, context.Parameters.GetFloat(Parameters.LimitingThersholdDBs, 0) / 20);
        thershold *= thershold;

        //Calculates how much the envelope decays each sample after the input volume has fallen below threshold
        float decay = math.exp(-1.0f / (context.Parameters.GetFloat(Parameters.ReleaseMs, 0) / 1000));

        //Main loop
        for(int i = 0; i < sampleFrames; i++)
        {

            for(int c = 0; c < channels; c++)
            {
                float inp = side[i * channels + c];
                sample = math.max(sample, inp * inp);
            }

            sample *= preGain;

            if(sample > thershold)
            {
                if (sample > envelopeFollower)
                    envelopeFollower = sample;
            }

            if (envelopeFollower > thershold)
                gain = thershold / envelopeFollower;

            if (sample < thershold)
                envelopeFollower *= decay;

            for(int cc = 0; cc < channels; cc++)
            {
                outs[i * channels + cc] = ins[i * channels + cc] * gain * preGain;
            }
        }

    }

    public void Dispose()
    {
    }
}
