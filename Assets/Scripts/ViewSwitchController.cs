using HMI.Communication;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 视角模式枚举
/// </summary>
public enum ViewMode
{
    Drive,  // 行车模式
    Park    // 泊车模式
}

/// <summary>
/// 视角切换控制器 - 修复版
/// </summary>
public class ViewSwitchController : MonoBehaviour
{
    [Header("相机配置")]
    [Tooltip("行车视角相机")]
    public Camera driveCamera;

    [Tooltip("泊车视角相机")]
    public Camera parkCamera;

    [Header("相机跟随设置")]
    [Tooltip("相机跟随目标（默认是自身）")]
    public Transform followTarget;

    [Tooltip("行车视角偏移")]
    public Vector3 driveOffset = new Vector3(0, 10, -8);

    [Tooltip("泊车视角偏移（俯视）")]
    public Vector3 parkOffset = new Vector3(0, 15, 0);

    [Tooltip("泊车视角旋转")]
    public Vector3 parkRotation = new Vector3(90, 0, 0);

    [Tooltip("是否启用平滑跟随")]
    public bool useSmoothFollow = true;

    [Tooltip("平滑速度")]
    public float smoothSpeed = 5f;

    [Header("UI面板配置")]
    public GameObject driveUI;
    public GameObject parkUI;

    [Header("切换设置")]
    public ViewMode defaultMode = ViewMode.Drive;
    public KeyCode toggleKey = KeyCode.E;

    [Header("行车视角子模式（V键切换）")]
    [Tooltip("行车视角下的子模式：0=车尾跟随，1=顶部俯视")]
    [Range(0, 1)] public int driveSubMode = 0;
    public KeyCode driveViewToggleKey = KeyCode.V;

    [Tooltip("顶部俯视偏移（相对于车辆正上方）")]
    public Vector3 topDownOffset = new Vector3(0, 25, 0);

    [Tooltip("顶部俯视旋转（朝下看）")]
    public Vector3 topDownRotation = new Vector3(90, 0, 0);

    [Header("事件")]
    public UnityEvent<ViewMode> onViewChanged = new UnityEvent<ViewMode>();

    // 当前模式
    private ViewMode currentMode;
    public ViewMode CurrentMode => currentMode;

    // 当前激活的相机引用（缓存，避免每帧GetComponent）
    private Camera currentActiveCamera;

