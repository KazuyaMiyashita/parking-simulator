using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParkingSim
{
    /// <summary>レイヤー番号(TagManager に名前を登録しなくても番号で使える)</summary>
    public static class Layers
    {
        public const int OwnCabin = 8;     // 自車のキャビン外装(運転席視点では非表示)
        public const int OwnInterior = 9;  // 内装・ミラー
        public const int Obstacle = 10;    // 他車・壁・柱(ボディが当たる)
        public const int WheelStop = 11;   // 輪止め・縁石(タイヤだけが当たる)
        public const int Guide = 12;       // 俯瞰視点用のガイド表示
        public const int OwnBody = 13;     // 自車ボディ外装

        public static int Mask(params int[] layers)
        {
            int m = 0;
            foreach (var l in layers) m |= 1 << l;
            return m;
        }

        public static int OwnCarAll => Mask(OwnCabin, OwnInterior, OwnBody);
    }

    public static class Util
    {
        static Material baseMaterial;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public static void Kill(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        /// <summary>
        /// 全マテリアルの複製元。Shader.Find を使わないのでビルド後もシェーダーが欠落しない。
        /// URP の場合はプリミティブの既定マテリアル(Lit)を使う。
        /// </summary>
        static Material BaseMaterial
        {
            get
            {
                // Built-in: Resources の Standard マテリアル(ビルドにシェーダーを含めるため)
                if (!baseMaterial && GraphicsSettings.currentRenderPipeline == null)
                    baseMaterial = Resources.Load<Material>("ParkingSimBase");
                if (!baseMaterial)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    baseMaterial = go.GetComponent<Renderer>().sharedMaterial;
                    Object.DestroyImmediate(go);
                }
                return baseMaterial;
            }
        }

        public static Material Mat(Color c, float smooth = 0.2f, float metal = 0f)
        {
            string key = $"{c.r:0.000}_{c.g:0.000}_{c.b:0.000}_{smooth:0.00}_{metal:0.00}";
            if (cache.TryGetValue(key, out var m) && m) return m;
            m = NewMat(c, smooth, metal);
            cache[key] = m;
            return m;
        }

        public static Material NewMat(Color c, float smooth = 0.2f, float metal = 0f)
        {
            var m = new Material(BaseMaterial) { color = c };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            return m;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Material m, bool collider, bool shadows)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            if (m) r.sharedMaterial = m;
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            go.transform.SetParent(parent, false);
            if (parent) go.layer = parent.gameObject.layer;
            return go;
        }

        public static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material m,
            Quaternion? rot = null, bool collider = false, bool shadows = true)
        {
            var go = Prim(PrimitiveType.Cube, name, parent, m, collider, shadows);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = size;
            return go;
        }

        /// <summary>中心でなく下端・上端などの範囲(min~max)で箱を作る</summary>
        public static GameObject BoxMinMax(string name, Transform parent, Vector3 min, Vector3 max, Material m,
            bool collider = false, bool shadows = true)
        {
            return Box(name, parent, (min + max) * 0.5f, max - min, m, null, collider, shadows);
        }

        /// <summary>a から b へ伸びる細長い箱(白線・ピラーなど)</summary>
        public static GameObject Segment(string name, Transform parent, Vector3 a, Vector3 b, float width, float thickness,
            Material m, bool shadows = true)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            Vector3 upHint = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
            var rot = len > 1e-5f ? Quaternion.LookRotation(d, upHint) : Quaternion.identity;
            return Box(name, parent, (a + b) * 0.5f, new Vector3(width, thickness, len), m, rot, false, shadows);
        }

        public static GameObject Cylinder(string name, Transform parent, Vector3 localPos, float radius, float height,
            Material m, Quaternion? rot = null, bool shadows = true)
        {
            var go = Prim(PrimitiveType.Cylinder, name, parent, m, false, shadows);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            return go;
        }

        public static GameObject Sphere(string name, Transform parent, Vector3 localPos, Vector3 size, Material m)
        {
            var go = Prim(PrimitiveType.Sphere, name, parent, m, false, true);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            return go;
        }

        public static GameObject Quad(string name, Transform parent, Vector3 localPos, Vector2 size, Material m,
            Quaternion rot)
        {
            var go = Prim(PrimitiveType.Quad, name, parent, m, false, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            return go;
        }

        public static GameObject Child(string name, Transform parent, Vector3 localPos, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            if (parent) go.layer = parent.gameObject.layer;
            return go;
        }
    }

    /// <summary>地面に平たく敷く帯(予想進路線など)のメッシュ</summary>
    public class Ribbon
    {
        public readonly GameObject go;
        readonly Mesh mesh;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<int> tris = new List<int>();
        readonly List<Vector3> normals = new List<Vector3>();

        public Ribbon(string name, Transform parent, Material m, int layer)
        {
            go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        public void Set(IList<Vector3> pts, float width, float y = 0.012f)
        {
            verts.Clear(); tris.Clear(); normals.Clear();
            if (pts != null && pts.Count >= 2)
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 a = pts[Mathf.Max(0, i - 1)], b = pts[Mathf.Min(pts.Count - 1, i + 1)];
                    Vector3 dir = b - a; dir.y = 0;
                    if (dir.sqrMagnitude < 1e-8f) dir = Vector3.forward;
                    Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * (width * 0.5f);
                    Vector3 p = pts[i]; p.y = y;
                    verts.Add(p - side); verts.Add(p + side);
                    normals.Add(Vector3.up); normals.Add(Vector3.up);
                    if (i > 0)
                    {
                        int k = i * 2;
                        tris.Add(k - 2); tris.Add(k); tris.Add(k - 1);
                        tris.Add(k - 1); tris.Add(k); tris.Add(k + 1);
                    }
                }
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        public void SetVisible(bool v) => go.SetActive(v);
    }
}
