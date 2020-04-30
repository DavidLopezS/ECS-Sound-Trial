using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using System;
using Unity.Entities;

//Static class EntityManager variable extension, created to use the SetComponentObject function that is not yet available on the Entities Package 
public static class EntityManagerExtesion
{
    /*Delegate variable that is used to perform operations on the following functions, for more info on this type of variable link of the 
     * definition: https://docs.microsoft.com/en-us/dotnet/api/system.action-3?view=netframework-4.8 */
    private static Action<EntityManager, Entity, ComponentType, object> setComponentObject;
    private static bool setup;//Boolean used to check if the setup is activated

    //Public function that will enact as the main function in other scripts
    public static void SetComponentObject(this EntityManager entityManager, Entity entity, ComponentType componentType, object componentObject)
    {
        //If bool is false initialize the Setup function
        if (!setup)
        {
            Setup();
        }
        //Execution of the delegate setComponentObject
        setComponentObject.Invoke(entityManager, entity, componentType, componentObject);
    }

    //Setup of the SetComponentObject function, initialized on the main function of the script, which is the prior function
    private static void Setup()
    {
        /*Initialization of the setComponentObject variable, where the function CreateSetObjectComponent equals the main variable of the script*/
        setComponentObject = CreateSetComponentObject();

        //Sets the bool to true to not create another copy of the same variable
        setup = true;
    }

    //Function that will create the SetComponentObject
    private static Action<EntityManager, Entity, ComponentType, object> CreateSetComponentObject()
    {
        /*MethodInfo is a class that describes a Method of a type, in this case it describes the method inside the type EntityManger, which is 
         SetComponentObject, and it finds it by the bindingflags tags nonpublic and Instance*/
        MethodInfo addTypeLookupMethodInfo = typeof(EntityManager).GetMethod(
            "SetComponentObject",
            BindingFlags.NonPublic | BindingFlags.Instance);

        //Conditionar that checks if the variable is null, it it is null throws the null reference exception shown below
        if(addTypeLookupMethodInfo == null)
        {
            throw new NullReferenceException("SetComponentObjet changed");
        }

        //Returns the MethodInfo variable as a Action delegate to be initializet as a variable
        return (Action<EntityManager, Entity, ComponentType, object>)Delegate.CreateDelegate(typeof(Action<EntityManager, Entity, ComponentType, object>), null, addTypeLookupMethodInfo);

    }
}
