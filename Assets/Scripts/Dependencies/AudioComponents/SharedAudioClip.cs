using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

public struct SharedAudioClip : IComponentData
{
    public int ClipInstanceID;
}
