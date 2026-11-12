using UnityEngine;

/// <summary>
/// 泊车视觉管理 - 车位 + 双轨迹线
/// 挂载到 Car 物体上
/// </summary>
public class ParkingVisualManager : MonoBehaviour
{
    [Header("车位配置")]
    [Tooltip("车位宽度")]
    public float parkingWidth = 2.8f;

    [Tooltip("车位深度")]
    public float parkingDepth = 5.5f;

    [Tooltip("车位颜色（空闲）")]
    public Color parkingColorIdle = new Color(0.2f, 0.8f, 0.2f, 0.3f);

    [Tooltip("车位颜色（占用/警告）")]
    public Color parkingColorWarning = new Color(0.8f, 0.2f, 0.2f, 0.5f);

    [Tooltip("车位距离自车后方的偏移")]
    public float parkingOffsetZ = -4f;

    [Header("倒车轨迹配置")]
    [Tooltip("轨迹线长度")]
    public float trajectoryLength = 5f;

    [Tooltip("轨迹线分段数")]
    public int trajectorySegments = 20;

    [Tooltip("最大转向角度（度）")]
    public float maxSteerAngle = 40f;

    [Tooltip("轨迹线宽度")]
    public float trajectoryLineWidth = 0.06f;

    [Tooltip("车轮轨迹间距比例（0.9 = 比车宽略窄，更真实）")]
    public float trackWidthRatio = 0.9f;

    [Header("联动引用")]
    public ViewSwitchController viewController;

    // 自车宽度（从 BoxCollider 自动提取）
    private float carWidth = 1.8f;
    private float carLength = 4.5f;

    // 组件引用
    private LineRenderer parkingLotRenderer;
    private LineRenderer trajectoryLeftRenderer;
    private LineRenderer trajectoryRightRenderer;

    // 车位角点
    private Vector3[] parkingCorners = new Vector3[5];

    void Start()
    {
        // 从 BoxCollider 提取车辆尺寸
        ExtractCarSize();

        // 查找视角控制器
        if (viewController == null)
            viewController = GetComponent<ViewSwitchController>();

        // 创建车位线
        // CreateParkingLot();

        // 创建双轨迹线
        CreateTrajectories();

        // 初始隐藏
        SetParkingVisualsActive(false);

        Debug.Log($"[ParkingVisual] 车宽: {carWidth:F2}m, 车长: {carLength:F2}m");
    }

    void Update()
    {
        // 检查是否在泊车模式
        if (viewController != null && viewController.CurrentMode == ViewMode.Park)
        {
            SetParkingVisualsActive(true);
            UpdateParkingLot();
            UpdateTrajectories();
        }
        else
        {
            SetParkingVisualsActive(false);
        }
    }

    /// <summary>
    /// 从 BoxCollider 提取车辆尺寸
    /// </summary>
    private void ExtractCarSize()
    {
        BoxCollider boxCollider = GetComponent<BoxCollider>();

        if (boxCollider != null)
        {
            // 获取碰撞体的局部尺寸
            Vector3 localSize = boxCollider.size;

            // 考虑缩放
            Vector3 worldSize = Vector3.Scale(localSize, transform.lossyScale);

            carWidth = worldSize.x;
            carLength = worldSize.z;

            // 如果没找到 BoxCollider，用默认值
            if (carWidth < 0.5f)
                carWidth = 1.8f;

            if (carLength < 1f)
                carLength = 4.5f;
        }
        else
        {
            // 没有 BoxCollider，用默认值
            carWidth = 1.8f;
            carLength = 4.5f;
            Debug.LogWarning("[ParkingVisual] 未找到 BoxCollider，使用默认尺寸");
        }
    }

    /// <summary>
    /// 创建车位线
    /// </summary>
    private void CreateParkingLot()
    {
        GameObject lotGO = new GameObject("ParkingLot");
        lotGO.transform.SetParent(transform);
        lotGO.transform.localPosition = Vector3.zero;

        parkingLotRenderer = lotGO.AddComponent<LineRenderer>();
        parkingLotRenderer.positionCount = 5;
        parkingLotRenderer.loop = true;
        parkingLotRenderer.useWorldSpace = true;
        parkingLotRenderer.startWidth = 0.08f;
        parkingLotRenderer.endWidth = 0.08f;

        Material mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = parkingColorIdle;
        parkingLotRenderer.material = mat;

        parkingLotRenderer.enabled = false;
    }

