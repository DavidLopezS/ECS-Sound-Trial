using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

//Not job component system applyed, classic system of ECS logic

//public class RotationSystem : ComponentSystem
//{

//    protected override void OnUpdate()
//    {
//        Entities.ForEach((ref RotationComponent rotationComponent, ref Rotation rotation) =>
//        {

//            rotation.Value = math.mul(math.normalize(rotation.Value), quaternion.AxisAngle(math.up(), rotationComponent.rotationSpeed * Time.deltaTime));


//        });

//    }
//}

//JobComponentSystem applyed, mix of ECS and JobSystem less CPU consuming and much more eficient
public class RotationSystem : JobComponentSystem
{
    //OnUpdate function of the JobComponentSystem, on this function the RotationSystem will be initialized and scheduled, in this case the only inputed variable will be the deltaTime
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        RotationJob rotationJob = new RotationJob
        {
            deltaTime = Time.deltaTime //deltaTime getting its value
        };

        //Variable that must be returned in order to make the function work, we'll input the main function and the inputDependencies
        JobHandle jobHandle = rotationJob.Schedule(this, inputDeps);
        jobHandle.Complete();
        return jobHandle;
    }

}

//Main Job used to Rotate the cubes according to the speed
[BurstCompile]//BusrstCompile Attribute to make the compilation even smoother
public struct RotationJob : IJobForEach<Rotation, RotationComponent>
{
    [ReadOnly] public float deltaTime;//deltaTime cannot be initializet on a Job as it is not a static vaiable, so it has to be created as a variable and inputed on the job constructor

    public void Execute(ref Rotation rotation, ref RotationComponent rotationComponent)
    {
        rotation.Value = math.mul(math.normalize(rotation.Value), quaternion.AxisAngle(math.up(), rotationComponent.rotationSpeed * deltaTime));
    }
}
