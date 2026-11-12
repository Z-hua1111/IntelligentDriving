using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 障碍物管理 - 完整版（基于蒋金成报告 + 增强）
/// 职责：解析JSON、创建/复用障碍物、提供数据给视觉增强器
/// </summary>
public class ObstacleManager : MonoBehaviour
{
    [Header("预制体配置")]
    [Tooltip("障碍物预制体数组，索引0对应ID=1，索引1对应ID=2...")]
    public GameObject[] obstaclePrefabs;
    
    [Tooltip("障碍物父节点")]
    public Transform obstacleParent;
    
    [Header("坐标换算")]
    [Tooltip("X轴缩放系数")]
    public float scaleX = 1f;
    
    [Tooltip("Z轴缩放系数")]
    public float scaleZ = 1f;
    
    [Tooltip("Y轴固定高度（贴地）")]
    public float fixedY = -3f;
    
    [Header("全局显隐")]
    [Tooltip("是否显示所有障碍物（由ADAS总开关控制）")]
    public bool isGlobalShow = true;
    
    [Header("调试")]
    [Tooltip("是否打印详细日志")]
    public bool enableDebugLog = false;
    
    // ========== 核心数据 ==========
    
    /// <summary>
    /// 障碍物字典：ID → GameObject
    /// </summary>
    private Dictionary<string, GameObject> obstacleDic = new Dictionary<string, GameObject>();
    
    /// <summary>
    /// 当前障碍物数据列表（供外部读取）
    /// </summary>
    private List<ObstacleInfo> curObstacleData = new List<ObstacleInfo>();
    
    /// <summary>
    /// 当前障碍物数据列表（公开属性，供Enhancer读取）
    /// </summary>
    public List<ObstacleInfo> CurrentObstacles => curObstacleData;
    
    /// <summary>
    /// 障碍物数据变更事件（供Enhancer订阅）
    /// </summary>
    public System.Action<List<ObstacleInfo>> onObstaclesUpdated;
    
    // ========== 数据实体类 ==========
    
    [System.Serializable]
    public class ObstacleInfo
    {
        public string ID;
        public float fPos_x;
        public float fPos_y;
        public float fAngle;
        
        // 用于调试
        public override string ToString()
        {
            return $"ID:{ID}, Pos:({fPos_x},{fPos_y}), Angle:{fAngle}";
        }
    }
    
    [System.Serializable]
    private class ObstacleWrapper
    {
        public List<ObstacleInfo> array;
    }
    
    // ========== 生命周期 ==========
    
    private void Awake()
    {
        // 如果没指定父节点，自动创建
        if (obstacleParent == null)
        {
            GameObject parentGO = new GameObject("ObstacleParent");
            parentGO.transform.SetParent(transform);
            obstacleParent = parentGO.transform;
        }
    }
    
    // ========== 核心方法 ==========
    
