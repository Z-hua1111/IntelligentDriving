using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

struct RoadInf
{
    public RoadInf(string name, int id, int order, GameObject body)
    {
        this.name = name;
        this.id = id;
        this.order = order;
        this.body = body;
        filter = body.GetComponent<MeshFilter>();
    }
    public string name;
    public int id;
    public int order;
    public GameObject body;
    public MeshFilter filter;
}

/// <summary>s=单线 d=双线；solid=实线 dashed=虚线</summary>
public enum LineType
{
    s_solid, d_solid, s_dashed, d_dashed
}

struct LinesInfo
{
    public LinesInfo(string name, int id, GameObject body, LineType type)
    {
        this.name = name;
        this.type = type;
        this.id = id;
        this.roadId = 0;
        this.body = body;
        filter = body.GetComponent<MeshFilter>();
    }
    public string name;
    public LineType type;
    // 部件自身的实例 ID；roadId 记录所属路面部件
    public int id;
    public int roadId;
    public GameObject body;
    public MeshFilter filter;
}

public class RoadRenderManager : MonoBehaviour
{
    private const string RoadPartName = "RoadSurface";
    private const int MaxSections = 512;
    private const float Epsilon = 0.001f;

    [Header("引用")]
    [Tooltip("车辆位置；留空时退化为使用本组件所在物体的位置")]
    public Transform Cartransform;

    [Header("就近渲染")]
    public float forwardDistance = 150f;
    public float backDistance = 30f;
    public float refreshStep = 4f;
    public bool usePosRotatesAsHeading = true;

    [Header("车道线")]
    [Tooltip("单条标线宽度")]
    public float lineWidth = 0.12f;
    [Tooltip("双标线两条线中心间距")]
    public float doubleLineGap = 0.22f;
    public float dashLength = 3f;
    public float gapLength = 3f;
    public float lineHeight = 0.02f;
    public LineType leftEdgeType = LineType.s_solid;
    public float uvScale = 0.25f;

    [Header("材质（留空则运行时自动创建）")]
    public Material roadMaterial;
    public Material lineMaterial;
    public Material centerLineMaterial;

    [Header("自模拟")]
    public float simLaneWidth = 3.5f;
    public float simStep = 5f;
    public float simExtendMargin = 20f;

    private RoadClass road;
    private bool simulated;
    private bool initialized;
    private Vector3 lastCarPos;
    private int simMinIndex;
    private int simMaxIndex;

    private List<RoadInf> AllRoads = new List<RoadInf>();
    private List<LinesInfo> AllLines = new List<LinesInfo>();
    private Dictionary<string, RoadInf> Roads = new Dictionary<string, RoadInf>();
    private Dictionary<string, LinesInfo> Lines = new Dictionary<string, LinesInfo>();
    private int lastLineCount = -1;

    private Vector3[] pts;
    private float[] arc;                // 各点沿中心线的累积里程
    private Vector3[] rights;           // 各点处的右向量（世界空间）
    private int laneCount;
    private float totalArc;
    private float dashPhaseOffset;      // 虚线相位锚点，保证窗口移动时虚线不"跳"

    /// <summary>只读访问当前路面（模拟出来的也能看），不能通过它修改渲染输入</summary>
    public RoadClass RoadData { get { return road; } }
    public bool IsSimulated { get { return simulated; } }

    #region 生命周期
    private void Awake()
    {
        EnsureInitialized();
        Rebuild(true);
    }

    private void Update()
    {
        // 车辆移动不足 refreshStep 时 Rebuild 内部会直接返回
        Rebuild(false);
    }

    private void OnValidate()
    {
        forwardDistance = Mathf.Max(1f, forwardDistance);
        backDistance = Mathf.Max(0f, backDistance);
        refreshStep = Mathf.Max(0.1f, refreshStep);
        lineWidth = Mathf.Max(0.01f, lineWidth);
        doubleLineGap = Mathf.Max(lineWidth, doubleLineGap);
        dashLength = Mathf.Max(0.1f, dashLength);
        gapLength = Mathf.Max(0.1f, gapLength);
        lineHeight = Mathf.Max(0.001f, lineHeight);
        uvScale = Mathf.Max(0.001f, uvScale);
        simLaneWidth = Mathf.Max(0.2f, simLaneWidth);
        simStep = Mathf.Max(0.5f, simStep);
        simExtendMargin = Mathf.Max(0f, simExtendMargin);
    }

