using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using static ObstacleManager;
using HMI.Communication;

/// <summary>
/// 障碍物视觉增强器 - 监听 ObstacleManager 的数据，做距离色块和预警标签
/// 挂载到自车物体上
/// </summary>
public class ObstacleVisualEnhancer : MonoBehaviour
{
    [Header("视觉配置")]
    [Tooltip("安全距离（米）")]
    public float safeDistance = 5f;

    [Tooltip("警示距离（米）")]
    public float warningDistance = 3f;

    [Tooltip("危险距离（米）")]
    public float dangerDistance = 1.5f;

    [Header("色块配置")]
    [Tooltip("安全色")]
    public Color safeColor = Color.green;

    [Tooltip("警示色")]
    public Color warningColor = Color.yellow;

    [Tooltip("危险色")]
    public Color dangerColor = Color.red;

    [Tooltip("色块大小")]
    public float blockSize = 0.5f;

    [Tooltip("色块Y轴高度（贴地高度）")]
    public float blockHeight = 0.1f;

    [Header("标签配置")]
    [Tooltip("是否显示距离标签")]
    public bool showDistanceLabel = true;

    [Tooltip("标签字体大小")]
    public int labelFontSize = 20;

    [Header("联动引用")]
    [Tooltip("视角控制器（自动查找）")]
    public ViewSwitchController viewController;

    [Tooltip("障碍物管理器（自动查找）")]
    public MonoBehaviour obstacleManagerRef; // 用MonoBehaviour引用，运行时转成ObstacleManager

    // 私有缓存
    private Transform selfTransform;
    private ViewMode currentMode = ViewMode.Drive;
    private ObstacleManager obstacleManager;

    // 障碍物视觉数据
    private Dictionary<string, ObstacleVisualData> visualDataMap = new Dictionary<string, ObstacleVisualData>();

    // 是否初始化完成
    private bool isInitialized = false;

    /// <summary>
    /// 障碍物视觉数据
    /// </summary>
    private class ObstacleVisualData
    {
        public GameObject block;           // 色块
        public TextMesh label;             // 距离标签
        public Renderer blockRenderer;     // 色块渲染器
        public string obstacleId;          // 障碍物ID
        public Vector3 lastPosition;       // 上次位置（用于脏检测）
        public float lastDistance;         // 上次距离（用于脏检测）
    }

    private void Start()
    {
        selfTransform = transform;

        // 查找视角控制器
        if (viewController == null)
            viewController = FindObjectOfType<ViewSwitchController>();

        if (viewController != null)
        {
            currentMode = viewController.CurrentMode;
            viewController.onViewChanged.AddListener(OnViewChanged);
        }

        // 查找障碍物管理器（尝试多种方式）
        FindObstacleManager();

        // 预创建色块池
        InitializeBlockPool();

        isInitialized = true;

        Debug.Log("[ObstacleVisualEnhancer] 初始化完成");
    }

    /// <summary>
    /// 查找障碍物管理器
    /// </summary>
    private void FindObstacleManager()
    {
        // 方式1：直接引用
        if (obstacleManagerRef != null)
        {
            obstacleManager = obstacleManagerRef as ObstacleManager;
            if (obstacleManager != null)
            {
                Debug.Log("[ObstacleVisualEnhancer] 通过引用找到 ObstacleManager");
                return;
            }
        }

        // 方式2：通过类型查找
        obstacleManager = FindObjectOfType<ObstacleManager>();
        if (obstacleManager != null)
        {
            Debug.Log("[ObstacleVisualEnhancer] 通过类型查找找到 ObstacleManager");
            return;
        }

        // 方式3：通过名称查找（兼容蒋金成报告中的命名）
        GameObject mgrObj = GameObject.Find("AdasManager");
        if (mgrObj != null)
        {
            obstacleManager = mgrObj.GetComponent<ObstacleManager>();
            if (obstacleManager != null)
            {
                Debug.Log("[ObstacleVisualEnhancer] 通过名称查找找到 ObstacleManager");
                return;
            }
        }

        Debug.LogWarning("[ObstacleVisualEnhancer] 未找到 ObstacleManager，将在运行时持续尝试");
    }

    /// <summary>
    /// 预创建色块池
    /// </summary>
    private void InitializeBlockPool()
    {
        // 创建父容器
        Transform poolParent = transform.Find("ObstacleVisualPool");
        if (poolParent == null)
        {
            GameObject parentGO = new GameObject("ObstacleVisualPool");
            parentGO.transform.SetParent(transform);
            poolParent = parentGO.transform;
        }

        // 清空旧对象（保留父容器）
        foreach (Transform child in poolParent)
        {
            Destroy(child.gameObject);
        }
        visualDataMap.Clear();

        // 不预先创建，按需创建
        Debug.Log("[ObstacleVisualEnhancer] 色块池已就绪");
    }

