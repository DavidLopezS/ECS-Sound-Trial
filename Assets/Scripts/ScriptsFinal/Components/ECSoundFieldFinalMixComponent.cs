using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using System;

[Serializable]
struct ECSoundFieldFinalMix : IComponentData
{
    public ECSoundPannerData data;
}