    /// <summary>
    /// 刷新障碍物（由外部调用，传入JSON字符串）
    /// </summary>
    public void RefreshByJson(string jsonStr)
    {
        if (string.IsNullOrEmpty(jsonStr))
        {
            if (enableDebugLog) Debug.LogWarning("[ObstacleManager] JSON为空，跳过刷新");
            return;
        }
        
        try
        {
            // JSON格式：外层拼接 {"array":[...]} 
            string fixedJson = "{\"array\":" + jsonStr + "}";
            ObstacleWrapper wrapper = JsonUtility.FromJson<ObstacleWrapper>(fixedJson);
            
            if (wrapper == null || wrapper.array == null)
            {
                Debug.LogError("[ObstacleManager] JSON解析失败，wrapper为空");
                return;
            }
            
            // 更新数据
            curObstacleData = wrapper.array;
            
            // 创建或更新障碍物
            SpawnOrUpdate();
            
            // 触发事件（通知Enhancer）
            onObstaclesUpdated?.Invoke(curObstacleData);
            
            if (enableDebugLog)
                Debug.Log($"[ObstacleManager] 刷新成功，共 {curObstacleData.Count} 个障碍物");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ObstacleManager] 解析异常: {e.Message}\nJSON: {jsonStr}");
        }
    }
    
    /// <summary>
    /// 创建或更新障碍物
    /// </summary>
    private void SpawnOrUpdate()
    {
        if (obstaclePrefabs == null || obstaclePrefabs.Length == 0)
        {
            Debug.LogError("[ObstacleManager] obstaclePrefabs 未配置！");
            return;
        }
        
        // 收集当前帧有效的ID
        HashSet<string> validIds = new HashSet<string>();
        
        foreach (var info in curObstacleData)
        {
            if (string.IsNullOrEmpty(info.ID)) continue;
            validIds.Add(info.ID);
            
            // 1. 如果ID不存在 → 创建
            if (!obstacleDic.ContainsKey(info.ID))
            {
                int index;
                if (!int.TryParse(info.ID, out index))
                {
                    Debug.LogWarning($"[ObstacleManager] ID格式错误: {info.ID}，跳过");
                    continue;
                }
                
                // ID转数组下标：ID=1 → 索引0
                int prefabIndex = index - 1;
                if (prefabIndex < 0 || prefabIndex >= obstaclePrefabs.Length)
                {
                    Debug.LogWarning($"[ObstacleManager] ID={info.ID} 超出预制体范围 (0~{obstaclePrefabs.Length})");
                    continue;
                }
                
                GameObject prefab = obstaclePrefabs[prefabIndex];
                if (prefab == null)
                {
                    Debug.LogWarning($"[ObstacleManager] 预制体索引 {prefabIndex} 为空");
                    continue;
                }
                
                // 实例化
                GameObject obj = Instantiate(prefab, obstacleParent);
                obj.name = $"Obstacle_{info.ID}";
                obstacleDic[info.ID] = obj;
                
                if (enableDebugLog)
                    Debug.Log($"[ObstacleManager] 创建新障碍物 ID:{info.ID}");
            }
            
            // 2. 更新位置和旋转（无论新建还是复用）
            GameObject target = obstacleDic[info.ID];
            UpdateTransform(target, info);
            
            // 3. 应用全局显隐
            target.SetActive(isGlobalShow);
        }
        
        // 4. 移除已不存在的障碍物
        List<string> toRemove = new List<string>();
        foreach (var kvp in obstacleDic)
        {
            if (!validIds.Contains(kvp.Key))
            {
                toRemove.Add(kvp.Key);
            }
        }
        
        foreach (string id in toRemove)
        {
            Destroy(obstacleDic[id]);
            obstacleDic.Remove(id);
            if (enableDebugLog)
                Debug.Log($"[ObstacleManager] 移除障碍物 ID:{id}");
        }
    }
    
    /// <summary>
    /// 更新单个障碍物的Transform
    /// </summary>
    private void UpdateTransform(GameObject obj, ObstacleInfo info)
    {
        // 坐标换算
        float worldX = info.fPos_x * scaleX;
        float worldZ = info.fPos_y * scaleZ;
        
        obj.transform.localPosition = new Vector3(worldX, fixedY, worldZ);
        obj.transform.localEulerAngles = new Vector3(0, info.fAngle, 0);
    }
    
    // ========== 帧更新 ==========
    
    /// <summary>
    /// 每帧更新障碍物位置（Z轴偏移+20，适配行车场景）
    /// 由 AdasManager 的 Update 调用
    /// </summary>
    public void UpdateObstaclePos()
    {
        foreach (var kvp in obstacleDic)
        {
            if (kvp.Value == null) continue;
            
            // 找到对应的数据
            var info = curObstacleData.Find(d => d.ID == kvp.Key);
            if (info == null) continue;
            
            // Z轴额外偏移（行车场景动态效果）
            Vector3 pos = kvp.Value.transform.localPosition;
            pos.z += 20.0f * Time.deltaTime;  // 用Time.deltaTime代替固定值
            kvp.Value.transform.localPosition = pos;
        }
    }
    
    // ========== 全局显隐控制 ==========
    
    /// <summary>
    /// 设置所有障碍物显隐（由ADAS总开关控制）
    /// </summary>
    public void SetAllActive(bool isShow)
    {
        isGlobalShow = isShow;
        
        foreach (var kvp in obstacleDic)
        {
            if (kvp.Value != null)
                kvp.Value.SetActive(isShow);
        }
        
        if (enableDebugLog)
            Debug.Log($"[ObstacleManager] 全局显隐: {(isShow ? "显示" : "隐藏")}");
    }
    
    /// <summary>
    /// 获取当前障碍物数量
    /// </summary>
    public int GetObstacleCount()
    {
        return obstacleDic.Count;
    }
    
    /// <summary>
    /// 清空所有障碍物
    /// </summary>
    public void ClearAll()
    {
        foreach (var kvp in obstacleDic)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }
        obstacleDic.Clear();
        curObstacleData.Clear();
        onObstaclesUpdated?.Invoke(curObstacleData);
        
        if (enableDebugLog)
            Debug.Log("[ObstacleManager] 清空所有障碍物");
    }
    
    // ========== 调试辅助 ==========
    
    /// <summary>
    /// 获取所有障碍物位置信息（调试用）
    /// </summary>
    public string GetDebugInfo()
    {
        string result = $"障碍物数量: {obstacleDic.Count}\n";
        foreach (var kvp in obstacleDic)
        {
            var info = curObstacleData.Find(d => d.ID == kvp.Key);
            result += $"  ID:{kvp.Key} -> {kvp.Value.name} ";
            if (info != null) result += info.ToString();
            result += "\n";
        }
        return result;
    }
    
    #if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // 在编辑器中可视化所有障碍物位置
        if (!Application.isPlaying) return;
        
        Gizmos.color = Color.red;
        foreach (var kvp in obstacleDic)
        {
            if (kvp.Value != null)
            {
                Gizmos.DrawWireSphere(kvp.Value.transform.position, 0.5f);
            }
        }
    }
    #endif
}