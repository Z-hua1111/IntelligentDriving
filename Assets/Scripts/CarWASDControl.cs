using UnityEngine;

/// <summary>
/// 车辆控制器 - 支持键盘输入和 WebSocket 外部速度驱动
/// 挂载到车辆父物体
/// </summary>
public class CarWASDControl : MonoBehaviour
{
    [Header("移动参数")]
    [Tooltip("前进后退速度（m/s，键盘模式用）")]
    public float moveSpeed = 5f;
    [Tooltip("转向速度（度/秒，键盘模式用）")]
    public float turnSpeed = 90f;

    [Header("外部速度模式（WebSocket 驱动）")]
    [Tooltip("是否使用外部速度控制，启用后忽略键盘输入")]
    public bool useExternalSpeed = false;

    [Tooltip("外部目标速度（km/h），由 WebSocket 设置")]
    public float externalTargetSpeedKmh = 0f;

    [Tooltip("当前实际速度（km/h），平滑过渡中")]
    public float currentSpeedKmh = 0f;

    [Tooltip("速度平滑加速度（km/h per second）")]
    public float acceleration = 30f;

    [Tooltip("当前挡位（D/R/P/N）")]
    public string gear = "P";

    [Header("外部转向（WebSocket 驱动）")]
    [Tooltip("外部转向输入：-1=左满舵，0=直行，1=右满舵")]
    [Range(-1f, 1f)]
    public float externalSteer = 0f;

    [Tooltip("外部转向速度（度/秒）")]
    public float externalTurnSpeed = 45f;

    [Tooltip("低速下转向更灵敏的系数曲线（速度为 0 时也能转向）")]
    public float minTurnSpeedFactor = 0.3f;

    /// <summary>
    /// 还未接入websocket进行读取处理，用于决定是够绘制倒车线，接入后删掉这条标记
    /// </summary>
    public bool DrawLines=true;

    public ParkCarLine carLine;


    void Update()
    {
        if (useExternalSpeed)
        {
            UpdateExternalDrive();
        }
        else
        {
            UpdateKeyboardDrive();
        }
        if(!DrawLines)
        {
            carLine.DisDraw();
        }
    }

    /// <summary>
    /// 外部速度驱动模式（WebSocket 数据）
    /// </summary>
    private void UpdateExternalDrive()
    {
        // 平滑过渡到目标速度
        float diff = externalTargetSpeedKmh - currentSpeedKmh;
        float maxDelta = acceleration * Time.deltaTime;
        if (Mathf.Abs(diff) <= maxDelta)
            currentSpeedKmh = externalTargetSpeedKmh;
        else
            currentSpeedKmh += Mathf.Sign(diff) * maxDelta;

        // km/h -> m/s
        float speedMPS = currentSpeedKmh / 3.6f;

        // 向前移动（速度为正则前进，为负则倒车）
        transform.Translate(Vector3.forward * speedMPS * Time.deltaTime, Space.Self);

        // 转向：速度越快转向越明显，低速保留最小转向能力
        float speedFactor = Mathf.Clamp01(Mathf.Abs(currentSpeedKmh) / 30f);
        float turnFactor = Mathf.Max(speedFactor, minTurnSpeedFactor);
        float turnAmount = externalSteer * externalTurnSpeed * turnFactor * Time.deltaTime;
        if (Mathf.Abs(turnAmount) > 0.001f)
        {
            transform.Rotate(Vector3.up, turnAmount, Space.Self);
        }
        if (DrawLines)
        {
            carLine.DrawLine(transform,turnAmount);
        }
    }

    /// <summary>
    /// 键盘输入驱动模式（WASD）
    /// </summary>
    private void UpdateKeyboardDrive()
    {
        // 获取输入
        float vertical = Input.GetAxis("Vertical");   // W/S：前后
        float horizontal = Input.GetAxis("Horizontal");// A/D：左右转向

        // 前后移动：沿着自身forward方向
        transform.Translate(Vector3.forward * vertical * moveSpeed * Time.deltaTime, Space.Self);

        // 左右转向：绕Y轴旋转
        transform.Rotate(Vector3.up * horizontal * turnSpeed * Time.deltaTime);

        if (DrawLines)
        {
            carLine.DrawLine(transform,turnSpeed* horizontal);
        }
        // 同步 currentSpeedKmh（方便 GetCurrentSpeedKmh 读取）
        currentSpeedKmh = vertical * moveSpeed * 3.6f;
    }

    /// <summary>
    /// 设置目标车速（km/h）并启用外部模式
    /// </summary>
    public void SetSpeed(float kmh)
    {
        useExternalSpeed = true;
        externalTargetSpeedKmh = Mathf.Clamp(kmh, -60f, 260f);
    }

    /// <summary>
    /// 设置转向（-1=左满舵，0=回正，1=右满舵）
    /// 自动启用外部模式
    /// </summary>
    public void SetSteer(float steer)
    {
        useExternalSpeed = true;
        externalSteer = Mathf.Clamp(steer, -1f, 1f);
    }

    /// <summary>
    /// 重置转向（回正）
    /// </summary>
    public void ResetSteer()
    {
        externalSteer = 0f;
    }

    /// <summary>
    /// 设置挡位（影响速度方向）
    /// D=前进，R=倒车，P/N=停车
    /// </summary>
    public void SetGear(string g)
    {
        gear = g?.ToUpper() ?? "P";
        switch (gear)
        {
            case "D":
                // 前进档：确保目标速度为正
                if (externalTargetSpeedKmh < 0)
                    externalTargetSpeedKmh = Mathf.Abs(externalTargetSpeedKmh);
                break;
            case "R":
                // 倒车档：速度取负
                if (externalTargetSpeedKmh >= 0)
                    externalTargetSpeedKmh = -Mathf.Max(Mathf.Abs(externalTargetSpeedKmh), 5f);
                break;
            case "P":
            case "N":
                externalTargetSpeedKmh = 0f;
                break;
        }
    }

    /// <summary>
    /// 获取当前实际速度（km/h，带方向）
    /// </summary>
    public float GetCurrentSpeedKmh()
    {
        return currentSpeedKmh;
    }

    /// <summary>
    /// 重置速度（停车）
    /// </summary>
    public void ResetSpeed()
    {
        externalTargetSpeedKmh = 0f;
        currentSpeedKmh = 0f;
    }
}