    /// <summary>
    /// 唯一的路面数据入口。传入 null 或无效数据即回到四车道直道模拟。
    /// </summary>
    public void SetInfo(RoadClass roadClass)
    {
        initialized = true;
        EnsureMaterials();

        if (IsValid(roadClass))
        {
            road = roadClass;
            simulated = false;
        }
        else
        {
            if (roadClass != null)
                Debug.LogWarning("[RoadRenderManager] 路面数据无效（至少需要 2 个中心点和 1 条车道），已改用四车道直道模拟。");
            CreateSimulatedRoad(GetCarPosition().z);
        }
        Rebuild(true);
    }

    /// <summary>手动强制刷新一次网格</summary>
    public void UpdateMesh()
    {
        Rebuild(true);
    }

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        if (AllRoads == null) AllRoads = new List<RoadInf>();
        if (AllLines == null) AllLines = new List<LinesInfo>();
        if (Roads == null) Roads = new Dictionary<string, RoadInf>();
        if (Lines == null) Lines = new Dictionary<string, LinesInfo>();
        EnsureMaterials();
        if (!IsValid(road))
        {
            Debug.Log("[RoadRenderManager] 未收到路面信息，自动模拟四车道直道。");
            CreateSimulatedRoad(GetCarPosition().z);
        }
        else
        {
            simulated = false;
        }
    }

    private static bool IsValid(RoadClass r)
    {
        return r != null && r.pos != null && r.pos.Count >= 2 &&
               r.radius != null && r.radius.Count > 0;
    }

    private Vector3 GetCarPosition()
    {
        return Cartransform != null ? Cartransform.position : transform.position;
    }

    private void CreateSimulatedRoad(float carZ)
    {
        float step = Mathf.Max(0.5f, simStep);
        int minIdx = Mathf.FloorToInt((carZ - backDistance - simExtendMargin) / step) - 1;
        int maxIdx = Mathf.CeilToInt((carZ + forwardDistance + simExtendMargin) / step) + 1;

        RoadClass r = new RoadClass();
        r.pos = new List<Vector3>();
        r.posRotates = new List<float>();
        r.radius = new List<List<float>>();
        r.types = new List<LineType>
        {
            LineType.s_dashed, LineType.d_solid, LineType.s_dashed, LineType.s_solid
        };
        for (int l = 0; l < 4; l++) r.radius.Add(new List<float>());

        simMinIndex = minIdx;
        simMaxIndex = minIdx - 1;
        road = r;
        simulated = true;
        for (int i = minIdx; i <= maxIdx; i++)
        {
            simMaxIndex = i;
            AppendSimPoint(i);
        }
    }

    private void EnsureSimulatedCoverage(float carZ)
    {
        float step = Mathf.Max(0.5f, simStep);
        int minIdx = Mathf.FloorToInt((carZ - backDistance - simExtendMargin) / step) - 1;
        int maxIdx = Mathf.CeilToInt((carZ + forwardDistance + simExtendMargin) / step) + 1;
        while (simMinIndex > minIdx)
        {
            simMinIndex--;
            InsertSimPoint(simMinIndex);
        }
        while (simMaxIndex < maxIdx)
        {
            simMaxIndex++;
            AppendSimPoint(simMaxIndex);
        }
    }

    private void InsertSimPoint(int index)
    {
        float z = index * Mathf.Max(0.5f, simStep);
        road.pos.Insert(0, new Vector3(0f, 0f, z));
        road.posRotates.Insert(0, 0f);
        for (int l = 0; l < road.radius.Count; l++) road.radius[l].Insert(0, simLaneWidth);
    }

    private void AppendSimPoint(int index)
    {
        float z = index * Mathf.Max(0.5f, simStep);
        road.pos.Add(new Vector3(0f, 0f, z));
        road.posRotates.Add(0f);
        for (int l = 0; l < road.radius.Count; l++) road.radius[l].Add(simLaneWidth);
    }
    #endregion

    #region 工厂部件
    public GameObject AddNewPart()
    {
        return AddNewPart("Mesh");
    }

    public GameObject AddNewPart(string partName)
    {
        GameObject temp = new GameObject(partName);
        temp.transform.SetParent(transform, false);
        temp.AddComponent<MeshFilter>();
        MeshRenderer mr = temp.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return temp;
    }

    public void AddRoad(string name, int order)
    {
        if (Roads.ContainsKey(name))
        {
            Debug.LogWarning($"[RoadRenderManager] 路面部件 {name} 已存在。");
            return;
        }
        GameObject go = AddNewPart(name);
        RoadInf roadInf = new RoadInf(name, go.GetInstanceID(), order, go);
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = roadMaterial;
        Roads.Add(name, roadInf);
        AllRoads.Add(roadInf);
    }

    public void AddLine(string RoadName, string name, LineType lineType)
    {
        RoadInf owner;
        if (!Roads.TryGetValue(RoadName, out owner))
        {
            Debug.LogError($"No Road named {RoadName}");
            return;
        }
        StringBuilder sb = new StringBuilder(RoadName);
        sb.Append('_').Append(name);
        string key = sb.ToString();
        if (Lines.ContainsKey(key)) return;

        GameObject go = AddNewPart(key);
        LinesInfo linesInfo = new LinesInfo(key, go.GetInstanceID(), go, lineType);
        linesInfo.roadId = owner.id;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = GetLineMaterial(lineType);
        Lines.Add(key, linesInfo);
        AllLines.Add(linesInfo);
    }

    public void AddLine(int RoadID, string name, LineType lineType)
    {
        for (int i = 0; i < AllRoads.Count; i++)
        {
            if (AllRoads[i].id == RoadID)
            {
                AddLine(AllRoads[i].name, name, lineType);
                return;
            }
        }
        Debug.LogError($"No Road with ID {RoadID}");
    }

    private RoadInf EnsureRoadPart()
    {
        RoadInf inf;
        if (Roads.TryGetValue(RoadPartName, out inf) && inf.body != null)
        {
            if (inf.filter == null)
            {
                inf.filter = inf.body.GetComponent<MeshFilter>();
                if (inf.filter == null) inf.filter = inf.body.AddComponent<MeshFilter>();
                Roads[RoadPartName] = inf;
            }
            MeshRenderer mr = inf.body.GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterial != roadMaterial) mr.sharedMaterial = roadMaterial;
            return inf;
        }
        Roads.Remove(RoadPartName);
        AddRoad(RoadPartName, 0);
        return Roads[RoadPartName];
    }

    private string LinePartName(int boundaryIndex)
    {
        return RoadPartName + "_L" + boundaryIndex;
    }

    private LinesInfo EnsureLinePart(int boundaryIndex, LineType type)
    {
        string key = LinePartName(boundaryIndex);
        LinesInfo info;
        if (!Lines.TryGetValue(key, out info) || info.body == null)
        {
            AddLine(RoadPartName, "L" + boundaryIndex, type);
            info = Lines[key];
        }
        else if (info.type != type)
        {
            info.type = type;
            Lines[key] = info;
        }
        if (info.filter == null)
        {
            info.filter = info.body.GetComponent<MeshFilter>();
            if (info.filter == null) info.filter = info.body.AddComponent<MeshFilter>();
            Lines[key] = info;
        }
        info.body.SetActive(true);
        MeshRenderer mr = info.body.GetComponent<MeshRenderer>();
        Material mat = GetLineMaterial(type);
        if (mr != null && mr.sharedMaterial != mat) mr.sharedMaterial = mat;
        return info;
    }

    private void SetPartMesh(MeshFilter filter, Mesh mesh)
    {
        if (filter == null) return;
        Mesh old = filter.sharedMesh;
        filter.sharedMesh = mesh;
        if (old != null && old != mesh)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(old);
            else UnityEngine.Object.DestroyImmediate(old);
        }
    }

    private void SetAllPartsVisible(bool visible)
    {
        foreach (KeyValuePair<string, RoadInf> kv in Roads)
            if (kv.Value.body != null) kv.Value.body.SetActive(visible);
        foreach (KeyValuePair<string, LinesInfo> kv in Lines)
            if (kv.Value.body != null) kv.Value.body.SetActive(visible);
    }

    private Material GetLineMaterial(LineType type)
    {
        bool centerLine = type == LineType.d_solid || type == LineType.d_dashed;
        return centerLine ? centerLineMaterial : lineMaterial;
    }

    private void EnsureMaterials()
    {
        if (roadMaterial == null)
            roadMaterial = MakeMaterial(new Color(0.23f, 0.24f, 0.26f), "Road Surface (runtime)");
        if (lineMaterial == null)
            lineMaterial = MakeMaterial(new Color(0.95f, 0.95f, 0.93f), "Lane Line (runtime)");
        if (centerLineMaterial == null)
            centerLineMaterial = MakeMaterial(new Color(0.95f, 0.76f, 0.10f), "Center Line (runtime)");
    }

    private static Material MakeMaterial(Color color, string matName)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) return null;

        Material mat = new Material(shader);
        mat.name = matName;
        mat.hideFlags = HideFlags.DontSave;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        return mat;
    }
    #endregion
    // ------------------------------------------------------------------
    // 就近渲染主流程
    // ------------------------------------------------------------------
    private void Rebuild(bool force)
    {
        EnsureInitialized();
        Vector3 carPos = GetCarPosition();
        float step = Mathf.Max(0.1f, refreshStep);
        if (!force && (carPos - lastCarPos).sqrMagnitude < step * step) return;
        lastCarPos = carPos;

        if (simulated) EnsureSimulatedCoverage(carPos.z);
        if (!IsValid(road))
        {
            SetAllPartsVisible(false);
            return;
        }

        PrepareGeometry();
        if (laneCount <= 0 || totalArc < Epsilon)
        {
            SetAllPartsVisible(false);
            return;
        }

        float carStation = FindCarStation(carPos);
        float winStart = Mathf.Clamp(carStation - backDistance, 0f, totalArc);
        float winEnd = Mathf.Clamp(carStation + forwardDistance, 0f, totalArc);
        if (winEnd - winStart < 0.05f)
        {
            SetAllPartsVisible(false);
            return;
        }

        // 路面
        List<Section> sections = BuildSections(winStart, winEnd);
        RoadInf roadPart = EnsureRoadPart();
        SetPartMesh(roadPart.filter, BuildRoadMesh(sections));
        roadPart.body.SetActive(true);

        // 车道线：0 为最左路沿，laneCount 为最右路沿，中间为车道分界线
        for (int k = 0; k <= laneCount; k++)
        {
            LineType type = GetBoundaryType(k);
            LinesInfo part = EnsureLinePart(k, type);
            SetPartMesh(part.filter, BuildLineMesh(k, type, winStart, winEnd));
        }
        for (int k = laneCount + 1; k <= lastLineCount; k++)
        {
            LinesInfo extra;
            if (Lines.TryGetValue(LinePartName(k), out extra) && extra.body != null)
                extra.body.SetActive(false);
        }
        lastLineCount = laneCount;
    }

    private void PrepareGeometry()
    {
        int n = road.pos.Count;
        pts = road.pos.ToArray();
        arc = new float[n];
        rights = new Vector3[n];
        laneCount = road.radius.Count;

        for (int i = 0; i < n; i++)
        {
            Vector3 forward;
            bool hasAngle = usePosRotatesAsHeading && road.posRotates != null &&
                            i < road.posRotates.Count && !float.IsNaN(road.posRotates[i]);
            if (hasAngle)
            {
                forward = Quaternion.Euler(0f, road.posRotates[i], 0f) * Vector3.forward;
            }
            else
            {
                Vector3 a = pts[Mathf.Max(0, i - 1)];
                Vector3 b = pts[Mathf.Clamp(i + 1, 0, n - 1)];
                forward = b - a;
            }
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-8f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward.normalized);
            rights[i] = right.sqrMagnitude < 1e-8f ? Vector3.right : right.normalized;
        }

        for (int i = 1; i < n; i++)
            arc[i] = arc[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        totalArc = arc[n - 1];

        // 直道模拟时把里程相位锚到世界 z，向前/向后补点也不会让虚线跳动
        dashPhaseOffset = simulated ? pts[0].z : 0f;
    }

    private float FindCarStation(Vector3 carPos)
    {
        float bestD2 = float.MaxValue;
        float station = 0f;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector3 a = pts[i];
            Vector3 ab = pts[i + 1] - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(carPos - a, ab) / len2) : 0f;
            Vector3 proj = a + ab * t;
            float dx = carPos.x - proj.x;
            float dz = carPos.z - proj.z;
            float d2 = dx * dx + dz * dz;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                station = arc[i] + (arc[i + 1] - arc[i]) * t;
            }
        }
        return station;
    }

    private LineType GetBoundaryType(int boundaryIndex)
    {
        if (boundaryIndex == 0) return leftEdgeType;
        if (road.types == null || road.types.Count == 0) return LineType.s_solid;
        int idx = boundaryIndex - 1;
        return idx < road.types.Count ? road.types[idx] : LineType.s_solid;
    }

    private float GetWidth(int lane, int pointIndex)
    {
        List<float> w = road.radius[lane];
        if (w == null || w.Count == 0) return simLaneWidth;
        int idx = Mathf.Clamp(pointIndex, 0, w.Count - 1);
        return Mathf.Max(0.05f, w[idx]);
    }

    private int FindSegment(float s)
    {
        int lo = 0;
        int hi = arc.Length - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (arc[mid] <= s) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    private struct Section
    {
        public float s;
        public Vector3 center;
        public Vector3 right;
        public float[] offsets;   // laneCount + 1 条边界相对中心线的横向偏移
    }

    private Section BuildCrossSection(float s)
    {
        float sc = Mathf.Clamp(s, 0f, totalArc);
        int i = FindSegment(sc);
        float segLen = arc[i + 1] - arc[i];
        float t = segLen > 1e-5f ? Mathf.Clamp01((sc - arc[i]) / segLen) : 0f;

        Section sec = new Section();
        sec.s = sc;
        sec.center = Vector3.Lerp(pts[i], pts[i + 1], t);
        sec.right = Vector3.Lerp(rights[i], rights[i + 1], t);
        if (sec.right.sqrMagnitude < 1e-8f) sec.right = Vector3.right;
        else sec.right = sec.right.normalized;

        float[] widths = new float[laneCount];
        float total = 0f;
        for (int k = 0; k < laneCount; k++)
        {
            widths[k] = Mathf.Lerp(GetWidth(k, i), GetWidth(k, i + 1), t);
            total += widths[k];
        }
        sec.offsets = new float[laneCount + 1];
        sec.offsets[0] = -total * 0.5f;
        for (int k = 1; k <= laneCount; k++)
            sec.offsets[k] = sec.offsets[k - 1] + widths[k - 1];
        return sec;
    }

    /// <summary>把世界坐标换算到部件局部坐标（这样本组件可任意平移/旋转）</summary>
    private Vector3 Local(Vector3 worldPoint)
    {
        return transform.InverseTransformPoint(worldPoint);
    }

    private List<float> CollectSamples(float s0, float s1)
    {
        List<float> samples = new List<float>();
        samples.Add(s0);
        List<float> inside = new List<float>();
        for (int i = 0; i < arc.Length; i++)
            if (arc[i] > s0 + Epsilon && arc[i] < s1 - Epsilon) inside.Add(arc[i]);
        int stride = Mathf.Max(1, Mathf.CeilToInt((float)inside.Count / MaxSections));
        for (int i = 0; i < inside.Count; i += stride) samples.Add(inside[i]);
        if (s1 > s0 + Epsilon) samples.Add(s1);
        return samples;
    }

    private List<Section> BuildSections(float winStart, float winEnd)
    {
        List<float> ss = CollectSamples(winStart, winEnd);
        List<Section> result = new List<Section>(ss.Count);
        for (int i = 0; i < ss.Count; i++) result.Add(BuildCrossSection(ss[i]));
        return result;
    }

    private Mesh BuildRoadMesh(List<Section> sections)
    {
        int cols = laneCount + 1;
        List<Vector3> verts = new List<Vector3>(sections.Count * cols);
        List<Vector2> uvs = new List<Vector2>(sections.Count * cols);
        List<int> tris = new List<int>();

        for (int i = 0; i < sections.Count; i++)
        {
            Section sec = sections[i];
            for (int k = 0; k < cols; k++)
            {
                verts.Add(Local(sec.center + sec.right * sec.offsets[k]));
                uvs.Add(new Vector2(sec.offsets[k] * uvScale, sec.s * uvScale));
            }
        }
        for (int i = 0; i < sections.Count - 1; i++)
        {
            for (int k = 0; k < cols - 1; k++)
            {
                int a = i * cols + k;
                tris.Add(a); tris.Add(a + cols); tris.Add(a + 1);
                tris.Add(a + cols); tris.Add(a + cols + 1); tris.Add(a + 1);
            }
        }
        return CreateMesh("RoadSurface_Near", verts, uvs, tris);
    }

    private struct Span
    {
        public float a;
        public float b;
        public Span(float a, float b) { this.a = a; this.b = b; }
    }

    private List<Span> BuildLineSpans(LineType type, float winStart, float winEnd)
    {
        List<Span> spans = new List<Span>();
        bool dashed = type == LineType.s_dashed || type == LineType.d_dashed;
        if (!dashed)
        {
            spans.Add(new Span(winStart, winEnd));
            return spans;
        }

        float period = Mathf.Max(0.2f, dashLength + gapLength);
        float dash = Mathf.Max(0.05f, dashLength);
        float phaseA = winStart + dashPhaseOffset;
        float phaseB = winEnd + dashPhaseOffset;
        long first = (long)System.Math.Floor(phaseA / period) - 1;
        for (long d = first; d * period <= phaseB && d < first + 4096; d++)
        {
            float da = d * period;
            float s0 = Mathf.Max(da, phaseA);
            float s1 = Mathf.Min(da + dash, phaseB);
            if (s1 - s0 > 0.02f)
                spans.Add(new Span(s0 - dashPhaseOffset, s1 - dashPhaseOffset));
        }
        return spans;
    }

    private Mesh BuildLineMesh(int boundaryIndex, LineType type, float winStart, float winEnd)
    {
        bool doubled = type == LineType.d_solid || type == LineType.d_dashed;
        float[] lateral = doubled
            ? new float[] { -doubleLineGap * 0.5f, doubleLineGap * 0.5f }
            : new float[] { 0f };

        List<Span> spans = BuildLineSpans(type, winStart, winEnd);
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();

        for (int l = 0; l < lateral.Length; l++)
            for (int i = 0; i < spans.Count; i++)
                AppendRibbon(boundaryIndex, lateral[l], spans[i].a, spans[i].b, verts, uvs, tris);

        return CreateMesh("LaneLine_L" + boundaryIndex, verts, uvs, tris);
    }

    private void AppendRibbon(int boundaryIndex, float lateralOffset, float s0, float s1,
                              List<Vector3> verts, List<Vector2> uvs, List<int> tris)
    {
        List<float> ss = CollectSamples(s0, s1);
        if (ss.Count < 2) return;
        int baseIndex = verts.Count;
        float half = lineWidth * 0.5f;

        for (int i = 0; i < ss.Count; i++)
        {
            Section sec = BuildCrossSection(ss[i]);
            Vector3 c = sec.center + sec.right * (sec.offsets[boundaryIndex] + lateralOffset)
                        + Vector3.up * lineHeight;
            verts.Add(Local(c - sec.right * half));
            verts.Add(Local(c + sec.right * half));
            uvs.Add(new Vector2(0f, ss[i] * uvScale));
            uvs.Add(new Vector2(1f, ss[i] * uvScale));
        }
        for (int i = 0; i < ss.Count - 1; i++)
        {
            int a = baseIndex + i * 2;
            tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
            tris.Add(a + 2); tris.Add(a + 3); tris.Add(a + 1);
        }
    }

    private static Mesh CreateMesh(string meshName, List<Vector3> verts,
                                   List<Vector2> uvs, List<int> tris)
    {
        Mesh mesh = new Mesh();
        mesh.name = meshName;
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        if (tris.Count > 0)
        {
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
        }
        mesh.RecalculateBounds();
        return mesh;
    }
}
