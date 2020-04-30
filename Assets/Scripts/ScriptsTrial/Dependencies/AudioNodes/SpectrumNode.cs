using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Audio;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public class SpectrumNode : IAudioKernel<SpectrumNode.Parameters, SpectrumNode.Providers>
{
    public enum WindowType
    {
        Rectangular = 0,
        Hamming,
        BlackmanHarris
    }

    public enum Parameters
    {
        Window
    }

    public enum Providers
    {
    }

    public const int BUFFER_SIZE = 1024;

    private NativeArray<float2> buffer;
    public NativeArray<float2> Buffer { get { return buffer; } }

    public void Initialize()
    {
        buffer = new NativeArray<float2>(BUFFER_SIZE, Allocator.AudioKernel, NativeArrayOptions.UninitializedMemory);
    }

    public void Execute(ref ExecuteContext<Parameters, Providers> context)
    {
        if (context.Inputs.Count != 1) return;

        SampleBuffer input = context.Inputs.GetSampleBuffer(0);
        Debug.Assert(input.Channels == 1);
        Debug.Assert(input.Samples == BUFFER_SIZE);
        NativeArray<float> inputBuffer = input.Buffer;

        WindowType windowType = (WindowType)math.round(context.Parameters.GetFloat(Parameters.Window, 0));

        int q = (int)math.round(math.log(buffer.Length) / math.log(2));
        Debug.Assert((int)math.round(math.pow(2, q)) == buffer.Length);
        int offset = 32 - q;
        int c = ~(~0 << q);

        for (int i = 0; i < inputBuffer.Length; ++i)
        {
            float window = windowVal(windowType, i, inputBuffer.Length);
            buffer[i] = new float2(inputBuffer[i] * window, 0f);
        }
        for (int i = inputBuffer.Length; i < buffer.Length; ++i)
        {
            buffer[i] = float2.zero;
        }
        for (int i = 1; i < buffer.Length; ++i)
        {
            int revI = (math.reversebits(i) >> offset) & c;
            if (revI <= i) continue;
            var temp = buffer[i];
            buffer[i] = buffer[revI];
            buffer[revI] = temp;
        }

        // fft
        fft(buffer);
    }

    float windowVal(WindowType windowType, int n, int N)
    {
        switch (windowType)
        {
            case WindowType.Rectangular: return 1.0f;
            case WindowType.Hamming: return hammingWindow(n, N);
            case WindowType.BlackmanHarris: return blackmanHarrisWindow(n, N);
        }
        return 1.0f;
    }

    float hammingWindow(int n, int N)
    {
        const float a0 = 0.53836f;
        const float a1 = 0.46164f;
        return a0 - a1 * math.cos((float)n / (float)N);
    }

    float blackmanHarrisWindow(int n, int N)
    {
        const float a0 = 0.35875f;
        const float a1 = 0.48829f;
        const float a2 = 0.14128f;
        const float a3 = 0.01168f;

        return a0 - a1 * math.cos(2 * math.PI * n / N) + a2 * math.cos(4 * math.PI * n / N) - a3 * math.cos(6 * math.PI * n / N);
    }


    void fft(NativeArray<float2> buffer)
    {
        for (int N = 2; N <= buffer.Length; N <<= 1)
        {
            for (int i = 0; i < buffer.Length; i += N)
            {
                for (int k = 0; k < N / 2; k++)
                {
                    int evenIndex = i + k;
                    int oddIndex = i + k + (N / 2);
                    var even = buffer[evenIndex];
                    var odd = buffer[oddIndex];

                    float term = -2 * math.PI * k / (float)N;
                    float2 exp = mulComplex(new float2(math.cos(term), math.sin(term)), odd);

                    buffer[evenIndex] = even + exp;
                    buffer[oddIndex] = even - exp;
                }
            }
        }
    }

    float2 mulComplex(float2 l, float2 r)
    {
        return new float2(
            l.x * r.x - l.y * r.y,
            l.x * r.y + l.y * r.x);
    }

    public void Dispose()
    {
        if (buffer.IsCreated) buffer.Dispose();
    }

}
