using UnityEngine;

public class SceneSwitchController : MonoBehaviour
{
    [Header("场景根节点")]
    public GameObject driveSceneRoot;
    public GameObject parkSceneRoot;

    [Header("联动引用")]
    public ViewSwitchController viewController;
    public AutoParkingController autoParking;

    private bool isParkingMode = false;

    void Start()
    {
        if (viewController == null)
            viewController = GetComponent<ViewSwitchController>();
        if (autoParking == null)
            autoParking = GetComponent<AutoParkingController>();

        SwitchToDrive();
    }

    /// <summary>
    /// 切换到泊车场景（只切换场景，不开始泊车）
    /// </summary>
    public void SwitchToPark()
    {
        if (isParkingMode) return;

        isParkingMode = true;

        // 1. 切换场景根节点显隐
        if (driveSceneRoot != null)
            driveSceneRoot.SetActive(false);
        if (parkSceneRoot != null)
            parkSceneRoot.SetActive(true);

        // 2. 切换相机视角
        if (viewController != null)
            viewController.SetView(ViewMode.Park);

        Debug.Log("[SceneSwitchController] 切换到泊车场景，等待开始泊车指令");
    }

    /// <summary>
    /// 切换到行车场景
    /// </summary>
    public void SwitchToDrive()
    {
        isParkingMode = false;

        if (driveSceneRoot != null)
            driveSceneRoot.SetActive(true);
        if (parkSceneRoot != null)
            parkSceneRoot.SetActive(false);

        if (viewController != null)
            viewController.SetView(ViewMode.Drive);

        if (autoParking != null)
            autoParking.ResetParking();

        Debug.Log("[SceneSwitchController] 切换到行车场景");
    }

    /// <summary>
    /// 开始泊车（由 parking.control 触发）
    /// </summary>
    public void StartParking(float speed)
    {
        if (!isParkingMode)
        {
            Debug.LogWarning("[SceneSwitchController] 当前不在泊车场景，请先切换到泊车场景");
            return;
        }

        if (autoParking != null)
        {
            autoParking.StartParking(speed);
        }
        else
        {
            Debug.LogWarning("[SceneSwitchController] 未找到 AutoParkingController");
        }
    }

    public bool IsParkingMode()
    {
        return isParkingMode;
    }
}