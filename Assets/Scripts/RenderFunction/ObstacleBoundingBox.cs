using UnityEngine;

/// <summary>
/// 障碍物检测框 - 在障碍物周围画彩色矩形框
/// 挂载到障碍物预制体上，或由 ObstacleManager 动态创建
/// </summary>
public class ObstacleBoundingBox : MonoBehaviour
{
    [Header("框的尺寸")]
    [Tooltip("框的宽度（X轴）")]
    public float boxWidth = 1.2f;

    [Tooltip("框的高度（Y轴）")]
    public float boxHeight = 1.8f;

    [Tooltip("框的深度（Z轴）")]
    public float boxDepth = 1.2f;

    [Header("颜色配置")]
    [Tooltip("安全距离（米）")]
    public float safeDistance = 5f;

    [Tooltip("危险距离（米）")]
    public float dangerDistance = 2f;

    [Tooltip("安全颜色")]
    public Color safeColor = Color.green;

    [Tooltip("警示颜色")]
    public Color warningColor = Color.yellow;

    [Tooltip("危险颜色")]
    public Color dangerColor = Color.red;

    [Header("连线配置")]
    [Tooltip("线宽")]
    public float lineWidth = 0.05f;

    [Header("引用")]
    [Tooltip("自车Transform（自动查找）")]
    public Transform carTransform;

    // LineRenderer 组件
    private LineRenderer lineRenderer;

    // 矩形框的8个顶点
    private Vector3[] corners = new Vector3[8];

    // 顶点索引顺序（画一个矩形框需要12条线段）
    private int[] lineIndices = new int[]
    {
        0, 1, 1, 2, 2, 3, 3, 0,  // 底面
        4, 5, 5, 6, 6, 7, 7, 4,  // 顶面
        0, 4, 1, 5, 2, 6, 3, 7   // 垂直边
    };

    void Start()
    {
        // 自动查找自车
        // ✅ 用 FindObjectOfType 更可靠
        if (carTransform == null)
        {
            // 方式1：通过类型查找
            CarWASDControl carControl = FindObjectOfType<CarWASDControl>();
            if (carControl != null)
                carTransform = carControl.transform;
        }
        if (carTransform == null)
        {
            GameObject car = GameObject.Find("Car");
            if (car != null)
                carTransform = car.transform;
        }

        // 创建 LineRenderer
        SetupLineRenderer();

        // 更新框的位置和颜色
        UpdateBox();
    }

    void Update()
    {
        UpdateBox();
    }

    /// <summary>
    /// 初始化 LineRenderer
    /// </summary>
    private void SetupLineRenderer()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
            lineRenderer = gameObject.AddComponent<LineRenderer>();

        lineRenderer.positionCount = lineIndices.Length;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.useWorldSpace = true;

        // 使用内置的 Unlit/Color 材质
        Material mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = safeColor;
        lineRenderer.material = mat;
    }

    /// <summary>
    /// 更新检测框
    /// </summary>
    private void UpdateBox()
    {
        if (lineRenderer == null) return;

        // 计算8个顶点位置（世界坐标）
        Vector3 center = transform.position;

        // 根据障碍物尺寸调整框的大小
        // 如果障碍物有 Renderer，自动适配大小
        float w = boxWidth;
        float h = boxHeight;
        float d = boxDepth;
        AdaptToRenderer(ref w, ref h, ref d);

        // 8个顶点：底面4个 + 顶面4个
        corners[0] = center + new Vector3(-w / 2, -h / 2, -d / 2);  // 底面
        corners[1] = center + new Vector3(w / 2, -h / 2, -d / 2);
        corners[2] = center + new Vector3(w / 2, -h / 2, d / 2);
        corners[3] = center + new Vector3(-w / 2, -h / 2, d / 2);
        corners[4] = center + new Vector3(-w / 2, h / 2, -d / 2);  // 顶面
        corners[5] = center + new Vector3(w / 2, h / 2, -d / 2);
        corners[6] = center + new Vector3(w / 2, h / 2, d / 2);
        corners[7] = center + new Vector3(-w / 2, h / 2, d / 2);

        // 设置 LineRenderer 顶点
        for (int i = 0; i < lineIndices.Length; i++)
        {
            lineRenderer.SetPosition(i, corners[lineIndices[i]]);
        }

        // 更新颜色（根据距离）
        UpdateColor();
    }

    /// <summary>
    /// 根据障碍物尺寸自适应调整框大小
    /// </summary>
    private void AdaptToRenderer(ref float w, ref float h, ref float d)
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            Bounds bounds = renderer.bounds;
            w = bounds.size.x * 1.2f;   // 放大一点，包住模型
            h = bounds.size.y * 1.2f;
            d = bounds.size.z * 1.2f;
        }
    }

    /// <summary>
    /// 更新框的颜色（根据距离）
    /// </summary>
    private void UpdateColor()
    {
        if (carTransform == null)
        {
            Debug.LogWarning("[ObstacleBoundingBox] carTransform 为空，无法计算距离");
            return;
        }
        if (carTransform == null || lineRenderer == null) return;

        // 计算距离（忽略Y轴）
        Vector3 carXZ = new Vector3(carTransform.position.x, 0, carTransform.position.z);
        Vector3 obsXZ = new Vector3(transform.position.x, 0, transform.position.z);
        float distance = Vector3.Distance(carXZ, obsXZ);

        // 计算颜色
        Color color;
        if (distance > safeDistance)
        {
            color = safeColor;
            color.a = 0.6f;
        }
        else if (distance > dangerDistance)
        {
            // 警示区：从绿色渐变到黄色
            float t = (distance - dangerDistance) / (safeDistance - dangerDistance);
            color = Color.Lerp(warningColor, safeColor, t);
            color.a = 0.8f;
        }
        else
        {
            // 危险区：红色闪烁
            float blink = Mathf.PingPong(Time.time * 5f, 1f);
            color = dangerColor;
            color.a = 0.6f + 0.4f * blink;
        }

        // 应用颜色
        lineRenderer.material.color = color;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
    }

    /// <summary>
    /// 外部调用：更新框的尺寸
    /// </summary>
    public void SetBoxSize(float width, float height, float depth)
    {
        boxWidth = width;
        boxHeight = height;
        boxDepth = depth;
    }

    /// <summary>
    /// 外部调用：设置颜色阈值
    /// </summary>
    public void SetColorThresholds(float safeDist, float dangerDist)
    {
        safeDistance = safeDist;
        dangerDistance = dangerDist;
    }
}