using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.Networking;

/// <summary>
/// 自动障碍物请求器 - Unity 主动向 Python 请求障碍物数据
/// 模拟网页点击"障碍物位移"按钮，实现自动持续位移效果
/// </summary>
public class AutoObstacleRequester : MonoBehaviour
{
    [Header("请求配置")]
    [Tooltip("基础请求间隔（秒）")]
    public float baseInterval = 0.4f;

    [Tooltip("最小请求间隔（秒）")]
    public float minInterval = 0.12f;

    [Tooltip("最大请求间隔（秒）")]
    public float maxInterval = 0.8f;

    [Tooltip("Python 服务器地址")]
    public string pythonUrl = "http://127.0.0.1:12017/simu";

    [Header("功能开关")]
    [Tooltip("是否启用障碍物自动请求")]
    public bool enableObstacle = true;

    [Tooltip("是否启用车道线自动请求")]
    public bool enableLane = true;

    [Header("UI 按钮（Unity 本地调试用）")]
    public Button toggleButton;
    public Text statusText;

    [Header("调试")]
    public bool showDebugLog = false;

    // 组件引用
    private ViewSwitchController viewController;
    private ObstacleManager obstacleManager;
    private LaneLineManager laneLineManager;

    // 状态
    private bool isRunning = false;
    private Coroutine requestCoroutine;
    private int currentObstacleIndex = 0;
    private int currentLaneIndex = 0;

    // 当前实际请求间隔
    private float currentInterval = 0.4f;
    private float currentSpeed = 0f;

    // ✅ 障碍物数据数组（从网页 StartBarrier() 中提取）
    private string[] obstacleDataArray = new string[]
    {
        "[{\"ID\":\"1\",\"eType\":\"1\",\"bMovingStatus\":\"false\",\"fPos_x\":\"0.0\",\"fPos_y\":\"20.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"2\",\"eType\":\"2\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"20.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"3\",\"eType\":\"4\",\"bMovingStatus\":\"false\",\"fPos_x\":\"-3.5\",\"fPos_y\":\"12.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"4\",\"eType\":\"5\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"9.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"}]",
        "[{\"ID\":\"1\",\"eType\":\"1\",\"bMovingStatus\":\"false\",\"fPos_x\":\"0.1\",\"fPos_y\":\"19.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"2\",\"eType\":\"2\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"19.5\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"3\",\"eType\":\"4\",\"bMovingStatus\":\"false\",\"fPos_x\":\"-3.5\",\"fPos_y\":\"13.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"4\",\"eType\":\"5\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"8.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"}]",
        "[{\"ID\":\"1\",\"eType\":\"1\",\"bMovingStatus\":\"false\",\"fPos_x\":\"0.2\",\"fPos_y\":\"19.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"2\",\"eType\":\"2\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"19.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"3\",\"eType\":\"4\",\"bMovingStatus\":\"false\",\"fPos_x\":\"-3.5\",\"fPos_y\":\"14.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"4\",\"eType\":\"5\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"7.5\",\"fAngle\":\"0.0\",\"eColor\":\"2\"}]",
        "[{\"ID\":\"1\",\"eType\":\"1\",\"bMovingStatus\":\"false\",\"fPos_x\":\"0.3\",\"fPos_y\":\"18.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"2\",\"eType\":\"2\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"18.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"3\",\"eType\":\"4\",\"bMovingStatus\":\"false\",\"fPos_x\":\"-3.5\",\"fPos_y\":\"15.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"4\",\"eType\":\"5\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"7.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"}]",
        "[{\"ID\":\"1\",\"eType\":\"1\",\"bMovingStatus\":\"false\",\"fPos_x\":\"0.2\",\"fPos_y\":\"18.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"2\",\"eType\":\"2\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"17.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"3\",\"eType\":\"4\",\"bMovingStatus\":\"false\",\"fPos_x\":\"-3.5\",\"fPos_y\":\"18.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"4\",\"eType\":\"5\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"6.5\",\"fAngle\":\"0.0\",\"eColor\":\"2\"}]",
        "[{\"ID\":\"1\",\"eType\":\"1\",\"bMovingStatus\":\"false\",\"fPos_x\":\"0.1\",\"fPos_y\":\"17.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"2\",\"eType\":\"2\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"16.5\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"3\",\"eType\":\"4\",\"bMovingStatus\":\"false\",\"fPos_x\":\"-3.5\",\"fPos_y\":\"19.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"4\",\"eType\":\"5\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"6.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"}]",
        "[{\"ID\":\"1\",\"eType\":\"1\",\"bMovingStatus\":\"false\",\"fPos_x\":\"0.0\",\"fPos_y\":\"15.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"2\",\"eType\":\"2\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"13.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"3\",\"eType\":\"4\",\"bMovingStatus\":\"false\",\"fPos_x\":\"-3.5\",\"fPos_y\":\"20.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"4\",\"eType\":\"5\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"5.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"}]",
        "[{\"ID\":\"1\",\"eType\":\"1\",\"bMovingStatus\":\"false\",\"fPos_x\":\"0.0\",\"fPos_y\":\"14.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"2\",\"eType\":\"2\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"12.5\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"3\",\"eType\":\"4\",\"bMovingStatus\":\"false\",\"fPos_x\":\"-3.5\",\"fPos_y\":\"22.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"},{\"ID\":\"4\",\"eType\":\"5\",\"bMovingStatus\":\"false\",\"fPos_x\":\"3.5\",\"fPos_y\":\"4.0\",\"fAngle\":\"0.0\",\"eColor\":\"2\"}]"
    };

