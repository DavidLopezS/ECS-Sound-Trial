using Unity.Audio;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;

// The 'audio job'. This is the kernel that defines a running DSP node inside the
// DSPGraph. It is a struct that implements the IAudioKernel interface. It can contain
// internal state, and will have the Execute function called as part of the graph
// traversal during an audio frame.
[BurstCompile(CompileSynchronously = true)]
public struct PlayClipNode : IAudioKernel<PlayClipNode.Parameters, PlayClipNode.SampleProviders>
{
    // Parameters are currently defined with enumerations. Each enum value corresponds to
    // a parameter within the node. Setting a value for a parameter uses these enum values.
    public enum Parameters
    {
        Rate
    }

    // Sample providers are defined with enumerations. Each enum value defines a slot where
    // a sample provider can live on a IAudioKernel. Sample providers are used to get samples from
    // AudioClips and VideoPlayers. They will eventually be able to pull samples from microphones and other concepts.
    public enum SampleProviders
    {
        DefaultShot
    }

    // The clip sample rate might be different to the output rate used by the system. Therefore we use a resampler
    // here.
    public Resampler resampler;

    [NativeDisableContainerSafetyRestriction]
    public NativeArray<float> resampleBuffer;

    public bool isPlaying;

    public void Initialize()
    {
        // During an initialization phase, we have access to a resource context which we can
        // do buffer allocations with safely in the job.
        resampleBuffer = new NativeArray<float>(1025 * 2, Allocator.AudioKernel);

        // set position to "end of buffer", to force pulling data on first iteration
        resampler.Position = (double)resampleBuffer.Length / 2;
    }

    public void Execute(ref ExecuteContext<Parameters, SampleProviders> context)
    {
        if (isPlaying)
        {
            // During the creation phase of this node we added an output port to feed samples to.
            // This API gives access to that output buffer.
            SampleBuffer buffer = context.Outputs.GetSampleBuffer(0);

            // Get the sample provider for the AudioClip currently being played. This allows
            // streaming of samples from the clip into a buffer.
            SampleProvider provider = context.Providers.GetSampleProvider(SampleProviders.DefaultShot);

            // We pass the provider to the resampler. If the resampler finishes streaming all the samples, it returns
            // true.
            bool isFinnished = resampler.ResampleLerpRead(provider, resampleBuffer, buffer.Buffer, context.Parameters, Parameters.Rate);

            if (isFinnished)
            {
                // Post an async event back to the main thread, telling the handler that the clip has stopped playing.
                context.PostEvent(new ClipStopped());
                isPlaying = false;
            }
        }
    }

    public void Dispose()
    {
        if (resampleBuffer.IsCreated)
            resampleBuffer.Dispose();
    }

}

[BurstCompile(CompileSynchronously = true)]
public struct PlayClipKernel : IAudioKernelUpdate<PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>
{
    // This update job is used to kick off playback of the node.
    public void Update(ref PlayClipNode audioKernel)
    {
        audioKernel.isPlaying = true;
    }
}


// Token Event that indicates that playback has finished
public struct ClipStopped { }
