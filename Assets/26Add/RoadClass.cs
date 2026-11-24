using System.Collections.Generic;
using UnityEngine;

public class RoadClass
{
    /// <summary>
    /// 将整个路面分解为片点（中心线，按行进方向依次排列，至少需要 2 个点）。
    /// 坐标为世界坐标；RoadRenderManager 会自动把它们换算到自身局部空间。
    /// </summary>
    public List<Vector3> pos;

    /// <summary>
    /// 路径点的路线朝向（绕 Y 轴的角度，单位：度，0 表示朝 +Z）。
    /// 可为空或长度不足：渲染器会用相邻点的切线推导朝向。
    /// 若该值实际表示的是"曲度"而非朝向，请把渲染器的 usePosRotatesAsHeading 关掉。
    /// </summary>
    public List<float> posRotates;

    /// <summary>
    /// 各条车道在各路径点处的宽度：radius[车道序号][点序号]。
    /// 车道从左至右编号 0..N-1；某一车道的宽度列表长度不足时，沿用其最后一个值。
    /// </summary>
    public List<List<float>> radius;

    /// <summary>
    /// 各车道右侧车道线的种类：types[i] 描述第 i 条车道右边界的线型。
    /// 因此 types[0..N-2] 是车道之间的分界线，types[N-1] 是最右侧路沿。
    /// 最左侧路沿由渲染器的 leftEdgeType 指定；types 不足时默认单实线。
    /// </summary>
    public List<LineType> types;
}
