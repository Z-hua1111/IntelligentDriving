using UnityEngine;
using System.Collections.Generic;

public class LaneLineManager : MonoBehaviour
{
    [Header("车道线配置")]
    public float lengthMultiplier = 6f;
    [Tooltip("虚线单段长度（米）")]
    public float dashSegmentLength = 1.0f;
    [Tooltip("虚线间隔长度（米）")]
    public float dashGapLength = 0.5f;
    [Header("颜色配置")]
    public Color defaultLaneColor = Color.white;
    [Header("引用")]
    public Transform laneParent;

    private List<GameObject> laneObjects = new List<GameObject>();

    void Awake()
    {
        if (laneParent == null)
        {
            GameObject parentGO = new GameObject("LaneLines");
            parentGO.transform.SetParent(transform);
            parentGO.transform.localPosition = Vector3.zero;
            laneParent = parentGO.transform;
        }
    }

    /// <summary>
    /// 刷新车道线（外部调用）
    /// </summary>
    public void RefreshLaneLines(string jsonStr)
    {
        if (string.IsNullOrEmpty(jsonStr))
        {
            ClearAllLanes();
            return;
        }

        try
        {
            string fixedJson = "{\"array\":" + jsonStr + "}";
            LaneLineWrapper wrapper = JsonUtility.FromJson<LaneLineWrapper>(fixedJson);

            if (wrapper == null || wrapper.array == null)
            {
                Debug.LogWarning("[LaneLineManager] 车道线数据为空");
                return;
            }

            ClearAllLanes();

            foreach (var data in wrapper.array)
            {
                CreateLaneLine(data);
            }

            Debug.Log($"[LaneLineManager] 绘制 {wrapper.array.Count} 条车道线");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LaneLineManager] 解析车道线失败: {e.Message}\nJSON: {jsonStr}");
        }
    }

    /// <summary>
    /// 创建单条车道线（实线/虚线分离）
    /// </summary>
    private void CreateLaneLine(LaneLineData data)
    {
        // 父对象（用于管理同一条车道线的所有段）
        GameObject laneRoot = new GameObject($"Lane_{data.ePosIndex}");
        laneRoot.transform.SetParent(laneParent);
        laneRoot.transform.localPosition = Vector3.zero;
        laneObjects.Add(laneRoot);

        float xOffset = float.Parse(data.fLineC0);
        float startZ = float.Parse(data.fStartX) * lengthMultiplier;
        float endZ = float.Parse(data.fEndX) * lengthMultiplier;

        // 处理坐标逆序
        if (startZ > endZ) (startZ, endZ) = (endZ, startZ);
        float totalLength = endZ - startZ;

        // 实线：直接创建1个LineRenderer
        if (data.eType == "1")
        {
            CreateSingleLineSegment(laneRoot.transform, xOffset, startZ, endZ, data.eColor);
        }
        // 虚线：创建多个短实线段，段之间留空
        else
        {
            float currentZ = startZ;
            // 循环创建每一段虚线
            while (currentZ + dashSegmentLength < endZ)
            {
                float segmentEndZ = currentZ + dashSegmentLength;
                // 创建单段短实线
                CreateSingleLineSegment(laneRoot.transform, xOffset, currentZ, segmentEndZ, data.eColor);
                // 跳过间隔段
                currentZ = segmentEndZ + dashGapLength;
            }
            // 处理最后一段（不足完整段的部分）
            if (currentZ < endZ)
            {
                CreateSingleLineSegment(laneRoot.transform, xOffset, currentZ, endZ, data.eColor);
            }
        }
    }

    /// <summary>
    /// 创建单个线段（实线/虚线段）
    /// </summary>
    private void CreateSingleLineSegment(Transform parent, float xOffset, float startZ, float endZ, string colorCode)
    {
        GameObject segmentGO = new GameObject($"Segment_{startZ:F1}_{endZ:F1}");
        segmentGO.transform.SetParent(parent);

        LineRenderer lr = segmentGO.AddComponent<LineRenderer>();
        // LineRenderer 基础配置
        lr.useWorldSpace = true;
        lr.startWidth = 0.08f;
        lr.endWidth = 0.08f;
        lr.loop = false;
        lr.numCapVertices = 1;

        // ===== 修复紫色核心代码 =====
        // 1. 获取Unity内置的纯色Shader（无自定义材质依赖）
        Shader unlitShader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        if (unlitShader != null)
        {
            // 2. 创建临时材质并设置颜色（仅用内置Shader，无外部依赖）
            Material mat = new Material(unlitShader);
            Color laneColor = GetLaneColor(colorCode);
            mat.color = laneColor;
            lr.material = mat;
        }
        // 3. 兜底：同时设置LineRenderer颜色（双重保障）
        Color finalColor = GetLaneColor(colorCode);
        lr.startColor = finalColor;
        lr.endColor = finalColor;

        // 设置线段顶点
        Vector3[] points = new Vector3[]
        {
        new Vector3(xOffset, 0.05f, startZ),
        new Vector3(xOffset, 0.05f, endZ)
        };
        lr.positionCount = points.Length;
        lr.SetPositions(points);
    }

    /// <summary>
    /// 获取车道线颜色
    /// </summary>
    private Color GetLaneColor(string colorCode)
    {
        switch (colorCode)
        {
            case "1": return Color.white;
            case "2": return Color.yellow;
            case "3": return Color.blue;
            case "4": return Color.red;
            default: return defaultLaneColor;
        }
    }

    /// <summary>
    /// 清空所有车道线
    /// </summary>
    public void ClearAllLanes()
    {
        foreach (var go in laneObjects)
        {
            if (go != null) Destroy(go);
        }
        laneObjects.Clear();
    }

    // 数据实体类
    [System.Serializable]
    public class LaneLineData
    {
        public string ePosIndex;
        public string eType;      // "1"=实线, "2"=虚线
        public string eColor;     // "1"=白色, "2"=黄色
        public string fLineC0;    // X轴偏移（字符串转float）
        public string fLineC1;
        public string fLineC2;
        public string fLineC3;
        public string fStartX;    // 起点Z（字符串转float）
        public string fEndX;      // 终点Z（字符串转float）
    }

    [System.Serializable]
    private class LaneLineWrapper
    {
        public List<LaneLineData> array;
    }
}