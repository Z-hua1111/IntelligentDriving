using UnityEngine;

/// <summary>
/// 自动泊车控制器 - 直接插值 + 角度对齐
/// 挂载到 Car 物体上
/// </summary>
public class AutoParkingController : MonoBehaviour
{
    [Header("车位目标")]
    [Tooltip("车位中心标记（场景中放置）")]
    public Transform parkingTarget;

    [Tooltip("车位宽度（米）")]
    public float parkingWidth = 2.8f;

    [Tooltip("车位深度（米）")]
    public float parkingDepth = 5.5f;

    [Header("泊车参数")]
    [Tooltip("倒车速度（km/h）")]
    public float parkingSpeed = 10f;

    [Tooltip("到达判定距离（米）")]
    public float arrivalDistance = 0.3f;

    [Tooltip("到达判定角度（度）")]
    public float arrivalAngle = 5f;

    [Header("车辆尺寸（自动从 BoxCollider 提取）")]
    public float carWidth = 1.8f;
    public float carLength = 4.5f;

    [Header("状态")]
    public bool isParking = false;
    public bool isComplete = false;
    public float progress = 0f;

    // 路径点
    private Vector3[] pathPoints;
    private int currentPointIndex = 0;

    // 起点/终点
    private Vector3 startPosition;
    private Quaternion startRotation;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool hasStartPosition = false;

    // 组件引用
    private Rigidbody rb;

    public System.Action OnParkingComplete;
    public System.Action<string> OnParkingError;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        ExtractCarSize();

        startPosition = transform.position;
        startRotation = transform.rotation;
        hasStartPosition = true;