    private void Update()
    {
        if (!isInitialized) return;

        // 每帧尝试查找障碍物管理器（如果还没找到）
        if (obstacleManager == null)
        {
            FindObstacleManager();
            return;
        }

        // 更新所有障碍物的视觉表现
        UpdateAllObstacles();
    }

    /// <summary>
    /// 更新所有障碍物视觉
    /// </summary>
    private void UpdateAllObstacles()
    {
        // 获取当前所有障碍物（通过反射获取私有字段，或通过公开方法）
        var obstacles = GetObstaclesFromManager();
        if (obstacles == null) return;

        // 收集当前帧的障碍物ID
        HashSet<string> currentIds = new HashSet<string>();

        foreach (var obstacle in obstacles)
        {
            string id = obstacle.ID;
            currentIds.Add(id);

            // 计算距离（障碍物位置转为世界坐标）
            Vector3 obsWorldPos = new Vector3(
                obstacle.fPos_x * GetScaleX(),
                -3f,  // Y固定，与ObstacleManager一致
                obstacle.fPos_y * GetScaleZ()
            );

            // 改为：只算水平距离（忽略Y轴）
            Vector3 obsXZ = new Vector3(obsWorldPos.x, 0, obsWorldPos.z);
            Vector3 carXZ = new Vector3(selfTransform.position.x, 0, selfTransform.position.z);
            float distance = Vector3.Distance(obsXZ, carXZ);

            // 检查是否需要更新视觉
            if (visualDataMap.TryGetValue(id, out ObstacleVisualData visualData))
            {
                // 更新已有的
                UpdateVisual(visualData, obsWorldPos, distance);
            }
            else
            {
                // 创建新的视觉元素
                CreateVisualForObstacle(id, obsWorldPos, distance);
            }
        }

        // 移除已不存在的障碍物视觉
        List<string> toRemove = visualDataMap.Keys
            .Where(id => !currentIds.Contains(id))
            .ToList();

        foreach (string id in toRemove)
        {
            RemoveVisualForObstacle(id);
        }
    }

    /// <summary>
    /// 从ObstacleManager获取障碍物列表（通过反射）
    /// </summary>
    private List<ObstacleInfo> GetObstaclesFromManager()
    {
        if (obstacleManager == null) return null;

        // 使用反射获取私有字段 curObstacleData
        var field = obstacleManager.GetType().GetField("curObstacleData",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (field != null)
        {
            var data = field.GetValue(obstacleManager) as List<ObstacleInfo>;
            if (data != null) return data;
        }

        // 如果反射失败，尝试获取公开属性或字段
        var prop = obstacleManager.GetType().GetProperty("CurrentObstacles");
        if (prop != null)
        {
            return prop.GetValue(obstacleManager) as List<ObstacleInfo>;
        }

        return null;
    }

    /// <summary>
    /// 获取缩放系数（从ObstacleManager读取）
    /// </summary>
    private float GetScaleX()
    {
        if (obstacleManager == null) return 1f;

        var field = obstacleManager.GetType().GetField("scaleX");
        if (field != null) return (float)field.GetValue(obstacleManager);

        return 1f;
    }

    private float GetScaleZ()
    {
        if (obstacleManager == null) return 1f;

        var field = obstacleManager.GetType().GetField("scaleZ");
        if (field != null) return (float)field.GetValue(obstacleManager);

        return 1f;
    }

    /// <summary>
    /// 为障碍物创建视觉元素
    /// </summary>
    private void CreateVisualForObstacle(string id, Vector3 position, float distance)
    {
        Transform poolParent = transform.Find("ObstacleVisualPool");
        if (poolParent == null) return;

        // 创建色块
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Quad);
        block.name = $"Block_{id}";
        block.transform.SetParent(poolParent);
        block.transform.position = new Vector3(position.x, blockHeight, position.z);
        block.transform.rotation = Quaternion.Euler(90, 0, 0); // 平躺在地上
        block.transform.localScale = new Vector3(blockSize, blockSize, 1);

        // 设置材质颜色
        Renderer renderer = block.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Unlit/Color"));
        renderer.material = mat;

        // 创建距离标签
        TextMesh label = null;
        if (showDistanceLabel)
        {
            GameObject labelGO = new GameObject($"Label_{id}");
            labelGO.transform.SetParent(block.transform);
            labelGO.transform.localPosition = new Vector3(0, 0.5f, 0);
            labelGO.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);

            label = labelGO.AddComponent<TextMesh>();
            label.fontSize = labelFontSize;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = Color.white;
            label.text = $"{distance:F1}m";

            // 让标签始终面向相机（可以用WorldSpace Canvas，但TextMesh简单）
            // 这里不实现Billboard，简化处理
        }