    // ✅ 车道线数据数组（从网页 StartLane() 中提取）
    private string[] laneDataArray = new string[]
    {
        "[{\"ePosIndex\":\"1\",\"eType\":\"2\",\"eColor\":\"1\",\"fLineC0\":\"-1.75\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"2\",\"eType\":\"2\",\"eColor\":\"1\",\"fLineC0\":\"1.75\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"3\",\"eType\":\"1\",\"eColor\":\"1\",\"fLineC0\":\"-5.25\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"4\",\"eType\":\"1\",\"eColor\":\"1\",\"fLineC0\":\"5.25\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"}]",
        "[{\"ePosIndex\":\"1\",\"eType\":\"2\",\"eColor\":\"1\",\"fLineC0\":\"-1.55\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"2\",\"eType\":\"2\",\"eColor\":\"1\",\"fLineC0\":\"1.95\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"3\",\"eType\":\"1\",\"eColor\":\"1\",\"fLineC0\":\"-5.05\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"4\",\"eType\":\"1\",\"eColor\":\"1\",\"fLineC0\":\"5.45\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"}]",
        "[{\"ePosIndex\":\"1\",\"eType\":\"2\",\"eColor\":\"1\",\"fLineC0\":\"-1.35\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"2\",\"eType\":\"2\",\"eColor\":\"1\",\"fLineC0\":\"2.15\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"3\",\"eType\":\"1\",\"eColor\":\"1\",\"fLineC0\":\"-4.85\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"4\",\"eType\":\"1\",\"eColor\":\"1\",\"fLineC0\":\"5.65\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"}]",
        "[{\"ePosIndex\":\"1\",\"eType\":\"2\",\"eColor\":\"1\",\"fLineC0\":\"-1.15\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"2\",\"eType\":\"2\",\"eColor\":\"1\",\"fLineC0\":\"2.35\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"3\",\"eType\":\"1\",\"eColor\":\"1\",\"fLineC0\":\"-4.65\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"4\",\"eType\":\"1\",\"eColor\":\"1\",\"fLineC0\":\"5.85\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"}]",
        "[{\"ePosIndex\":\"1\",\"eType\":\"2\",\"eColor\":\"1\",\"fLineC0\":\"-0.95\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"2\",\"eType\":\"2\",\"eColor\":\"1\",\"fLineC0\":\"2.55\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"3\",\"eType\":\"1\",\"eColor\":\"1\",\"fLineC0\":\"-4.45\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"},{\"ePosIndex\":\"4\",\"eType\":\"1\",\"eColor\":\"1\",\"fLineC0\":\"6.05\",\"fLineC1\":\"0.0\",\"fLineC2\":\"0.0\",\"fLineC3\":\"0.0\",\"fStartX\":\"0.0\",\"fEndX\":\"5.0\"}]"
    };

    void Start()
    {
        // 查找引用
        viewController = FindObjectOfType<ViewSwitchController>();
        obstacleManager = FindObjectOfType<ObstacleManager>();
        laneLineManager = FindObjectOfType<LaneLineManager>();

        // 绑定 UI 按钮
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(ToggleRequesting);
        }

        currentInterval = baseInterval;
        UpdateUI();

        Debug.Log("[AutoObstacleRequester] 初始化完成，按 K 键切换开关");
    }

    void Update()
    {
        // 键盘快捷键：K 键切换开关
        if (Input.GetKeyDown(KeyCode.K))
        {
            ToggleRequesting();
        }

        // 如果正在运行，检测是否还在行车模式
        if (isRunning && viewController != null)
        {
            if (viewController.CurrentMode != ViewMode.Drive)
            {
                StopRequesting();
                Debug.Log("[AutoObstacleRequester] 退出行车模式，自动停止发送");
            }
        }
    }

    /// <summary>
    /// 切换开关
    /// </summary>
    public void ToggleRequesting()
    {
        if (viewController != null && viewController.CurrentMode != ViewMode.Drive)
        {
            Debug.LogWarning("[AutoObstacleRequester] 当前不在行车模式，无法启动");
            UpdateUI();
            return;
        }

        if (isRunning)
        {
            StopRequesting();
        }
        else
        {
            StartRequesting();
        }
    }

    /// <summary>
    /// 开始请求
    /// </summary>
    public void StartRequesting()
    {
        if (isRunning) return;

        if (viewController != null && viewController.CurrentMode != ViewMode.Drive)
        {
            Debug.LogWarning("[AutoObstacleRequester] 当前不在行车模式，无法启动");
            UpdateUI();
            return;
        }

        isRunning = true;
        currentObstacleIndex = 0;
        currentLaneIndex = 0;

        if (requestCoroutine != null)
            StopCoroutine(requestCoroutine);

        requestCoroutine = StartCoroutine(RequestLoop());

        Debug.Log("[AutoObstacleRequester] ✅ 开始自动请求（障碍物 + 车道线）");
        UpdateUI();
    }

    /// <summary>
    /// 停止请求
    /// </summary>
    public void StopRequesting()
    {
        if (!isRunning) return;

        isRunning = false;

        if (requestCoroutine != null)
        {
            StopCoroutine(requestCoroutine);
            requestCoroutine = null;
        }

        Debug.Log("[AutoObstacleRequester] ⏹ 停止自动请求");
        UpdateUI();
    }

    /// <summary>
    /// 更新当前车速（由外部调用）
    /// </summary>
    public void UpdateSpeed(float speed)
    {
        currentSpeed = speed;
        UpdateIntervalBySpeed();
    }

    /// <summary>
    /// 根据车速计算请求间隔
    /// </summary>
    private void UpdateIntervalBySpeed()
    {
        // 速度映射到间隔：速度越快，间隔越小
        // 0 km/h → maxInterval (0.8s)
        // 120 km/h → minInterval (0.12s)
        float t = Mathf.Clamp01(currentSpeed / 120f);
        currentInterval = Mathf.Lerp(maxInterval, minInterval, t);
        currentInterval = Mathf.Clamp(currentInterval, minInterval, maxInterval);

        if (showDebugLog && isRunning)
        {
            Debug.Log($"[AutoObstacleRequester] 车速: {currentSpeed}km/h, 间隔: {currentInterval:F2}s");
        }
    }

    /// <summary>
    /// 请求循环协程
    /// </summary>
    IEnumerator RequestLoop()
    {
        while (isRunning)
        {
            if (viewController != null && viewController.CurrentMode != ViewMode.Drive)
            {
                Debug.Log("[AutoObstacleRequester] 检测到非行车模式，自动停止");
                StopRequesting();
                yield break;
            }

            // 发送障碍物数据
            if (enableObstacle)
            {
                string obstacleData = obstacleDataArray[currentObstacleIndex];
                SendRequest("Adas@ObstacleJsonData", obstacleData);
                currentObstacleIndex = (currentObstacleIndex + 1) % obstacleDataArray.Length;
            }

            // 发送车道线数据
            if (enableLane)
            {
                string laneData = laneDataArray[currentLaneIndex];
                SendRequest("Adas@LaneLineJsonData", laneData);
                currentLaneIndex = (currentLaneIndex + 1) % laneDataArray.Length;
            }

            // 使用动态计算的间隔
            yield return new WaitForSeconds(currentInterval);
        }
    }

    /// <summary>
    /// 发送单次 HTTP 请求
    /// </summary>
    private void SendRequest(string signalName, string jsonData)
    {
        string encodedJson = Uri.EscapeDataString(jsonData);
        string fullUrl = $"{pythonUrl}?name={signalName}&value={encodedJson}&type=string&isstate=true";

        if (showDebugLog)
            Debug.Log($"[AutoObstacleRequester] 发送 {signalName}");

        StartCoroutine(SendHttpRequest(fullUrl));
    }

    /// <summary>
    /// HTTP 请求协程
    /// </summary>
    IEnumerator SendHttpRequest(string url)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (showDebugLog)
                    Debug.LogWarning($"[AutoObstacleRequester] HTTP 请求失败: {request.error}");
            }
        }
    }

    /// <summary>
    /// 更新 UI 显示
    /// </summary>
    private void UpdateUI()
    {
        string status = isRunning ? "🟢 运行中" : "⚪ 已停止";
        string mode = (viewController != null && viewController.CurrentMode == ViewMode.Drive) ? "行车" : "非行车";

        if (statusText != null)
        {
            statusText.text = $"自动请求: {status} (模式: {mode})";
            statusText.color = isRunning ? Color.green : Color.gray;
        }

        if (toggleButton != null)
        {
            Text buttonText = toggleButton.GetComponentInChildren<Text>();
            if (buttonText != null)
            {
                buttonText.text = isRunning ? "停止自动" : "启动自动";
            }
        }
    }

    private void OnDisable()
    {
        if (isRunning) StopRequesting();
    }

    void OnDestroy()
    {
        if (toggleButton != null)
            toggleButton.onClick.RemoveListener(ToggleRequesting);
    }
}