        Debug.Log($"[AutoParking] 初始化完成，车宽:{carWidth:F2}m，车长:{carLength:F2}m");
    }

    void Update()
    {
        if (!isParking || isComplete) return;

        MoveAlongPath();

        if (CheckArrival())
        {
            CompleteParking();
        }
    }

    /// <summary>
    /// 开始泊车
    /// </summary>
    public void StartParking(float speed)
    {
        if (parkingTarget == null)
        {
            OnParkingError?.Invoke("未设置车位目标");
            return;
        }

        parkingSpeed = Mathf.Max(speed, 5f);

        startPosition = transform.position;
        startRotation = transform.rotation;
        hasStartPosition = true;

        targetPosition = parkingTarget.position;
        targetPosition.y = 0.05f;
        targetRotation = parkingTarget.rotation;

        pathPoints = GeneratePath();

        if (pathPoints == null || pathPoints.Length < 3)
        {
            OnParkingError?.Invoke("无法生成泊车路径，请检查车位位置");
            return;
        }

        currentPointIndex = 0;
        isParking = true;
        isComplete = false;
        progress = 0f;

        Debug.Log($"[AutoParking] 开始泊车，速度:{parkingSpeed}km/h，路径点:{pathPoints.Length}");
    }

    /// <summary>
    /// 停止泊车
    /// </summary>
    public void StopParking()
    {
        isParking = false;
        if (rb != null)
            rb.velocity = Vector3.zero;
        Debug.Log("[AutoParking] 泊车已停止");
    }

    /// <summary>
    /// 重置泊车状态
    /// </summary>
    public void ResetParking()
    {
        isParking = false;
        isComplete = false;
        progress = 0f;

        if (rb != null)
            rb.velocity = Vector3.zero;

        pathPoints = null;
        currentPointIndex = 0;

        if (hasStartPosition)
        {
            transform.position = startPosition;
            transform.rotation = startRotation;
        }

        Debug.Log("[AutoParking] 重置泊车状态");
    }

    /// <summary>
    /// 从 BoxCollider 提取车辆尺寸
    /// </summary>
    private void ExtractCarSize()
    {
        BoxCollider boxCollider = GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            Vector3 worldSize = Vector3.Scale(boxCollider.size, transform.lossyScale);
            carWidth = Mathf.Max(worldSize.x, 1.5f);
            carLength = Mathf.Max(worldSize.z, 4f);
        }
    }

    /// <summary>
    /// 生成泊车路径（直接插值 + 角度对齐）
    /// </summary>
    private Vector3[] GeneratePath()
    {
        int pointCount = 35;
        Vector3[] points = new Vector3[pointCount];

        Vector3 startPos = transform.position - transform.forward * carLength * 0.5f;
        startPos.y = 0.05f;

        Vector3 endPos = parkingTarget.position;
        endPos.y = 0.05f;

        float distance = Vector3.Distance(startPos, endPos);

        // 如果已经很近，直接直线过去
        if (distance < 0.8f)
        {
            for (int i = 0; i < pointCount; i++)
            {
                float t = i / (float)(pointCount - 1);
                points[i] = Vector3.Lerp(startPos, endPos, t);
            }
            return points;
        }

        // 计算方向
        Vector3 dirToTarget = (endPos - startPos).normalized;
        Vector3 rightDir = Vector3.Cross(Vector3.up, dirToTarget).normalized;

        // 判断车辆在车位哪一侧
        Vector3 toCar = (transform.position - parkingTarget.position);
        float sideOffset = Vector3.Dot(toCar, rightDir);

        // 弯曲方向和幅度
        float bendDir = -Mathf.Sign(sideOffset);
        if (Mathf.Abs(sideOffset) < 0.3f) bendDir = 1f;

        float bendAmount = Mathf.Clamp(distance * 0.15f, 0.5f, 2.5f);

        // 控制点：向侧面偏移
        Vector3 controlOffset = rightDir * bendDir * bendAmount + dirToTarget * (distance * 0.2f);
        Vector3 controlPoint = (startPos + endPos) / 2f + controlOffset;

        // 生成贝塞尔曲线
        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            float u = 1f - t;

            // 二次贝塞尔
            points[i] = u * u * startPos + 2f * u * t * controlPoint + t * t * endPos;
            points[i].y = 0.05f;
        }

        return points;
    }

    /// <summary>
    /// 沿路径移动（位置插值 + 角度对齐）
    /// </summary>
    private void MoveAlongPath()
    {
        if (pathPoints == null || currentPointIndex >= pathPoints.Length)
            return;

        float speedMPS = parkingSpeed / 3.6f;
        float step = speedMPS * Time.deltaTime;

        Vector3 targetPoint = pathPoints[currentPointIndex];

        // ✅ 位置：直接 MoveTowards
        transform.position = Vector3.MoveTowards(transform.position, targetPoint, step);

        // ✅ 角度：朝向路径方向（倒车时车头朝反方向）
        if (currentPointIndex + 1 < pathPoints.Length)
        {
            Vector3 nextPoint = pathPoints[currentPointIndex + 1];
            Vector3 moveDir = (nextPoint - transform.position).normalized;
            moveDir.y = 0;

            if (moveDir.magnitude > 0.01f)
            {
                // 倒车：车头朝反方向
                Vector3 carForward = -moveDir;
                Quaternion targetRot = Quaternion.LookRotation(carForward);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 6f);
            }
        }

        // 到达当前路径点，进入下一个
        if (Vector3.Distance(transform.position, targetPoint) < 0.05f)
        {
            currentPointIndex++;
        }

        // 更新进度
        float totalDistance = Vector3.Distance(startPosition, targetPosition);
        float traveled = Vector3.Distance(startPosition, transform.position);
        progress = totalDistance > 0 ? Mathf.Clamp01(traveled / totalDistance) : 0f;
    }

    /// <summary>
    /// 检测是否到达车位
    /// </summary>
    private bool CheckArrival()
    {
        if (parkingTarget == null) return false;

        float distance = Vector3.Distance(transform.position, targetPosition);
        float angleDiff = Quaternion.Angle(transform.rotation, targetRotation);

        // 到达条件：距离够近 + 角度够正 + 车辆在车位内
        if (distance < arrivalDistance && angleDiff < arrivalAngle && IsCarInsideParking())
        {
            return true;
        }

        // 微调角度（靠近时自动修正方向）
        if (distance < 0.8f && angleDiff > arrivalAngle)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 4f);
        }

        return false;
    }

    /// <summary>
    /// 检测车辆是否全包裹在车位内
    /// </summary>
    private bool IsCarInsideParking()
    {
        float halfW = carWidth * 0.45f;
        float halfL = carLength * 0.45f;

        Vector3[] carCorners = new Vector3[]
        {
            transform.position + transform.right * halfW + transform.forward * halfL,
            transform.position - transform.right * halfW + transform.forward * halfL,
            transform.position + transform.right * halfW - transform.forward * halfL,
            transform.position - transform.right * halfW - transform.forward * halfL
        };

        float halfPW = parkingWidth * 0.45f;
        float halfPD = parkingDepth * 0.45f;

        foreach (var corner in carCorners)
        {
            Vector3 localPos = parkingTarget.InverseTransformPoint(corner);
            if (Mathf.Abs(localPos.x) > halfPW || Mathf.Abs(localPos.z) > halfPD)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 完成泊车
    /// </summary>
    private void CompleteParking()
    {
        isParking = false;
        isComplete = true;
        progress = 1f;

        if (rb != null)
            rb.velocity = Vector3.zero;

        // 精确定位到目标
        transform.position = targetPosition;
        transform.rotation = targetRotation;

        Debug.Log("[AutoParking] 🎉 泊车完成！");
        OnParkingComplete?.Invoke();
    }

    /// <summary>
    /// 可视化路径
    /// </summary>
    void OnDrawGizmosSelected()
    {
        if (pathPoints != null)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < pathPoints.Length - 1; i++)
            {
                Gizmos.DrawLine(pathPoints[i], pathPoints[i + 1]);
                Gizmos.DrawWireSphere(pathPoints[i], 0.1f);
            }
        }

        if (parkingTarget != null)
        {
            Vector3 center = parkingTarget.position;
            center.y = 0.05f;
            Vector3 size = new Vector3(parkingWidth, 0.1f, parkingDepth);

            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Gizmos.DrawCube(center, size);
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(center, size);
        }
    }
}