    /// <summary>
    /// 创建双轨迹线
    /// </summary>
    private void CreateTrajectories()
    {
        // 左轨迹线
        GameObject leftGO = new GameObject("Trajectory_Left");
        leftGO.transform.SetParent(transform);
        leftGO.transform.localPosition = Vector3.zero;

        trajectoryLeftRenderer = leftGO.AddComponent<LineRenderer>();
        trajectoryLeftRenderer.positionCount = trajectorySegments + 1;
        trajectoryLeftRenderer.useWorldSpace = true;
        trajectoryLeftRenderer.startWidth = trajectoryLineWidth;
        trajectoryLeftRenderer.endWidth = trajectoryLineWidth * 0.5f;
        trajectoryLeftRenderer.material = CreateTrajectoryMaterial(Color.yellow);
        trajectoryLeftRenderer.enabled = false;

        // 右轨迹线
        GameObject rightGO = new GameObject("Trajectory_Right");
        rightGO.transform.SetParent(transform);
        rightGO.transform.localPosition = Vector3.zero;

        trajectoryRightRenderer = rightGO.AddComponent<LineRenderer>();
        trajectoryRightRenderer.positionCount = trajectorySegments + 1;
        trajectoryRightRenderer.useWorldSpace = true;
        trajectoryRightRenderer.startWidth = trajectoryLineWidth;
        trajectoryRightRenderer.endWidth = trajectoryLineWidth * 0.5f;
        trajectoryRightRenderer.material = CreateTrajectoryMaterial(Color.yellow);
        trajectoryRightRenderer.enabled = false;
    }

    /// <summary>
    /// 创建轨迹线材质
    /// </summary>
    private Material CreateTrajectoryMaterial(Color color)
    {
        Material mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = color;
        return mat;
    }

    /// <summary>
    /// 设置泊车视觉显隐
    /// </summary>
    private void SetParkingVisualsActive(bool active)
    {
        if (parkingLotRenderer != null)
            parkingLotRenderer.enabled = active;

        if (trajectoryLeftRenderer != null)
            trajectoryLeftRenderer.enabled = active;

        if (trajectoryRightRenderer != null)
            trajectoryRightRenderer.enabled = active;
    }

    /// <summary>
    /// 更新车位位置
    /// </summary>
    private void UpdateParkingLot()
    {
        if (parkingLotRenderer == null) return;

        Vector3 center = transform.position + transform.forward * parkingOffsetZ;

        float halfW = parkingWidth / 2f;
        float halfD = parkingDepth / 2f;

        parkingCorners[0] = center + new Vector3(-halfW, 0.05f, -halfD);
        parkingCorners[1] = center + new Vector3(halfW, 0.05f, -halfD);
        parkingCorners[2] = center + new Vector3(halfW, 0.05f, halfD);
        parkingCorners[3] = center + new Vector3(-halfW, 0.05f, halfD);
        parkingCorners[4] = parkingCorners[0];

        parkingLotRenderer.SetPositions(parkingCorners);

        bool isOccupied = IsParkingSlotOccupied(center);
        parkingLotRenderer.material.color = isOccupied ? parkingColorWarning : parkingColorIdle;
    }

