using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Rendering;

//All the data that is contained on the system and component scripts is initialized here
public class MainTest : MonoBehaviour
{

    [SerializeField] private Mesh cubeMesh;
    [SerializeField] private Material cubeMaterial;
    [SerializeField] private int numOfEntities = 10000;



    private void Start()
    {
        //Variable that sets the entity on the world and will be used to set the characteristics of that entity by using archetypes
        EntityManager entityManager = World.Active.EntityManager;//New version's equivalent of World.Active.EnityManager

        /*Archetype used to set the characteristics of the entity. This can store the variables initialized on the component scripts such as the level or the movementSpeed,
        or the global variables such as the translation, the renderMesh and the LocalWorld*/
        EntityArchetype entityArchetype = entityManager.CreateArchetype
        (
            typeof(Translation),
            typeof(RenderMesh),
            typeof(MovementSpeedComponent),
            typeof(LocalToWorld),
            typeof(RotationComponent),
            typeof(Rotation)

        );

        //Array that stores all entities, on this array we can determine the number of entites we want to create
        NativeArray<Entity> entitiesArray = new NativeArray<Entity>(numOfEntities, Allocator.TempJob);

        //Funtion that creates the entities using the archetype to get the data of the entity and the array to set the number of entites
        entityManager.CreateEntity(entityArchetype, entitiesArray);

        for (int i = 0; i < entitiesArray.Length; i++)
        {
            Entity entity = entitiesArray[i];
            entityManager.SetComponentData(entity, new MovementSpeedComponent { moveSpeed = Random.Range(1f, 2f) });//Sets a random speed to the entity
            entityManager.SetComponentData(entity, new Translation { Value = new Unity.Mathematics.float3(Random.Range(-8f, 8f), Random.Range(-5f, 5f), 0) });//Sets a random position to the entity
            entityManager.SetComponentData(entity, new RotationComponent { rotationSpeed = Random.Range(1f, 2f) });//Sets a random rotarion speed to the entity
            entityManager.SetComponentData(entity, new Rotation { Value = new Unity.Mathematics.quaternion(Random.Range(1f, 8f), 0f, 0f, 0f) });//Sets a random rotarion axsis to the entity

            //Sets the mesh and the material to the entity
            entityManager.SetSharedComponentData(entity, new RenderMesh
            {
                mesh = cubeMesh,
                material = cubeMaterial
            });

        }

        //This methods disposes the memory owned by the NativeArray. The behaviour of this methods depends on the allocation strategy used when creating it (see: Allocator).
        entitiesArray.Dispose();
    }
}
