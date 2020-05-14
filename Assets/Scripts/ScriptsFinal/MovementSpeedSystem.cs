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

//public class MovementSpeedSystem : ComponentSystem
//{
//    protected override void OnUpdate()
//    {
//        Entities.ForEach((ref Translation translation, ref MovementSpeedComponent movementSpeedComponent) => {

//            translation.Value.y += movementSpeedComponent.moveSpeed * Time.deltaTime;           
//            if(translation.Value.y > 5f)
//            {
//                movementSpeedComponent.moveSpeed = -math.abs(movementSpeedComponent.moveSpeed);
//            }
//            if(translation.Value.y < -5f)
//            {
//                movementSpeedComponent.moveSpeed = +math.abs(movementSpeedComponent.moveSpeed);
//            }

//        }); 
//    }
//}

//JobComponentSystem applyed, mix of ECS and JobSystem less CPU consuming and much more eficient
public class MovementSpeedSystem : JobComponentSystem
{

    //OnUpdate function of the JobComponentSystem, on this function the MoveSpeedJob will be initialized and scheduled, in this case the only inputed variable will be the deltaTime
    protected override JobHandle OnUpdate(JobHandle inputDependencies)

    {
        MoveSpeedJob moveSpeedJob = new MoveSpeedJob
        {
            deltaTime = Time.deltaTime//deltaTime getting its value
        };

        //Variable that must be returned in order to make the function work, we'll input the main function and the inputDependencies
        JobHandle jobHandle = moveSpeedJob.Schedule(this, inputDependencies);
        jobHandle.Complete();
        return jobHandle;
    }
}

//Main Job used to Move the cubes according to the speed
[BurstCompile]//BusrstCompile Attribute to make the compilation even smoother
public struct MoveSpeedJob : IJobForEach<Translation, MovementSpeedComponent>
{
    [ReadOnly] public float deltaTime;//deltaTime cannot be initializet on a Job as it is not a static vaiable, so it has to be created as a variable and inputed on the job constructor 

    public void Execute(ref Translation translation, ref MovementSpeedComponent movementSpeedComponent)
    {
        translation.Value.y += movementSpeedComponent.moveSpeed * deltaTime;
        if (translation.Value.y > 5f)
        {
            movementSpeedComponent.moveSpeed = -math.abs(movementSpeedComponent.moveSpeed);
        }
        if (translation.Value.y < -5f)
        {
            movementSpeedComponent.moveSpeed = +math.abs(movementSpeedComponent.moveSpeed);
        }
    }
}


