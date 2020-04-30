using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Audio;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;

public struct StateVariableFilter
{
    public enum FilterType
    {
        Lowpass,
        Highpass,
        Bandpass,
        Bell,
        Notch,
        Lowshelf,
        Highshelf
    }

    DSPNode m_Node;
    NativeArray<NodeJob.Channel> m_Channels;

    public DSPNode node => m_Node;

    public static StateVariableFilter Create(DSPCommandBlock block)
    {
        return new StateVariableFilter(block);
    }

    StateVariableFilter(DSPCommandBlock block)
    {
        m_Channels = new NativeArray<NodeJob.Channel>(2, Allocator.Persistent);
        for (int c = 0; c < m_Channels.Length; ++c)
        {
            NodeJob.Channel channel = new NodeJob.Channel();
            m_Channels[c] = channel;
        }

        m_Node = block.CreateDSPNode<NodeJob.Parameters, NodeJob.Providers, NodeJob>();
        block.AddInletPort(m_Node, 2, SoundFormat.Stereo);
        block.AddOutletPort(m_Node, 2, SoundFormat.Stereo);

        UpdateJob reverbUpdateJob = new UpdateJob();
        reverbUpdateJob.channels = m_Channels;
        block.CreateUpdateRequest<UpdateJob, NodeJob.Parameters, NodeJob.Providers, NodeJob>(reverbUpdateJob, m_Node, req => { req.Dispose(); });

        block.SetFloat<NodeJob.Parameters, NodeJob.Providers, NodeJob>(m_Node, NodeJob.Parameters.FilterType, (int)FilterType.Lowpass, 0);
        block.SetFloat<NodeJob.Parameters, NodeJob.Providers, NodeJob>(m_Node, NodeJob.Parameters.Cutoff, 1500.0f, 0);
        block.SetFloat<NodeJob.Parameters, NodeJob.Providers, NodeJob>(m_Node, NodeJob.Parameters.Q, 0.707f, 0);
        block.SetFloat<NodeJob.Parameters, NodeJob.Providers, NodeJob>(m_Node, NodeJob.Parameters.GainInDBs, 0.0f, 0);
    }

    public void Dispose(DSPCommandBlock block)
    {
        DisposeJob disposeJob = new DisposeJob();
        NativeArray<NodeJob.Channel> ch = m_Channels;
        m_Channels = default;
        block.CreateUpdateRequest<DisposeJob, NodeJob.Parameters, NodeJob.Providers, NodeJob>(disposeJob, m_Node, req => {
            ch.Dispose();
            req.Dispose();
        });
        block.ReleaseDSPNode(m_Node);
    }

    public struct Coefficients
    {
        public float A, g, k, a1, a2, a3, m0, m1, m2;
    }

    public static Coefficients DesignBell(float fc, float quality, float linearGain)
    {
        float A = linearGain;
        float g = Mathf.Tan(Mathf.PI * fc);
        float k = 1 / (quality * A);
        float a1 = 1 / (1 + g * (g + k));
        float a2 = g * a1;
        float a3 = g * a2;
        float m0 = 1;
        float m1 = k * (A * A - 1);
        float m2 = 0;
        return new Coefficients { A = A, g = g, k = k, a1 = a1, a2 = a2, a3 = a3, m0 = m0, m1 = m1, m2 = m2 };
    }

    public static Coefficients DesignLowpass(float normalizedFrequency, float Q, float linearGain)
    {
        float A = linearGain;
        float g = math.tan(Mathf.PI * normalizedFrequency);
        float k = 1 / Q;
        float a1 = 1 / (1 + g * (g + k));
        float a2 = g * a1;
        float a3 = g * a2;
        float m0 = 0;
        float m1 = 0;
        float m2 = 1;
        return new Coefficients { A = A, g = g, k = k, a1 = a1, a2 = a2, a3 = a3, m0 = m0, m1 = m1, m2 = m2 };
    }

    public static Coefficients DesignBandpass(float normalizedFrequency, float Q, float linearGain)
    {
        Coefficients coeffs = Design(FilterType.Lowpass, normalizedFrequency, Q, linearGain);
        coeffs.m1 = 1;
        coeffs.m2 = 0;
        return coeffs;
    }

    public static Coefficients DesignHighpass(float normalizedFrequency, float Q, float linearGain)
    {
        Coefficients coeffs = Design(FilterType.Lowpass, normalizedFrequency, Q, linearGain);
        coeffs.m0 = 1;
        coeffs.m1 = -coeffs.k;
        coeffs.m2 = -1;
        return coeffs;
    }

    public static Coefficients DesignNotch(float normalizedFrequency, float Q, float linearGain)
    {
        Coefficients coeffs = DesignLowpass(normalizedFrequency, Q, linearGain);
        coeffs.m0 = 1;
        coeffs.m1 = -coeffs.k;
        coeffs.m2 = 0;
        return coeffs;
    }

    public static Coefficients DesignLowshelf(float normalizedFrequency, float Q, float linearGain)
    {
        float A = linearGain;
        float g = Mathf.Tan(Mathf.PI * normalizedFrequency) / Mathf.Sqrt(A);
        float k = 1 / Q;
        float a1 = 1 / (1 + g * (g + k));
        float a2 = g * a1;
        float a3 = g * a2;
        float m0 = 1;
        float m1 = k * (A - 1);
        float m2 = A * A - 1;
        return new Coefficients { A = A, g = g, k = k, a1 = a1, a2 = a2, a3 = a3, m0 = m0, m1 = m1, m2 = m2 };
    }

