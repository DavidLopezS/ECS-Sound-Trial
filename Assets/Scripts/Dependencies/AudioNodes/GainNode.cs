using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

/*Node that will act as the Gain of the mix. Gain definition: 
 * Another word for volume, or how loud the output is 
 * How loud the input is
 * Distortion
 For further info on the topic: https://www.musicianonamission.com/gain-vs-volume/ */
[BurstCompile]
public struct GainNode : IAudioKernel<GainNode.Parameters, GainNode.Providers>
{
    public enum Parameters
    {
        Vol1,
        Vol2
    }

    public enum Providers { }

    //[NativeDisableContainerSafetyRestriction]
    //NativeArray<float> src;

    //[NativeDisableContainerSafetyRestriction]
    //NativeArray<float> dst;

    //int numChannels;
    //int sampleFrames;
    //int offset;

    public void Initialize()
    {
    }

    public void Execute (ref ExecuteContext<GainNode.Parameters, GainNode.Providers> context)
    {

        SampleBuffer  inputBuffer = context.Inputs.GetSampleBuffer(0);
        SampleBuffer outputBuffer = context.Outputs.GetSampleBuffer(0);
        int numChannels = outputBuffer.Channels;
        int sampleFrames = outputBuffer.Samples;
        NativeArray<float> src = inputBuffer.Buffer;
        NativeArray<float> dst = outputBuffer.Buffer;

        int offset = 0;
        for(int n = 0; n < sampleFrames; n++)
        {
            dst[offset] = src[offset] * context.Parameters.GetFloat(Parameters.Vol1, n); ++offset;
            dst[offset] = src[offset] * context.Parameters.GetFloat(Parameters.Vol2, n); ++offset;
        }

    }

    public void Dispose()
    {
        //src.Dispose();
        //dst.Dispose();
    }
}
