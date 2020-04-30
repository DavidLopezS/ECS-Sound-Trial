using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

public struct SamplePlayback : IComponentData
{
    public float Volume;
    public float Left;
    public float Right;
    public float Pitch;
    public float Loop;
}
