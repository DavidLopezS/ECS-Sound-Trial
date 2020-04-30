using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using System;

/*Struct that enacts as a container of chunks of data(in this case will be used for the samples), and its fucntions will return the number of elements 
 * of the container*/
public struct ChunkUtils 
{
    public static int ReallocateEntitiesFromChunkArray(ref NativeArray<Entity> entities, EntityManager entityManager, EntityQuery entityQuery, Allocator allocator, int maxElements = 0x7FFFFFFF)
    {
        NativeArray<ArchetypeChunk> chunks = entityQuery.CreateArchetypeChunkArray(Allocator.TempJob);
        int count = ReallocateEntitiesFromChunkArray(ref entities, entityManager, chunks, allocator, maxElements);
        chunks.Dispose();

        return 1;
    }

    public static int ReallocateEntitiesFromChunkArray(ref NativeArray<Entity> entities, EntityManager entityManager, NativeArray<ArchetypeChunk> chunks, Allocator allocator, int maxElements = 0x7FFFFFFF)
    {
        if (entities.IsCreated)
            entities.Dispose();

        int numElements = 0;
        for(int i = 0; i < chunks.Length; i++)
        {
            numElements += chunks[i].Count;
            if(numElements >= maxElements)
            {
                numElements = maxElements;
                break;
            }
        }

        if(numElements == 0)
        {
            entities = new NativeArray<Entity>(0, allocator);
            return 0;
        }

        entities = new NativeArray<Entity>(numElements, allocator);

        ArchetypeChunkEntityType entityType = entityManager.GetArchetypeChunkEntityType();

        int offset = 0;
        for(int c = 0; c < chunks.Length; c++) {

            ArchetypeChunk chunk = chunks[c];
            NativeArray<Entity> entitiesInChunk = chunk.GetNativeArray(entityType);
            for(int i = 0; i < chunk.Count; i++)
            {
                if (offset == numElements)
                    break;
                entities[offset++] = entitiesInChunk[i];
            }

        }

        return numElements;
    }

}

//Struct of the IDisposable type, used to create a saved memory and enum of the chunks
public struct ChunkEntityEnumerable : IDisposable
{

    ArchetypeChunkEntityType entityType;
    NativeArray<ArchetypeChunk> chunks;

    public ChunkEntityEnumerable(bool dummy)
    {
        entityType = default;
        chunks = default;
    }

    public ChunkEntityEnumerable(EntityManager entityManager, EntityQuery entityQuery, Allocator allocator)
    {
        entityType = default;
        chunks = default;

        Setup(entityManager, entityQuery, allocator);
    }

    public void Setup(EntityManager entityManager, EntityQuery entityQuery, Allocator allocator)
    {
        if (chunks.IsCreated)
            chunks.Dispose();

        entityType = entityManager.GetArchetypeChunkEntityType();
        chunks = entityQuery.CreateArchetypeChunkArray(allocator);
    }

    public bool Empty
    {
        get
        {
            for (int n = 0; n < chunks.Length; n++)
                if (chunks[n].Count > 0)
                    return false;
            return true;
        }
    }

    public struct ChunkEntityEnumerator
    {
        internal ArchetypeChunkEntityType entityType;
        internal NativeArray<ArchetypeChunk> chunks;
        internal int chunkIndex;
        internal int elementIndex;
        internal NativeArray<Entity> currentChunk;
        internal int currentChunkLength;

        public Entity Current
        {
            get
            {
                return currentChunk[elementIndex];
            }
        }

        public T GetCurrentShaderData<T>(ArchetypeChunkSharedComponentType<T> componentType, EntityManager entityManager) where T : struct, ISharedComponentData
        {
            return chunks[chunkIndex].GetSharedComponentData<T>(componentType, entityManager);
        }

        public bool MoveNext()
        {
            if (++elementIndex >= currentChunkLength)
            {
                if (++chunkIndex >= chunks.Length)
                {
                    return false;
                }
                else
                {
                    elementIndex = 0;
                    currentChunk = chunks[chunkIndex].GetNativeArray(entityType);
                    currentChunkLength = currentChunk.Length;
                }

            }

            return true;
        }

    }

    public ChunkEntityEnumerator GetEnumerator()
    {
        return new ChunkEntityEnumerator
        {
            entityType = entityType,
            chunks = chunks,
            chunkIndex = 0,
            elementIndex = -1,
            currentChunk = (chunks.Length == 0) ? new NativeArray<Entity>() : chunks[0].GetNativeArray(entityType),
            currentChunkLength = (chunks.Length == 0) ? 0 : chunks[0].Count

        };
    }

    public void Dispose()
    {
        if (chunks.IsCreated)
            chunks.Dispose();
    }
}