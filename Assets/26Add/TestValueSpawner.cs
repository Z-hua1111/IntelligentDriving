using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestValueSpawner : MonoBehaviour
{
    public AdasManager adasManager;
    public ObstacleManager obstacleManager;
    public AutoObstacleRequester autoObstacleRequester;
    public LaneLineManager laneLineManager;
    public void Awake()
    {
        gameObject.SetActive(false);
#if UNITY_EDITOR
        gameObject.SetActive(true);
#endif
    }




}
