using UnityEngine;
using Unity.Audio;
using Unity.Burst;

// Bootstrap MonoBehaviour to get the example running.
[BurstCompile(CompileSynchronously = true)]
public class PlayClipMain : MonoBehaviour
{
    public AudioClip clip;

    AudioOutputHandle my_Output;
    DSPGraph my_Graph;
    DSPNode my_Node;
    DSPConnection my_Connection;

    int my_HandlerID;

    void Start()
    {
        SoundFormat format = ChannelEnumConverter.GetSoundFormatFromSpeakerMode(AudioSettings.speakerMode);
        int channels = ChannelEnumConverter.GetChannelCountFromSoundFormat(format);
        AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBeffers);

        int sampleRate = AudioSettings.outputSampleRate;

        my_Graph = DSPGraph.Create(format, channels, bufferLength, sampleRate);

        DefaultDSPGraphDriver driver = new DefaultDSPGraphDriver { Graph = my_Graph };
        my_Output = driver.AttachToDefaultOutput();

        // Add an event handler delegate to the graph for ClipStopped. So we are notified
        // of when a clip is stopped in the node and can handle the resources on the main thread.
        my_HandlerID = my_Graph.AddNodeEventHandler<ClipStopped>((node, evt) =>
        {
            Debug.Log("Received ClipStopped event on main thread, cleaning resources");
        });

        // All async interaction with the graph must be done through a DSPCommandBlock.
        // Create it here and complete it once all commands are added.
        DSPCommandBlock block = my_Graph.CreateCommandBlock();

        my_Node = block.CreateDSPNode<PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>();

        // Currently input and output ports are dynamic and added via this API to a node.
        // This will change to a static definition of nodes in the future.
        block.AddOutletPort(my_Node, 2, SoundFormat.Stereo);

        // Connect the node to the root of the graph.
        my_Connection = block.Connect(my_Node, 0, my_Graph.RootDSP, 0);

        // We are done, fire off the command block atomically to the mixer thread.
        block.Complete();


        using (DSPCommandBlock myBlock = my_Graph.CreateCommandBlock())
        {
            // Decide on playback rate here by taking the provider input rate and the output settings of the system
            float resampleRate = (float)clip.frequency / AudioSettings.outputSampleRate;
            myBlock.SetFloat<PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>(my_Node, PlayClipNode.Parameters.Rate, resampleRate);

            // Assign the sample provider to the slot of the node.
            myBlock.SetSampleProvider<PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>(clip, my_Node, PlayClipNode.SampleProviders.DefaultShot);

            // Kick off playback. This will be done in a better way in the future.
            myBlock.UpdateAudioKernel<PlayClipKernel, PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>(new PlayClipKernel(), my_Node);
        }
    }

    void Update()
    {
        my_Graph.Update();
    }

    void OnDisable()
    {
        // Command blocks can also be completed via the C# 'using' construct for convenience
        using (DSPCommandBlock block = my_Graph.CreateCommandBlock())
        {
            block.Disconnect(my_Connection);
            block.ReleaseDSPNode(my_Node);
        }

        my_Graph.RemoveNodeEventHandler(my_HandlerID);

        my_Output.Dispose();
    }

    void OnGUI()
    {
        if (GUI.Button(new Rect(10, 10, 150, 100), "Play Audio"))
        {
            if (clip == null)
            {
                Debug.Log("No clip assigned, not playing (" + gameObject.name + ")");
                return;
            }

            using (DSPCommandBlock block = my_Graph.CreateCommandBlock())
            {
                // Decide on playback rate here by taking the provider input rate and the output settings of the system
                float resampleRate = (float)clip.frequency / AudioSettings.outputSampleRate;
                block.SetFloat<PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>(my_Node, PlayClipNode.Parameters.Rate, resampleRate);

                // Assign the sample provider to the slot of the node.
                block.SetSampleProvider<PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>(clip, my_Node, PlayClipNode.SampleProviders.DefaultShot);

                // Kick off playback. This will be done in a better way in the future.
                block.UpdateAudioKernel<PlayClipKernel, PlayClipNode.Parameters, PlayClipNode.SampleProviders, PlayClipNode>(new PlayClipKernel(), my_Node);
            }
        }
    }
}
