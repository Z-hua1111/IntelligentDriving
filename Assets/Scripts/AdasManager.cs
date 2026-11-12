using UnityEngine;

/// <summary>
/// ADAS总管理器 - 空壳（如已有则忽略）
/// </summary>
public class AdasManager : MonoBehaviour
{
    // 如果需要数据源切换功能，在这里实现
    public void SwitchDataSource(string mode)
    {
        Debug.Log($"[AdasManager] 切换数据源: {mode}");
    }
}