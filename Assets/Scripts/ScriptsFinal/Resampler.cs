using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.Audio
{
    [BurstCompile(CompileSynchronously = true)]
    public struct Resampler
    {
        const float k_HalfPi = math.PI * 0.5f;
        const float k_HalfSquareRootOfTwo = math.SQRT2 * 0.5f;

        public double Position;

        public bool ResampleLerpRead<T>(
            SampleProvider provider,
            NativeArray<float> input,
            NativeArray<float> output,
            ParameterData<T> parameterData,
            T rateParam)
            where T : unmanaged, Enum
        {
            bool finishedSampleProvider = false;

            for (int i = 0; i < output.Length / 2; i++)
            {
                float rate = parameterData.GetFloat(rateParam, i);
                Position += rate;

                int length = input.Length / 2 - 1;

                while (Position >= length)
                {
                    input[0] = input[input.Length - 2];
                    input[1] = input[input.Length - 1];

                    finishedSampleProvider |= ReadSamples(provider, new NativeSlice<float>(input, 2));

                    Position -= input.Length / 2 - 1;
                }

                double positionFloor = Math.Floor(Position);
                double positionFraction = Position - positionFloor;
                int previousSampleIndex = (int)positionFloor;
                int nextSampleIndex = previousSampleIndex + 1;

                float prevSampleL = input[previousSampleIndex * 2 + 0];
                float prevSampleR = input[previousSampleIndex * 2 + 1];
                float sampleL = input[nextSampleIndex * 2 + 0];
                float sampleR = input[nextSampleIndex * 2 + 1];

                output[i * 2 + 0] = (float)(prevSampleL + (sampleL - prevSampleL) * positionFraction);
                output[i * 2 + 1] = (float)(prevSampleR + (sampleR - prevSampleR) * positionFraction);
            }

            return finishedSampleProvider;
        }

        // read either mono or stereo, always convert to stereo interleaved
        static bool ReadSamples(SampleProvider provider, NativeSlice<float> destination)
        {
            if (!provider.Valid)
                return true;

            bool finished = false;

            // Read from SampleProvider and convert to interleaved stereo if needed
            if (provider.ChannelCount == 2)
            {
                int read = provider.Read(destination.Slice(0, destination.Length));
                if (read < destination.Length / 2)
                {
                    for (int i = read * 2; i < destination.Length; i++)
                        destination[i] = 0;
                    return true;
                }
            }
            else
            {
                int n = destination.Length / 2;
                NativeSlice<float> buffer = destination.Slice(0, n);
                int read = provider.Read(buffer);

                if (read < n)
                {
                    for (int i = read; i < n; i++)
                        destination[i] = 0;

                    finished = true;
                }

                for (int i = n - 1; i >= 0; i--)
                {
                    destination[i * 2 + 0] = destination[i];
                    destination[i * 2 + 1] = destination[i];
                }
            }

            return finished;
        }
    }
}