    private void Start()
    {
        if (followTarget == null)
            followTarget = transform;

        // 初始化到默认视角（不触发事件）
        InitializeView(defaultMode);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleView();
        }
        // 行车模式下按 V 键切换车尾跟随 / 顶部俯视
        if (currentMode == ViewMode.Drive && Input.GetKeyDown(driveViewToggleKey))
        {
            driveSubMode = (driveSubMode + 1) % 2;
            Debug.Log($"[ViewSwitch] 行车视角切换: {(driveSubMode == 0 ? "车尾跟随" : "顶部俯视")}");
        }
    }

    private void LateUpdate()
    {
        if (followTarget == null) return;
        if (currentActiveCamera == null) return;

        Vector3 targetPos;
        Quaternion targetRot;

        if (currentMode == ViewMode.Drive)
        {
            if (driveSubMode == 1)
            {
                // 顶部俯视：车正上方，固定朝下（不跟车旋转，能看出车的朝向变化）
                targetPos = followTarget.position + topDownOffset;
                targetRot = Quaternion.Euler(topDownRotation);
            }
            else
            {
                // 车尾跟随：相机跟车一起转
                targetPos = followTarget.position + followTarget.TransformDirection(driveOffset);
                targetRot = Quaternion.LookRotation(followTarget.position - targetPos);
            }
        }
        else // Park
        {
            targetPos = followTarget.position + parkOffset;
            targetRot = Quaternion.Euler(parkRotation);
        }

        if (useSmoothFollow)
        {
            currentActiveCamera.transform.position = Vector3.Lerp(
                currentActiveCamera.transform.position,
                targetPos,
                smoothSpeed * Time.deltaTime
            );
            currentActiveCamera.transform.rotation = Quaternion.Slerp(
                currentActiveCamera.transform.rotation,
                targetRot,
                smoothSpeed * Time.deltaTime
            );
        }
        else
        {
            currentActiveCamera.transform.position = targetPos;
            currentActiveCamera.transform.rotation = targetRot;
        }
    }

    /// <summary>
    /// 初始化视角（不触发事件）
    /// </summary>
    private void InitializeView(ViewMode mode)
    {
        currentMode = mode;

        // 设置相机
        if (driveCamera != null)
            driveCamera.enabled = (mode == ViewMode.Drive);
        if (parkCamera != null)
            parkCamera.enabled = (mode == ViewMode.Park);

        // 更新当前激活相机引用
        currentActiveCamera = (mode == ViewMode.Drive) ? driveCamera : parkCamera;

        // 设置UI
        if (driveUI != null)
            driveUI.SetActive(mode == ViewMode.Drive);
        if (parkUI != null)
            parkUI.SetActive(mode == ViewMode.Park);

        // 立即跳转到目标位置
        TeleportActiveCamera();

        Debug.Log($"[ViewSwitchController] 初始化: {mode}");
    }

    /// <summary>
    /// 切换视角
    /// </summary>
    public void ToggleView()
    {
        ViewMode newMode = (currentMode == ViewMode.Drive) ? ViewMode.Park : ViewMode.Drive;
        SetView(newMode);
    }

    /// <summary>
    /// 切换到指定视角
    /// </summary>
    public void SetView(ViewMode newMode)
    {
        if (currentMode == newMode) return;

        // 1. 先更新模式
        currentMode = newMode;

        // 2. 切换相机
        if (driveCamera != null)
            driveCamera.enabled = (newMode == ViewMode.Drive);
        if (parkCamera != null)
            parkCamera.enabled = (newMode == ViewMode.Park);

        // 3. 更新当前激活相机引用
        currentActiveCamera = (newMode == ViewMode.Drive) ? driveCamera : parkCamera;

        // 4. 立即跳转（用新模式计算位置）
        TeleportActiveCamera();

        // 5. 切换UI
        if (driveUI != null)
            driveUI.SetActive(newMode == ViewMode.Drive);
        if (parkUI != null)
            parkUI.SetActive(newMode == ViewMode.Park);

        // 6. 触发事件
        onViewChanged.Invoke(currentMode);
        Debug.Log($"[ViewSwitchController] 切换至: {currentMode}");

        // 新增：通知WebSocket
        var ws = FindObjectOfType<WebSocketClient>();
        if (ws != null)
        {
            ws.SendSceneCurrent();
        }
    }

    /// <summary>
    /// 立即将当前相机移动到目标位置
    /// </summary>
    private void TeleportActiveCamera()
    {
        if (followTarget == null || currentActiveCamera == null) return;

        Vector3 targetPos;
        Quaternion targetRot;

        if (currentMode == ViewMode.Drive)
        {
            targetPos = followTarget.position + followTarget.TransformDirection(driveOffset);
            targetRot = Quaternion.LookRotation(followTarget.position - targetPos);
        }
        else
        {
            targetPos = followTarget.position + parkOffset;
            targetRot = Quaternion.Euler(parkRotation);
        }

        currentActiveCamera.transform.position = targetPos;
        currentActiveCamera.transform.rotation = targetRot;
    }

    public void OnDriveButton()
    {
        var sceneController = GetComponent<SceneSwitchController>();
        if (sceneController != null)
        {
            sceneController.SwitchToDrive();
        }else
        {
            SetView(ViewMode.Drive);
        }
        
        // => 
    }
    
    public void OnParkButton()
    {
        var sceneController = GetComponent<SceneSwitchController>();
        if (sceneController != null)
        {
            sceneController.SwitchToPark(); // 切换场景 + 触发动画
        }
        else
        {
            // 降级：只切换视角
            SetView(ViewMode.Park);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (driveCamera == null || parkCamera == null)
        {
            Debug.LogWarning("[ViewSwitchController] 请拖拽分配两个相机！");
        }
    }
#endif
}