        // 保存数据
        ObstacleVisualData data = new ObstacleVisualData
        {
            block = block,
            label = label,
            blockRenderer = renderer,
            obstacleId = id,
            lastPosition = position,
            lastDistance = distance
        };

        visualDataMap[id] = data;

        // 更新颜色
        UpdateBlockColor(data, distance);

        Debug.Log($"[ObstacleVisualEnhancer] 为障碍物 {id} 创建视觉元素，距离 {distance:F1}m");
    }

    /// <summary>
    /// 更新视觉元素
    /// </summary>
    private void UpdateVisual(ObstacleVisualData data, Vector3 position, float distance)
    {
        // 更新位置（如果有变化）
        if (Vector3.Distance(data.lastPosition, position) > 0.01f)
        {
            data.block.transform.position = new Vector3(position.x, blockHeight, position.z);
            data.lastPosition = position;
        }

        // 更新颜色
        UpdateBlockColor(data, distance);

        // 更新标签
        if (data.label != null && showDistanceLabel)
        {
            if (Mathf.Abs(data.lastDistance - distance) > 0.1f)
            {
                data.label.text = $"{distance:F1}m";
                data.lastDistance = distance;
            }
        }
    }

    /// <summary>
    /// 更新色块颜色（根据距离）
    /// </summary>
    private void UpdateBlockColor(ObstacleVisualData data, float distance)
    {
        // 【调试】打印实时距离
        // Debug.Log($"[颜色调试] 障碍物 {data.obstacleId} 距离: {distance:F2}m");
        Color targetColor;

        // 泊车模式用更严格的阈值
        float safe = currentMode == ViewMode.Park ? safeDistance * 0.8f : safeDistance;
        float warning = currentMode == ViewMode.Park ? warningDistance * 0.8f : warningDistance;
        float danger = currentMode == ViewMode.Park ? dangerDistance * 0.8f : dangerDistance;

        if (distance < danger)
        {
            targetColor = dangerColor;
            // 危险时闪烁
            float blink = Mathf.PingPong(Time.time * 4f, 1f);
            targetColor.a = 0.6f + 0.4f * blink;
        }
        else if (distance < warning)
        {
            targetColor = warningColor;
            // 警示时缓慢呼吸
            float breathe = 0.7f + 0.3f * Mathf.Sin(Time.time * 2f);
            targetColor.a = breathe;
        }
        else if (distance < safe)
        {
            targetColor = safeColor;
            targetColor.a = 0.6f;
        }
        else
        {
            // 超出安全距离：透明（不可见）
            targetColor = Color.white;
            targetColor.a = 0f;
        }

        data.blockRenderer.material.color = targetColor;

        // 新增：检测到危险时上报预警
        if (distance < dangerDistance)
        {
            var ws = FindObjectOfType<WebSocketClient>();
            if (ws != null)
            {
                ws.SendRiskWarning(2, $"障碍物 {data.obstacleId} 距离 {distance:F1}m，请接管");
            }
        }
        else if (distance < warningDistance)
        {
            var ws = FindObjectOfType<WebSocketClient>();
            if (ws != null)
            {
                ws.SendRiskWarning(1, $"障碍物 {data.obstacleId} 距离 {distance:F1}m，请注意");
            }
        }
    }

    /// <summary>
    /// 移除障碍物视觉
    /// </summary>
    private void RemoveVisualForObstacle(string id)
    {
        if (visualDataMap.TryGetValue(id, out ObstacleVisualData data))
        {
            if (data.block != null)
                Destroy(data.block);
            visualDataMap.Remove(id);
            Debug.Log($"[ObstacleVisualEnhancer] 移除障碍物 {id} 的视觉元素");
        }
    }

    /// <summary>
    /// 视角切换联动回调
    /// </summary>
    private void OnViewChanged(ViewMode mode)
    {
        currentMode = mode;
        Debug.Log($"[ObstacleVisualEnhancer] 切换模式: {mode}");

        // 模式切换后强制刷新所有颜色
        foreach (var kvp in visualDataMap)
        {
            // 重新计算距离
            var obstacles = GetObstaclesFromManager();
            if (obstacles != null)
            {
                var obs = obstacles.Find(o => o.ID == kvp.Key);
                if (obs != null)
                {
                    Vector3 worldPos = new Vector3(
                        obs.fPos_x * GetScaleX(),
                        -3f,
                        obs.fPos_y * GetScaleZ()
                    );
                    float dist = Vector3.Distance(worldPos, selfTransform.position);
                    UpdateBlockColor(kvp.Value, dist);
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (viewController != null)
            viewController.onViewChanged.RemoveListener(OnViewChanged);

        // 清理资源
        foreach (var kvp in visualDataMap)
        {
            if (kvp.Value.block != null)
                Destroy(kvp.Value.block);
        }
        visualDataMap.Clear();
    }
}