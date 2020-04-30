using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Audio;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using System;

//public struct Resampler
//{

//    public double pos;

//    public void ResampleLerpRead(ParameterData<ECSoundPlayerComponent.Parameters> parameters, NativeArray<float> output, NativeArray<float> input, SampleProvider provider)
//    {
//        for(int i = 0; i < output.Length / 2; i++)
//        {
//            float factor = parameters.GetFloat(ECSoundPlayerComponent.Parameters.Factor, i);
//            pos += factor;

//            double origpos = pos;

//            int length = input.Length / 2 - 1;

//            while(pos > length)
//            {
//                input[0] = input[input.Length - 2];
//                input[1] = input[input.Length - 1];

//                ReadSamples(provider, new NativeList<float>(2, Allocator.AudioKernel));

//                pos -= input.Length / 2 - 1;
//            }

//            double fp = math.floor(pos);

//            double frac = pos - fp;

//            int p = (int)fp;

//            int n = p + 1;

//            float prevSampleL = input[p * 2 + 0];
//            float prevSampleR = input[p * 2 + 1];
//            float sampleL = input[n * 2 + 0];
//            float sampleR = input[n * 2 + 1];

//            output[i * 2 + 0] = (float)(prevSampleL + (sampleL - prevSampleL) * frac);
//            output[i * 2 + 1] = (float)(prevSampleR + (sampleR - prevSampleR) * frac);

//        }
//    }

//    public static void ReadSamples(SampleProvider provider, NativeArray<float> dst)
//    {
//        if (!provider.Valid)
//            return;

//        if (provider.ChannelCount == 2)
//        {
//            int res = provider.Read(dst.Slice(0, dst.Length));
//            if (res < dst.Length / 2)
//            {
//                for (int i = res * 2; i < dst.Length; i++)
//                {
//                    dst[i] = 0;
//                }
//            }
//        }
//        else
//        {
//            int n = dst.Length / 2;
//            int res = provider.Read(dst.Slice(0, n));
//            if (res < n)
//            {
//                for (int i = res; i < n; i++)
//                {
//                    dst[i] = 0;
//                }
//            }
//            for (int i = n - 1; i >= 0; i--)
//            {
//                dst[i * 2 + 0] = dst[i];
//                dst[i * 2 + 1] = dst[i];
//            }
//        }
//    }
//}


[BurstCompile]
public struct ECSoundPlayerComponent : IAudioKernel<ECSoundPlayerComponent.Parameters, ECSoundPlayerComponent.Providers>
{
    public enum Parameters
    {
        Factor,
    }

    public enum Providers
    {
        Sample
    }

    public Resampler resampler;

    [NativeDisableParallelForRestriction]
    public NativeArray<float> resampleBuffer;

    public void Initialize() { }

    public void Execute(ref ExecuteContext<ECSoundPlayerComponent.Parameters, ECSoundPlayerComponent.Providers> context)
    {
        SampleBuffer buf = context.Outputs.GetSampleBuffer(0);

        SampleProvider prov = context.Providers.GetSampleProvider(Providers.Sample);

        resampler.ResampleLerpRead(prov, resampleBuffer, buf.Buffer, context.Parameters, Parameters.Factor);
    }

    public void Dispose() { }
}

public struct SoundPlayerComponentUpdate : IAudioKernelUpdate<ECSoundPlayerComponent.Parameters, ECSoundPlayerComponent.Providers, ECSoundPlayerComponent>
{
    

    public void Update(ref ECSoundPlayerComponent audioComponent)
    {
        if (!audioComponent.resampleBuffer.IsCreated)
        {
            audioComponent.resampleBuffer = new NativeArray<float>(1025 * 2, Allocator.AudioKernel);
            audioComponent.resampler.Position = (double)audioComponent.resampleBuffer.Length / 2;
        }
    }
}

[Serializable]
public struct ECSoundPlayer : IComponentData
{
    internal int soundPlayerIndex;

    internal DSPNode source;
    internal DSPNode directMixGain;
    internal DSPNode directLPFGain;
    internal DSPNode LPF;

    internal DSPConnection directMixConnection;
    internal DSPConnection directLPFConnection;
}