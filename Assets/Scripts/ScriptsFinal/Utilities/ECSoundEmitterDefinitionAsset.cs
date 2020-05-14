using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Unity.Entities;

#if UNITY_EDITOR
using UnityEditor;
#endif
/*Class that will be used to define the ScriptableObject that refers to the definition, this asset will enable the edition of the variables that 
 * will set the values of the entities to whom the sound will be attached*/
public class ECSoundEmitterDefinitionAsset : ScriptableObject
{
    public ECSoundEmitterDefinition data;//Component data that will store the variables needed to assign the values to the entity

    [NonSerialized]
    private Entity definitionEntity;//Entity that is created

    //Internal function that will initialize the entity
    internal Entity GetEntity(EntityManager entityManager)
    {

        /* Ifs that check different conditions to get if the entity is created or there is any problem that will return a Null entity in that case*/
        if (definitionEntity.Index >= entityManager.EntityCapacity)
            return Entity.Null;

        if (!entityManager.IsCreated)
            return Entity.Null;

        if (definitionEntity != Entity.Null)
            return entityManager.Exists(definitionEntity) ? definitionEntity : Entity.Null;

        definitionEntity = entityManager.CreateEntity();//Cration of the entity variable
        entityManager.AddComponentData(definitionEntity, data);//Adding the the entity to the component data and the entity
        return definitionEntity;//returns the entity
    }

    public void Reflect(EntityManager entityManager)
    {
        /* These extra checks are needed to guard against MonoBehaviour destruction order that would otherwise cause errors 
         * in scenes like _SoundObjects*/
        Entity entity = GetEntity(entityManager);
        if (entityManager != null && entity != Entity.Null && entityManager.HasComponent<ECSoundEmitterDefinition>(entity))
            entityManager.SetComponentData(entity, data);
    }

    //IF that will create the Object and generate the unity editor on the object
#if UNITY_EDITOR
    [MenuItem("Assets/Create/Sound Emitter Definition Asset")]
    public static void CreateAsset()
    {
        ECSoundEmitterDefinitionAsset asset = ScriptableObject.CreateInstance<ECSoundEmitterDefinitionAsset>();

        asset.data.probability = 100.0f;
        asset.data.volume = 0.5f;
        asset.data.coneAngle = 360.0f;
        asset.data.coneTransition = 0.0f;
        asset.data.minDist = 5.0f;
        asset.data.maxDist = 100.0f;

        AssetDatabase.CreateAsset(asset, "Assets/SoundEmitterDefinition.asset");
        AssetDatabase.SaveAssets();

        EditorUtility.FocusProjectWindow();

        Selection.activeObject = asset;
    }
#endif
}