    public static Coefficients DesignHighshelf(float normalizedFrequency, float Q, float linearGain)
    {
        float A = linearGain;
        float g = Mathf.Tan(Mathf.PI * normalizedFrequency) / Mathf.Sqrt(A);
        float k = 1 / Q;
        float a1 = 1 / (1 + g * (g + k));
        float a2 = g * a1;
        float a3 = g * a2;
        float m0 = A * A;
        float m1 = k * (1 - A) * A;
        float m2 = 1 - A * A;
        return new Coefficients { A = A, g = g, k = k, a1 = a1, a2 = a2, a3 = a3, m0 = m0, m1 = m1, m2 = m2 };
    }

    public static Coefficients Design(FilterType type, float normalizedFrequency, float Q, float linearGain)
    {
        switch (type)
        {
            case FilterType.Lowpass: return DesignLowpass(normalizedFrequency, Q, linearGain);
            case FilterType.Highpass: return DesignHighpass(normalizedFrequency, Q, linearGain);
            case FilterType.Bandpass: return DesignBandpass(normalizedFrequency, Q, linearGain);
            case FilterType.Bell: return DesignBell(normalizedFrequency, Q, linearGain);
            case FilterType.Notch: return DesignNotch(normalizedFrequency, Q, linearGain);
            case FilterType.Lowshelf: return DesignLowshelf(normalizedFrequency, Q, linearGain);
            case FilterType.Highshelf: return DesignHighshelf(normalizedFrequency, Q, linearGain);
            default:
                throw new ArgumentOutOfRangeException("type", type, null);
        }
    }

    public static Coefficients Design(FilterType filterType, float cutoff, float Q, float gainInDBs, float sampleRate)
    {
        float linearGain = Mathf.Pow(10, gainInDBs / 20);
        switch (filterType)
        {
            case FilterType.Lowpass:
                return DesignLowpass(cutoff / sampleRate, Q, linearGain);
            case FilterType.Highpass:
                return DesignHighpass(cutoff / sampleRate, Q, linearGain);
            case FilterType.Bandpass:
                return DesignBandpass(cutoff / sampleRate, Q, linearGain);
            case FilterType.Bell:
                return DesignBell(cutoff / sampleRate, Q, linearGain);
            case FilterType.Notch:
                return DesignNotch(cutoff / sampleRate, Q, linearGain);
            case FilterType.Lowshelf:
                return DesignLowshelf(cutoff / sampleRate, Q, linearGain);
            case FilterType.Highshelf:
                return DesignHighshelf(cutoff / sampleRate, Q, linearGain);
            default:
                throw new ArgumentOutOfRangeException("filterType", filterType, null);
        }
    }

    [BurstCompile]
    public struct NodeJob : IAudioKernel<NodeJob.Parameters, NodeJob.Providers>
    {
        public struct Channel
        {
            public float z1, z2;
        }

        [NativeDisableContainerSafetyRestriction]
        public NativeArray<Channel> m_Channels;

        public enum Parameters
        {
            FilterType,
            Cutoff,
            Q,
            GainInDBs
        }

        public enum Providers { }

        public void Initialize()
        {
        }

        public void Init(ParameterData<Parameters> parameters)
        {
        }

        public void Execute(ref ExecuteContext<Parameters, Providers> ctx)
        {
            SampleBuffer inputBuffer = ctx.Inputs.GetSampleBuffer(0);
            SampleBuffer outputBuffer = ctx.Outputs.GetSampleBuffer(0);
            int numChannels = (int)outputBuffer.Channels;
            int sampleFrames = (int)outputBuffer.Samples;
            int numChamples = numChannels * sampleFrames;

            NativeArray<float> inBuf = inputBuffer.Buffer;
            NativeArray<float> outBuf = outputBuffer.Buffer;

            if (m_Channels.Length == 0)
            {
                for (int n = 0; n < numChamples; n++)
                    outBuf[n] = 0.0f;
                return;
            }

            ref ParameterData<Parameters> parameters = ref ctx.Parameters;
            Coefficients coeffs = Design((FilterType)parameters.GetFloat(Parameters.FilterType, 0), parameters.GetFloat(Parameters.Cutoff, 0), parameters.GetFloat(Parameters.Q, 0), parameters.GetFloat(Parameters.GainInDBs, 0), ctx.SampleRate);

            for (int c = 0; c < m_Channels.Length; c++)
            {
                float z1 = m_Channels[c].z1;
                float z2 = m_Channels[c].z2;

                for (int i = 0; i < numChamples; i += numChannels)
                {
                    float x = inBuf[i + c];

                    float v3 = x - z2;
                    float v1 = coeffs.a1 * z1 + coeffs.a2 * v3;
                    float v2 = z2 + coeffs.a2 * z1 + coeffs.a3 * v3;
                    z1 = 2 * v1 - z1;
                    z2 = 2 * v2 - z2;
                    outBuf[i + c] = coeffs.m0 * x + coeffs.m1 * v1 + coeffs.m2 * v2;
                }

                m_Channels[c] = new Channel { z1 = z1, z2 = z2 };
            }
        }

        public void Dispose()
        {
        }
    }

    [BurstCompileAttribute]
    struct UpdateJob : IAudioKernelUpdate<NodeJob.Parameters, NodeJob.Providers, NodeJob>
    {
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<NodeJob.Channel> channels;

        public void Update(ref NodeJob audioJob)
        {
            audioJob.m_Channels = channels;
        }
    }

    [BurstCompileAttribute]
    struct DisposeJob : IAudioKernelUpdate<NodeJob.Parameters, NodeJob.Providers, NodeJob>
    {
        public void Update(ref NodeJob audioJob)
        {
        }
    }

}
