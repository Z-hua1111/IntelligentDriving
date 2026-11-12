using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InfineRoad : MonoBehaviour
{
    public Transform CarTransForm;
    public Transform frontRoad;
    public Transform backRoad;
    public void Update()
    {
        if(CarTransForm.position.z>frontRoad.position.z)
        {
            backRoad.transform.position = frontRoad.position + new Vector3(0, 0, 50);
            var temp = frontRoad;
            frontRoad=backRoad;
            backRoad=temp;
        }
        else if(CarTransForm.position.z<backRoad.position.z)
        {
            frontRoad.transform.position = frontRoad.position - new Vector3(0, 0, 50);
            var temp = frontRoad;
            frontRoad = backRoad;
            backRoad = temp;
        }
    }
}
