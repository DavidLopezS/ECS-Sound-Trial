using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

public class EntityPosition : IComponentData
{
    public float prevDist;
    public float currDist;
    public float left;
    public float right;
}