    /// <summary>
    /// 检测车位是否被占用
    /// </summary>
    private bool IsParkingSlotOccupied(Vector3 center)
    {
        float halfW = parkingWidth / 2f;
        float halfD = parkingDepth / 2f;

        Vector3[] checkPoints = new Vector3[]
        {
            center,
            center + new Vector3(-halfW, 0, -halfD),
            center + new Vector3( halfW, 0, -halfD),
            center + new Vector3( halfW, 0,  halfD),
            center + new Vector3(-halfW, 0,  halfD)
        };

        foreach (var point in checkPoints)
        {
            Collider[] hits = Physics.OverlapSphere(point, 0.3f);
            foreach (var hit in hits)
            {
                if (hit.CompareTag("Obstacle"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 更新双轨迹线
    /// </summary>
    private void UpdateTrajectories()
    {
        if (trajectoryLeftRenderer == null || trajectoryRightRenderer == null) return;

        float steerAngle = GetSteerAngle();

        // 计算两条轨迹点
        Vector3[] leftPoints = CalculateTrajectoryPoints(steerAngle, -1);
        Vector3[] rightPoints = CalculateTrajectoryPoints(steerAngle, 1);

        trajectoryLeftRenderer.positionCount = leftPoints.Length;
        trajectoryLeftRenderer.SetPositions(leftPoints);

        trajectoryRightRenderer.positionCount = rightPoints.Length;
        trajectoryRightRenderer.SetPositions(rightPoints);

        // 根据方向盘角度改变颜色
        float angleRatio = Mathf.Abs(steerAngle) / maxSteerAngle;
        Color color = Color.Lerp(Color.green, Color.yellow, angleRatio);

        trajectoryLeftRenderer.material.color = color;
        trajectoryRightRenderer.material.color = color;

        // 方向盘角度大时线变亮
        float alpha = 0.6f + 0.4f * angleRatio;
        Color colorWithAlpha = color;
        colorWithAlpha.a = alpha;
        trajectoryLeftRenderer.material.color = colorWithAlpha;
        trajectoryRightRenderer.material.color = colorWithAlpha;
    }

    /// <summary>
    /// 计算单条轨迹点
    /// </summary>
    /// <param name="steerAngle">方向盘角度（度）</param>
    /// <param name="side">-1=左轮, 1=右轮</param>
    private Vector3[] CalculateTrajectoryPoints(float steerAngle, int side)
    {
        int pointCount = trajectorySegments + 1;
        Vector3[] points = new Vector3[pointCount];

        // ✅ 起点：车尾中心（车辆后方 1.2m）
        Vector3 startPos = transform.position - transform.forward * 1.2f;
        startPos.y = 0.05f;

        float trackWidth = carWidth * trackWidthRatio;
        float halfTrack = trackWidth / 2f;

        // ✅ 起点偏移到左右轮位置（在车辆局部坐标系中）
        Vector3 startOffset = transform.right * side * halfTrack;

        float angleRad = steerAngle * Mathf.Deg2Rad;
        float wheelbase = carLength * 0.6f;

        // 如果转向角很小，走直线
        if (Mathf.Abs(steerAngle) < 0.5f)
        {
            for (int i = 0; i < pointCount; i++)
            {
                float t = i / (float)trajectorySegments;
                float distance = t * trajectoryLength;
                // ✅ 直线方向：车辆后方（-forward）
                points[i] = startPos + startOffset + (-transform.forward) * distance;
            }
            return points;
        }

        // 转弯半径
        float radius = wheelbase / Mathf.Tan(Mathf.Clamp(Mathf.Abs(angleRad), 0.001f, 10f));
        float direction = Mathf.Sign(steerAngle);

        // ✅ 圆心位置：在车辆侧后方
        Vector3 center = startPos + transform.right * direction * radius;

        // ✅ 起始角度：从车辆后方方向开始
        // 车辆后方方向 = -transform.forward
        // 在 XZ 平面上，这个方向的角度 = transform.eulerAngles.y + 180°
        float startAngle = (transform.eulerAngles.y + 180f) * Mathf.Deg2Rad;

        // 如果是右转，需要调整起始角度
        if (direction < 0)
        {
            startAngle = (transform.eulerAngles.y - 90f) * Mathf.Deg2Rad;
        }
        else
        {
            startAngle = (transform.eulerAngles.y + 90f) * Mathf.Deg2Rad;
        }

        // 圆弧角度范围
        float arcAngle = trajectoryLength / radius;

        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)trajectorySegments;
            float angle = startAngle + direction * t * arcAngle;

            float x = center.x + radius * Mathf.Cos(angle);
            float z = center.z + radius * Mathf.Sin(angle);

            points[i] = new Vector3(x, 0.05f, z);
        }

        return points;
    }

    /// <summary>
    /// 获取方向盘角度（-maxSteerAngle ~ maxSteerAngle）
    /// </summary>
    private float GetSteerAngle()
    {
        // 从 CarWASDControl 获取水平输入（和转向方向一致）
        CarWASDControl carControl = GetComponent<CarWASDControl>();
        if (carControl != null)
        {
            // 读取当前水平输入值（-1 ~ 1）
            float horizontal = Input.GetAxis("Horizontal");
            return Mathf.Clamp(horizontal * maxSteerAngle, -maxSteerAngle, maxSteerAngle);
        }
        return 0f;
    }

    /// <summary>
    /// 外部调用：设置车位位置偏移
    /// </summary>
    public void UpdateParkingPosition(float offsetZ)
    {
        parkingOffsetZ = offsetZ;
    }

    /// <summary>
    /// 外部调用：获取车辆宽度
    /// </summary>
    public float GetCarWidth()
    {
        return carWidth;
    }
}