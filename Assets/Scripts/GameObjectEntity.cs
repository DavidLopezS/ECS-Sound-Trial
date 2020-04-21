using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Unity.Entities;
using MonoBehaviour = UnityEngine.MonoBehaviour;
using GameObject = UnityEngine.GameObject;
using Component = UnityEngine.Component;

[DisallowMultipleComponent]
[ExecuteAlways]
public class GameObjectEntity : MonoBehaviour
{
    public EntityManager EntityManager
    {
        get
        {
            if (enabled && gameObject.activeInHierarchy)
                ReInitializeEntityManagerAndEntityIfNecessary();
            return myEntityManager;
        }
    }
    EntityManager myEntityManager;

    public Entity Entity
    {
        get
        {
            if (enabled && gameObject.activeInHierarchy)
                ReInitializeEntityManagerAndEntityIfNecessary();
            return myEntity;
        }
    }
    Entity myEntity;

    void ReInitializeEntityManagerAndEntityIfNecessary()
    {
        // in case e.g., on a prefab that was open for edit when domain was unloaded
        // existing m_EntityManager lost all its data, so simply create a new one
        if (myEntityManager != null && !myEntityManager.IsCreated && !myEntity.Equals(default(Entity)))
            Initialize();
    }

    public static Entity AddToEntityManager(EntityManager entityManager, GameObject gameObject)
    {
        ComponentType[] types;
        Component[] components;
        GetComponents(gameObject, true, out types, out components);

        EntityArchetype archetype;
        try
        {
            archetype = entityManager.CreateArchetype(types);
        }
        catch(Exception e)
        {
            for(int i = 0; i < types.Length; ++i)
            {
                if(Array.IndexOf(types, types[i]) != i)
                {
                    Debug.LogWarning($"GameObject '{gameObject}' has multiple {types[i]} components and cannot be converted, skipping.");
                    return Entity.Null;
                }
            }

            throw e;
        }

        Entity entity = CreateEntity(entityManager, archetype, components, types);

        return entity;
    }

    public static void AddToEntity(EntityManager entityManager, GameObject gameObject, Entity entity)
    {
        Component[] components = gameObject.GetComponents<Component>();

        for(int i = 0; i != components.Length; i++)
        {
            Component com = components[i];
            ComponentDataProxyBase proxy = com as ComponentDataProxyBase;
            Behaviour behaviour = com as Behaviour;
            if (behaviour != null && !behaviour.enabled)
                continue;

            if(!(com is GameObjectEntity) && com != null && proxy == null)
            {
                entityManager.AddComponentObject(entity, com);
            }
        }
    }

    static void GetComponents(GameObject gameObject, bool includeGameObjectComponents, out ComponentType[] types, out Component[] components)
    {
        components = gameObject.GetComponents<Component>();

        int componentCount = 0;
        for(int i = 0; i != components.Length; i++)
        {
            Component com = components[i];
            ComponentDataProxyBase componentData = com as ComponentDataProxyBase;

            if (com == null)
                Debug.LogWarning($"The referenced script is missing on {gameObject.name}", gameObject);
            else if (componentData != null)
                componentCount++;
            else if (includeGameObjectComponents && !(com is GameObjectEntity))
                componentCount++;
        }

        types = new ComponentType[componentCount];

        int t = 0;
        for(int i = 0; i != components.Length; i++)
        {
            Component com = components[i];
            ComponentDataProxyBase componentData = com as ComponentDataProxyBase;

            if (componentData != null)
                types[t++] = ComponentType.ReadWrite<ComponentDataProxyBase>();
            else if (includeGameObjectComponents && !(com is GameObjectEntity) && com != null)
                types[t++] = com.GetType();

        }
    }

    static Entity CreateEntity(EntityManager entityManager, EntityArchetype archetype, IReadOnlyList<Component> components, IReadOnlyList<ComponentType> types)
    {
        Entity entity = entityManager.CreateEntity(archetype);
        int t = 0;
        for (int i = 0; i != components.Count; i++)
        {
            Component com = components[i];
            ComponentDataProxyBase componentDataProxy = com as ComponentDataProxyBase;

            if (componentDataProxy != null)
            {
                //componentDataProxy.UpdateComponentData(entityManager, entity);
                //entityManager.SetComponentData(entity, new ComponentDataProxyBase {  });
                t++;
            }
            else if (!(com is GameObjectEntity) && com != null)
            {
                entityManager.SetComponentObject(entity, types[t], com);
                t++;
            }
        }
        return entity;
    }

    void Initialize()
    {
        DefaultWorldInitialization.DefaultLazyEditModeInitialize();
        if (World.Active != null)
        {
            myEntityManager = World.Active.EntityManager;
            myEntity = AddToEntityManager(myEntityManager, gameObject);
        }
    }

    protected virtual void OnEnable()
    {
        Initialize();
    }

    protected virtual void OnDisable()
    {
        if(EntityManager != null && EntityManager.IsCreated && EntityManager.Exists(Entity))
            EntityManager.DestroyEntity(Entity);

        myEntityManager = null;
        myEntity = Entity.Null;
    }
    
    public static void CopyAllComponentsToEntity(GameObject gameObject, EntityManager entityManager, Entity entity)
    {
        foreach(ComponentDataProxyBase proxy in gameObject.GetComponents<ComponentDataProxyBase>())
        {
            ComponentType type = ComponentType.ReadWrite<ComponentDataProxyBase>();
            entityManager.AddComponent(entity, type);
            //entityManager.SetComponentData(entity, proxy);
        }
    }
}
