using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ParkCarLine : MonoBehaviour
{
    public GameObject LinePer;
    private bool enable=false;
    public Color color = new Color(1f, 0.85f, 0.1f, 0.85f);

    public Vector3 offs = new Vector3(0f, 0.02f, -0.3f);


    public float halfTrack = 0.75f;
 
    public float wheelBase = 2.6f;
    public int dashCount = 6;
    public float dashLength = 0.55f;
    public float gapLength = 0.35f;
    public float lineWidth = 0.12f;
    [Range(1, 12)]
    public int subdivision = 4;
    public float maxSteerAngle = 45f;

    public List<LineInf> AllLine;

    public class LineInf
    {
        public GameObject LineObject;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public Mesh Mesh;

        public Vector3[] Vertices;
        public Vector2[] Uvs;
        public Vector3[] Normals;
        public int[] Triangles;

        public Vector3 CorePos;   
        public float legth;     
        public float rotation;  

        public int side;   
        public int index;  
    }

    int _cachedDash = -1;
    int _cachedSub = -1;

    // ------------------------------------------------------------------
    public void LineInit()
    {
        if (AllLine != null)
        {
            foreach (var inf in AllLine)
            {
                if (inf != null && inf.LineObject != null)
                {
                    if (Application.isPlaying) Destroy(inf.LineObject);
                    else DestroyImmediate(inf.LineObject);
                }
            }
        }
        AllLine = new List<LineInf>();

        if (LinePer == null) { _cachedDash = -1; _cachedSub = -1; return; }

        _cachedDash = Mathf.Max(1, dashCount);
        _cachedSub = Mathf.Max(1, subdivision);

        int total = _cachedDash * 2;
        int vCount = (_cachedSub + 1) * 2;

        for (int i = 0; i < total; i++)
        {
            GameObject go = Instantiate(LinePer, transform);
            go.name = (i < _cachedDash ? "LeftDash_" : "RightDash_") + i;
            go.transform.localScale = Vector3.one;
            go.SetActive(true);

            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();

            if (mr.sharedMaterial == null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                if (sh != null) mr.material = new Material(sh);
            }
            if (mr.material != null) mr.material.color = color;

            Mesh mesh = new Mesh { name = "ParkCarLineMesh" };
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;

            var inf = new LineInf
            {
                LineObject = go,
                Filter = mf,
                Renderer = mr,
                Mesh = mesh,
                Vertices = new Vector3[vCount],
                Uvs = new Vector2[vCount],
                Normals = new Vector3[vCount],
                Triangles = new int[_cachedSub * 6],
                side = i < _cachedDash ? 0 : 1,
                index = i < _cachedDash ? i : i - _cachedDash,
            };
            AllLine.Add(inf);
        }
    }

    public void DrawLine(Transform CarTra, float rotateFactor)
    {
        if (CarTra == null || LinePer == null) return;
        if(!enable)enable = true;
        rotateFactor=-rotateFactor;
        int wantDash = Mathf.Max(1, dashCount);
        int wantSub = Mathf.Max(1, subdivision);
        if (AllLine == null
            || _cachedDash != wantDash
            || _cachedSub != wantSub
            || AllLine.Count != wantDash * 2)
        {
            LineInit();
        }
        if (AllLine == null || AllLine.Count == 0) return;

        float steer = Mathf.Clamp(rotateFactor, -maxSteerAngle, maxSteerAngle);
        float steerRad = steer * Mathf.Deg2Rad;
        bool straight = Mathf.Abs(steer) < 0.01f;

        float R = straight ? 0f : wheelBase / Mathf.Tan(steerRad);  
        Vector3 centerLocal = straight ? Vector3.zero : new Vector3(-R, 0f, 0f);
        float rotSign = straight ? 0f : Mathf.Sign(R);

        Vector3 rL = new Vector3(-halfTrack, 0f, 0f) - centerLocal;
        Vector3 rR = new Vector3(halfTrack, 0f, 0f) - centerLocal;
        float radiusL = Mathf.Max(0.0001f, rL.magnitude);
        float radiusR = Mathf.Max(0.0001f, rR.magnitude);

        Vector3 up = CarTra.up;

        System.Func<float, Vector3, float, Vector3> Sample =
            (s, r0, radius) =>
            {
                if (straight)
                    return centerLocal + r0 + new Vector3(0f, 0f, -s);
                float phi = (s / radius) * rotSign;
                return centerLocal + RotateAroundY(r0, phi);
            };

        for (int k = 0; k < AllLine.Count; k++)
        {
            LineInf inf = AllLine[k];
            if (inf.LineObject == null) continue;

            bool isLeft = inf.side == 0;
            Vector3 r0 = isLeft ? rL : rR;
            float radius = isLeft ? radiusL : radiusR;

            int sub = wantSub;
            int vCount = (sub + 1) * 2;
            if (inf.Vertices == null || inf.Vertices.Length != vCount)
            {
                inf.Vertices = new Vector3[vCount];
                inf.Uvs = new Vector2[vCount];
                inf.Normals = new Vector3[vCount];
                inf.Triangles = new int[sub * 6];
            }

            float s0 = inf.index * (dashLength + gapLength);
            float s1 = s0 + dashLength;

            Vector3 p0Local = Sample(s0, r0, radius);
            Vector3 worldFirst = CarTra.TransformPoint(p0Local + offs);

            float eps = Mathf.Min(0.02f, dashLength * 0.5f);
            Vector3 worldNext = CarTra.TransformPoint(Sample(s0 + eps, r0, radius) + offs);
            Vector3 forward = worldNext - worldFirst;
            if (forward.sqrMagnitude < 1e-8f) forward = -CarTra.forward;
            forward.Normalize();

            Vector3 right = Vector3.Cross(up, forward);
            if (right.sqrMagnitude < 1e-8f) right = CarTra.right;
            right.Normalize();

            Transform tr = inf.LineObject.transform;
            tr.position = worldFirst;
            tr.rotation = Quaternion.LookRotation(forward, up);

            Vector3[] verts = inf.Vertices;
            Vector2[] uvs = inf.Uvs;
            int[] tris = inf.Triangles;
            float halfW = lineWidth * 0.5f;

            for (int i = 0; i <= sub; i++)
            {
                float t = (float)i / sub;
                float s = Mathf.Lerp(s0, s1, t);

                Vector3 world = CarTra.TransformPoint(Sample(s, r0, radius) + offs);

                // ÇÐÏß
                Vector3 tangentLocal =
                    Sample(s + 0.02f, r0, radius) - Sample(s - 0.02f, r0, radius);
                Vector3 worldTangent = CarTra.TransformDirection(tangentLocal);
                if (worldTangent.sqrMagnitude < 1e-8f) worldTangent = forward;
                worldTangent.Normalize();

                Vector3 sideN = Vector3.Cross(up, worldTangent);
                if (sideN.sqrMagnitude < 1e-8f) sideN = right;
                sideN.Normalize();

                Vector3 wL = world - sideN * halfW;
                Vector3 wR = world + sideN * halfW;

                Vector3 dL = wL - worldFirst;
                Vector3 dR = wR - worldFirst;

                verts[i * 2] = new Vector3(Vector3.Dot(dL, right), Vector3.Dot(dL, up), Vector3.Dot(dL, forward));
                verts[i * 2 + 1] = new Vector3(Vector3.Dot(dR, right), Vector3.Dot(dR, up), Vector3.Dot(dR, forward));
                uvs[i * 2] = new Vector2(0f, t);
                uvs[i * 2 + 1] = new Vector2(1f, t);
            }

            for (int i = 0; i < sub; i++)
            {
                int a = i * 2;
                int b = i * 2 + 1;
                int c = (i + 1) * 2;
                int d = (i + 1) * 2 + 1;
                int o = i * 6;
                tris[o] = a; tris[o + 1] = c; tris[o + 2] = b;
                tris[o + 3] = b; tris[o + 4] = c; tris[o + 5] = d;
            }

            Vector3[] normals = inf.Normals;
            for (int i = 0; i < vCount; i++) normals[i] = Vector3.up;

            Mesh mesh = inf.Mesh;
            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.normals = normals;
            mesh.RecalculateBounds();

            Vector3 worldEnd = CarTra.TransformPoint(Sample(s1, r0, radius) + offs);
            inf.CorePos = worldFirst;
            inf.legth = Vector3.Distance(worldFirst, worldEnd);
            inf.rotation = tr.eulerAngles.y;
        }
    }

    public void DisDraw()
    {
        if(!enable)return;
        enable = false;
        foreach(var line in AllLine)
        {
            line.LineObject.SetActive(false);
        }
    }

    static Vector3 RotateAroundY(Vector3 v, float radians)
    {
        float c = Mathf.Cos(radians);
        float s = Mathf.Sin(radians);
        return new Vector3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c);
    }
}