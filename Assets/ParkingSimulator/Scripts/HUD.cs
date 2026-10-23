using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>画面表示(IMGUI)。1920x1080 を基準に拡大縮小する。マウスでは操作しない(Joy-Con とキーボードで同じ操作)</summary>
    public class HUD : MonoBehaviour
    {
        public GameManager gm;

        float vw, vh = 1080f;

        Font font;
        bool styled;
        GUIStyle panel, label, small, big, huge, title, centerLabel, header;
        Texture2D panelTex, solidTex, frameTex;
        GUIStyle solid;

        // 車の選択画面の外観プレビュー
        Camera previewCam;
        RenderTexture previewTex;

        struct ToastMsg { public string text; public Color color; public float until; }
        readonly List<ToastMsg> toasts = new List<ToastMsg>();

        static readonly Color Accent = new Color(1f, 0.6f, 0.15f);

        public void Toast(string text, Color c, float dur = 2.5f)
        {
            toasts.RemoveAll(t => t.text == text);
            toasts.Add(new ToastMsg { text = text, color = c, until = Time.unscaledTime + dur });
            if (toasts.Count > 4) toasts.RemoveAt(0);
        }

        void OnDestroy()
        {
            if (previewTex) { previewTex.Release(); Util.Kill(previewTex); }
        }

        // ---------------------------------------------------------------- スタイル
        void InitStyles()
        {
            if (styled) return;
            styled = true;
            font = FindJapaneseFont();

            panelTex = Tex(new Color(0.03f, 0.04f, 0.06f, 0.72f));
            solidTex = Tex(new Color(0.05f, 0.06f, 0.08f, 0.94f));
            frameTex = Tex(new Color(0.08f, 0.08f, 0.09f, 1f));
            var skin = GUI.skin;
            if (font)
            {
                skin.font = font;
                foreach (var st in new[] { skin.label, skin.button, skin.toggle, skin.box, skin.textField, skin.window })
                    st.font = font;
            }
            skin.button.fontSize = 18;
            skin.toggle.fontSize = 18;
            skin.label.fontSize = 18;
            skin.button.padding = new RectOffset(10, 10, 6, 6);

            panel = new GUIStyle { normal = { background = panelTex }, padding = new RectOffset(14, 14, 10, 10) };
            solid = new GUIStyle(panel) { normal = { background = solidTex } };
            label = new GUIStyle(skin.label) { fontSize = 19, richText = true, wordWrap = false, normal = { textColor = Color.white } };
            small = new GUIStyle(label) { fontSize = 16, normal = { textColor = new Color(0.85f, 0.88f, 0.92f) } };
            big = new GUIStyle(label) { fontSize = 30, fontStyle = FontStyle.Bold };
            huge = new GUIStyle(label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(label) { fontSize = 24, fontStyle = FontStyle.Bold };
            header = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.85f, 1f) } };
            centerLabel = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        }

        /// <summary>日本語フォント。同梱フォントが無ければ OS のフォントを順に試す</summary>
        static Font FindJapaneseFont()
        {
            // 同梱フォント(BIZ UDPゴシック, SIL OFL)を優先
            var bundled = Resources.Load<Font>("Fonts/BIZUDPGothic-Regular");
            if (bundled) return bundled;

            string[] candidates =
            {
                "Hiragino Kaku Gothic ProN", "Hiragino Kaku Gothic Pro", "Noto Sans JP", "Yu Gothic UI", "Meiryo UI",
                "Meiryo", "MS UI Gothic", "MS Gothic", "Noto Sans CJK JP", "Hiragino Maru Gothic ProN", "Hiragino Sans",
            };
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var n in candidates)
            {
                if (!installed.Contains(n)) continue;
                var f = Font.CreateDynamicFontFromOSFont(n, 20);
                if (!f) continue;
                f.RequestCharactersInTexture("駐車", 20);
                if (f.GetCharacterInfo('駐', out _, 20)) return f;
                Util.Kill(f);
            }
            return null;
        }

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }

        // ---------------------------------------------------------------- 描画
        void OnGUI()
        {
            if (!gm || !gm.car) return;
            InitStyles();
            float s = Screen.height / 1080f;
            vw = Screen.width / s;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));

            var ov = gm.overlay;
            if (gm.clearance.show && ov == Overlay.None) DrawClearance();
            if (gm.rig.MirrorsActive) DrawMirrors();
            if (gm.rig.BackMonitorVisible) DrawBackMonitor();
            DrawTopInfo();
            DrawInstruments();
            DrawSensors();
            if (gm.destination != null) DrawNav();
            if (gm.arrival != null && ov == Overlay.None) DrawArrival();
            DrawToasts();
            switch (ov)
            {
                case Overlay.Map: DrawMap(); break;
                case Overlay.Menu: DrawMenu(); break;
                case Overlay.CarSelect: DrawCarSelect(); break;
                case Overlay.Help: DrawHelp(); break;
            }
        }

        void DrawTopInfo()
        {
            var car = gm.car;
            GUILayout.BeginArea(new Rect(16, 16, 640, 100), panel);
            GUILayout.Label($"現在地: <b>{gm.world.ZoneName(car.Center)}</b>", label);
            GUILayout.Label("<color=#9ad>視点 X / V    ズーム ZR・ZL / R・F    マップ − / M    メニュー + / Esc</color>", small);
            GUILayout.Label(Pad.Status() + (Pad.Connected ? "" : "   <color=#888>(キーボードで操作できます)</color>"), small);
            GUILayout.EndArea();
        }

        void DrawInstruments()
        {
            var car = gm.car;
            var r = new Rect(16, vh - 236, 800, 220);
            GUI.Box(r, GUIContent.none, panel);

            // 速度
            float kmh = car.SpeedKmh;
            GUI.Label(new Rect(r.x + 16, r.y + 8, 260, 50), $"{Mathf.Abs(kmh):0.0} <size=20>km/h</size>", big);
            GUI.Label(new Rect(r.x + 200, r.y + 22, 120, 26), kmh > 0.05f ? "▲ 前進" : kmh < -0.05f ? "▼ 後退" : "停止", small);

            // シフト: 左右にシフト操作のキーを並べ、押すとどちらへ動くかを示す(L / Q で P 側、R / E で D 側)
            var keyStyle = new GUIStyle(centerLabel) { fontSize = 17, fontStyle = FontStyle.Bold };
            var arrowStyle = new GUIStyle(centerLabel) { fontSize = 26, normal = { textColor = new Color(1f, 0.85f, 0.2f) } };
            float gx = r.x + 96, gy = r.y + 74;
            GUI.Label(new Rect(gx - 72, gy - 2, 64, 22), "L / Q", keyStyle);
            GUI.Label(new Rect(gx - 72, gy + 16, 64, 26), "◀", arrowStyle);
            string[] g = { "P", "R", "N", "D" };
            for (int i = 0; i < 4; i++)
            {
                bool on = (int)car.gear == i;
                var gr = new Rect(gx + i * 46, gy, 40, 40);
                GUI.color = on ? new Color(1f, 0.85f, 0.2f) : new Color(1, 1, 1, 0.3f);
                GUI.DrawTexture(gr, Texture2D.whiteTexture);
                GUI.color = Color.white;
                var st = new GUIStyle(centerLabel) { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = on ? Color.black : Color.white } };
                GUI.Label(gr, g[i], st);
            }
            float kx = gx + 4 * 46 + 4;
            GUI.Label(new Rect(kx, gy - 2, 64, 22), "R / E", keyStyle);
            GUI.Label(new Rect(kx, gy + 16, 64, 26), "▶", arrowStyle);
            GUI.Label(new Rect(gx - 72, gy + 44, 330, 22), "<color=#9ad>枠に停めて P に入れると記録します</color>", new GUIStyle(small) { fontSize = 14 });

            // ペダル
            Bar(new Rect(r.x + 16, r.y + 160, 200, 14), car.throttle, new Color(0.3f, 0.9f, 0.4f), "アクセル  A / W");
            Bar(new Rect(r.x + 16, r.y + 198, 200, 14), car.brake, new Color(1f, 0.35f, 0.3f), "ブレーキ  B / S");

            // ハンドル
            Vector2 c = new Vector2(r.x + 540, r.y + 110);
            DrawSteeringIcon(c, 62f, car.wheelDeg);
            float turns = car.wheelDeg / 360f;
            string dir = Mathf.Abs(car.wheelDeg) < 1f ? "中立" : car.wheelDeg > 0 ? "右" : "左";
            bool full = Mathf.Abs(car.wheelDeg) >= CarSpec.MaxWheelDeg - 0.5f;
            GUILayout.BeginArea(new Rect(r.x + 615, r.y + 12, 180, 200));
            GUILayout.Label("ハンドル", small);
            GUILayout.Label($"<b>{dir} {Mathf.Abs(turns):0.00}回転</b>", label);
            GUILayout.Label(full ? "<color=#fc6>いっぱい</color>" : $"{Mathf.Abs(car.wheelDeg):0}°", small);
            GUILayout.Label($"タイヤ {car.SteerDeg:+0.0;-0.0;0.0}°", small);
            GUILayout.Space(6);
            GUILayout.Label(car.steerHold ? "<color=#fb6><b>■ 固定中</b></color>" : "<color=#888>固定</color>", small);
            GUILayout.Label("<color=#888><size=14>Lスティック押込 / Space</size></color>", small);
            GUILayout.EndArea();
        }

        void Bar(Rect r, float v, Color col, string name)
        {
            GUI.Label(new Rect(r.x, r.y - 22, 200, 22), name, small);
            GUI.color = new Color(1, 1, 1, 0.15f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = col;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void DrawSteeringIcon(Vector2 c, float radius, float deg)
        {
            const int n = 32;
            float rot = deg * Mathf.Deg2Rad;
            Vector2 P(float a, float rr) => c + new Vector2(Mathf.Sin(a + rot), -Mathf.Cos(a + rot)) * rr;
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                bool top = i == 0 || i == n - 1;
                Line(P(a0, radius), P(a1, radius), top ? new Color(1f, 0.55f, 0.1f) : new Color(0.85f, 0.85f, 0.9f), 7f);
            }
            Line(P(Mathf.PI / 2, 14), P(Mathf.PI / 2, radius), Color.gray, 6f);
            Line(P(-Mathf.PI / 2, 14), P(-Mathf.PI / 2, radius), Color.gray, 6f);
            Line(P(Mathf.PI, 14), P(Mathf.PI, radius), Color.gray, 6f);
            Line(P(0, 0) + new Vector2(-12, 0), P(0, 0) + new Vector2(12, 0), Color.gray, 18f);
        }

        void DrawSensors()
        {
            var sen = gm.sensors;
            if (!sen) return;
            bool any = false;
            foreach (var a in sen.active) any |= a;
            if (!any) return;
            var r = new Rect(vw - 236, vh - 336, 220, 320);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 10, r.y + 6, 200, 24), "距離センサー", small);
            // 車のアイコン(上が前)
            float cw = 64f, ch = 162f;
            Vector2 c = new Vector2(r.x + r.width / 2, r.y + 172);
            var body = new Rect(c.x - cw / 2, c.y - ch / 2, cw, ch);
            GUI.color = new Color(0.93f, 0.93f, 0.92f);
            GUI.DrawTexture(body, Texture2D.whiteTexture);
            GUI.color = new Color(0.1f, 0.12f, 0.15f);
            GUI.DrawTexture(new Rect(body.x + 8, body.y + 42, cw - 16, 30), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(body.x + 8, body.y + 118, cw - 16, 20), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float scale = ch / CarSpec.Length;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < ParkingSensors.Defs.Length; i++)
            {
                if (!sen.active[i]) continue;
                var d = ParkingSensors.Defs[i];
                float dist = sen.dist[i];
                nearest = Mathf.Min(nearest, dist);
                Vector2 p = c + new Vector2(d.local.x, -d.local.z) * scale;
                Vector2 dir = new Vector2(Mathf.Sin(d.yaw * Mathf.Deg2Rad), -Mathf.Cos(d.yaw * Mathf.Deg2Rad));
                int levels = float.IsInfinity(dist) ? 0 : dist < 0.3f ? 4 : dist < 0.5f ? 3 : dist < 0.8f ? 2 : 1;
                for (int k = 0; k < 4; k++)
                {
                    Color col = k >= 4 - levels
                        ? (levels >= 4 ? Color.red : levels == 3 ? new Color(1f, 0.5f, 0.1f) : levels == 2 ? Color.yellow : new Color(0.3f, 1f, 0.4f))
                        : new Color(1, 1, 1, 0.12f);
                    Vector2 q = p + dir * (12 + (3 - k) * 9);
                    Vector2 side = new Vector2(-dir.y, dir.x) * 9f;
                    Line(q - side, q + side, col, 6f);
                }
            }
            string txt = float.IsInfinity(nearest) ? "障害物なし" : $"最接近 {nearest * 100:0} cm";
            GUI.Label(new Rect(r.x, r.y + r.height - 32, r.width, 26), txt, centerLabel);
        }

        void DrawMirrors()
        {
            var p = gm.car.parts;
            float top = 18f;
            Mirror(new Rect(vw / 2 - 230, top, 460, 133), p.rtRear, "ルームミラー");
            Mirror(new Rect(16, 340, 330, 198), p.rtLeft, "左ミラー");
            Mirror(new Rect(vw - 346, 340, 330, 198), p.rtRight, "右ミラー");
        }

        void Mirror(Rect r, Texture tex, string name)
        {
            if (!tex) return;
            GUI.DrawTexture(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), frameTex);
            GUI.DrawTextureWithTexCoords(r, tex, new Rect(1, 0, -1, 1)); // 鏡像
            GUI.Label(new Rect(r.x + 6, r.y + 2, 200, 22), $"<size=14>{name}</size>", small);
        }

        void DrawBackMonitor()
        {
            var car = gm.car;
            var cam = car.parts.backCam;
            if (!cam || !car.parts.rtBack) return;
            var r = gm.rig.mode == ViewMode.Driver3D
                ? new Rect(vw / 2 - 140, vh - 330, 480, 300)
                : new Rect(vw - 552, vh - 690, 520, 325);
            GUI.DrawTexture(new Rect(r.x - 6, r.y - 30, r.width + 12, r.height + 36), frameTex);
            GUI.DrawTextureWithTexCoords(r, car.parts.rtBack, new Rect(1, 0, -1, 1)); // バックカメラは鏡像表示
            GUI.Label(new Rect(r.x + 4, r.y - 28, r.width, 24), "<size=15>バックカメラ  周囲の安全を直接確認してください</size>", small);

            // 距離の目安線(車幅・0.5m・1m・2m・3m)と、ハンドルに連動する予想進路線
            Transform t = car.transform;
            float zr = -CarSpec.HalfLength, hw = CarSpec.HalfWidth;
            Vector3 P(float x, float back) => t.TransformPoint(new Vector3(x, 0.01f, zr - back));
            Color red = new Color(1f, 0.2f, 0.15f), yel = new Color(1f, 0.9f, 0.1f), grn = new Color(0.2f, 1f, 0.3f);
            foreach (float x in new[] { -hw, hw })
            {
                WorldLine(cam, r, P(x, 0.1f), P(x, 0.5f), red, 4f);
                WorldLine(cam, r, P(x, 0.5f), P(x, 1.0f), yel, 4f);
                WorldLine(cam, r, P(x, 1.0f), P(x, 3.0f), grn, 4f);
                float s = Mathf.Sign(-x);
                WorldLine(cam, r, P(x, 1.0f), P(x + s * 0.3f, 1.0f), yel, 4f);
                WorldLine(cam, r, P(x, 2.0f), P(x + s * 0.3f, 2.0f), grn, 4f);
                WorldLine(cam, r, P(x, 3.0f), P(x + s * 0.3f, 3.0f), grn, 4f);
            }
            WorldLine(cam, r, P(-hw, 0.5f), P(hw, 0.5f), red, 4f);
            Polyline(cam, r, gm.path.rearLeft, new Color(1f, 0.6f, 0.1f), 3f);
            Polyline(cam, r, gm.path.rearRight, new Color(1f, 0.6f, 0.1f), 3f);
        }

        /// <summary>他の車・壁・柱までの距離(オプション)。車体と障害物の一番近い点を線で結び、cm で表示</summary>
        void DrawClearance()
        {
            var cam = gm.rig.cam;
            var r = new Rect(0, 0, vw, vh);
            var st = new GUIStyle(centerLabel) { fontSize = 18, fontStyle = FontStyle.Bold };
            foreach (var p in gm.clearance.pairs)
            {
                Color col = p.dist < 0.3f ? new Color(1f, 0.25f, 0.2f) : p.dist < 0.6f ? new Color(1f, 0.75f, 0.15f) : new Color(0.35f, 1f, 0.5f);
                Vector3 va = cam.WorldToViewportPoint(p.car), vb = cam.WorldToViewportPoint(p.obstacle);
                if (va.z < 0.05f || vb.z < 0.05f) continue;
                Vector2 a = new Vector2(va.x * vw, (1 - va.y) * vh), b = new Vector2(vb.x * vw, (1 - vb.y) * vh);
                if (!Clip(ref a, ref b, r)) continue;
                Line(a, b, col, 4f);
                Vector2 m = (a + b) * 0.5f;
                st.normal.textColor = col;
                Shadowed(new Rect(m.x - 50, m.y - 30, 100, 26), $"{p.dist * 100:0} cm", st);
            }
        }

        void Polyline(Camera cam, Rect r, List<Vector3> pts, Color col, float w)
        {
            for (int i = 1; i < pts.Count; i++) WorldLine(cam, r, pts[i - 1], pts[i], col, w);
        }

        void WorldLine(Camera cam, Rect r, Vector3 a, Vector3 b, Color col, float w)
        {
            Vector3 va = cam.WorldToViewportPoint(a), vb = cam.WorldToViewportPoint(b);
            if (va.z < 0.05f || vb.z < 0.05f) return;
            Vector2 pa = new Vector2(r.x + (1 - va.x) * r.width, r.y + (1 - va.y) * r.height);
            Vector2 pb = new Vector2(r.x + (1 - vb.x) * r.width, r.y + (1 - vb.y) * r.height);
            if (Clip(ref pa, ref pb, r)) Line(pa, pb, col, w);
        }

        /// <summary>Liang–Barsky による線分クリップ</summary>
        static bool Clip(ref Vector2 a, ref Vector2 b, Rect r)
        {
            float t0 = 0, t1 = 1;
            Vector2 d = b - a;
            float[] p = { -d.x, d.x, -d.y, d.y };
            float[] q = { a.x - r.xMin, r.xMax - a.x, a.y - r.yMin, r.yMax - a.y };
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Approximately(p[i], 0)) { if (q[i] < 0) return false; continue; }
                float t = q[i] / p[i];
                if (p[i] < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
            }
            Vector2 a0 = a;
            a = a0 + d * t0; b = a0 + d * t1;
            return true;
        }

        static void Line(Vector2 a, Vector2 b, Color col, float w)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var saved = GUI.matrix;
            var savedCol = GUI.color;
            GUI.matrix = saved * Matrix4x4.TRS(new Vector3(a.x, a.y, 0), Quaternion.Euler(0, 0, ang), Vector3.one);
            GUI.color = col;
            GUI.DrawTexture(new Rect(0, -w / 2, len, w), Texture2D.whiteTexture);
            GUI.matrix = saved;
            GUI.color = savedCol;
        }

        void DrawToasts()
        {
            toasts.RemoveAll(t => t.until < Time.unscaledTime);
            float y = gm.rig.MirrorsActive ? 170 : 24;
            foreach (var t in toasts)
            {
                var size = label.CalcSize(new GUIContent(t.text));
                var r = new Rect(vw / 2 - size.x / 2 - 16, y, size.x + 32, 38);
                GUI.Box(r, GUIContent.none, panel);
                var st = new GUIStyle(centerLabel) { normal = { textColor = t.color } };
                GUI.Label(r, t.text, st);
                y += 44;
            }
        }

        /// <summary>駐車枠に収まって P に入れたときの表示(点数は付けず、これまでの記録だけ)</summary>
        void DrawArrival()
        {
            var a = gm.arrival;
            float h = 270 + (a.first ? 34 : 0) + (a.unlocked ? 34 : 0) + (a.cleared ? 34 : 0);
            var r = new Rect(vw / 2 - 330, vh / 2 - h / 2 - 60, 660, h);
            GUILayout.BeginArea(r, solid);
            var center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            var record = new GUIStyle(center) { fixedHeight = 52f };
            GUILayout.Label(a.place != null ? $"<color=#fb6>{a.place.name}</color> に到着しました" : "駐車しました",
                new GUIStyle(title) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
            GUILayout.Space(14);
            GUILayout.Label($"駐車回数  <size=34><b>{a.parkings}</b></size> 回", record);
            GUILayout.Label($"総走行距離  <size=34><b>{a.distance / 1000f:0.00}</b></size> km", record);
            GUILayout.Space(6);
            if (a.first) GUILayout.Label("<color=#9df>初めての場所です</color>", center);
            if (a.unlocked) GUILayout.Label("<color=#ffd84a><b>遠くの場所に行けるようになりました</b></color>", center);
            if (a.cleared) GUILayout.Label("<color=#ffd84a><b>すべての場所に行きました! これからも自由に運転できます</b></color>", center);
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#9ad>次の目的地は − / M のマップで選べます</color>", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            GUILayout.EndArea();
        }

        // ---------------------------------------------------------------- カーナビ
        /// <summary>カーナビの案内(右上): 次の曲がり角の向きとそこまでの道のり、目的地までの道のり</summary>
        void DrawNav()
        {
            var dest = gm.destination;
            var r = new Rect(vw - 476, 16, 460, 150);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 14, r.y + 8, 430, 28), $"<color=#fb6>目的地</color>  <b>{dest.name}</b>", label);
            if (gm.guideEnded)
            {
                GUI.Label(new Rect(r.x + 14, r.y + 44, 430, 90),
                    "<size=26><b>目的地に到着</b></size>\n<size=17>空いている枠(オレンジ)に停めて、P に入れてください</size>", label);
                return;
            }
            var next = gm.guide.Next;
            int turn = next?.turn ?? 0;
            float dist = next?.along ?? gm.routeLength;
            string what = turn > 0 ? "右方向" : turn < 0 ? "左方向" : "目的地";
            GUI.Label(new Rect(r.x + 120, r.y + 40, 330, 50), $"<size=36><b>{dist:0}</b></size> m 先  <size=26><b>{what}</b></size>", label);
            GUI.Label(new Rect(r.x + 120, r.y + 100, 330, 30), gm.NearDestination ? "<color=#fb6><b>まもなく目的地</b></color>" : $"<color=#bbb>目的地まで {gm.routeLength:0} m</color>", small);
            DrawTurnIcon(new Vector2(r.x + 62, r.y + 92), turn);
        }

        /// <summary>曲がる向きの矢印(0 = まっすぐ)</summary>
        void DrawTurnIcon(Vector2 c, int turn)
        {
            const float w = 10f;
            Vector2 bottom = c + new Vector2(0, 46);
            if (turn == 0)
            {
                Vector2 tip = c + new Vector2(0, -40);
                Line(bottom, tip + new Vector2(0, 4), Accent, w);
                Line(tip, tip + new Vector2(-20, 22), Accent, w);
                Line(tip, tip + new Vector2(20, 22), Accent, w);
                return;
            }
            Vector2 corner = c + new Vector2(-turn * 14, -8);
            Vector2 end = corner + new Vector2(turn * 44, 0);
            Line(bottom + new Vector2(-turn * 14, 0), corner + new Vector2(0, w / 2), Accent, w);
            Line(corner - new Vector2(turn * w / 2, 0), end, Accent, w);
            Line(end + new Vector2(turn * 3, 0), end + new Vector2(-turn * 20, -20), Accent, w);
            Line(end + new Vector2(turn * 3, 0), end + new Vector2(-turn * 20, 20), Accent, w);
        }

        /// <summary>マップ(カーナビ): 左に町の地図と経路、右に目的地の一覧</summary>
        void DrawMap()
        {
            var car = gm.car;
            GUI.DrawTexture(new Rect(0, 0, vw, vh), solidTex);

            const float side = 460f, pad = 24f;
            float aspect = MapView.Area.width / MapView.Area.height;
            float mh = vh - pad * 2, mw = mh * aspect;
            float maxW = vw - side - pad * 3;
            if (mw > maxW) { mw = maxW; mh = mw / aspect; }
            float left = (vw - (mw + pad + side)) / 2;
            var mr = new Rect(left, (vh - mh) / 2, mw, mh);
            GUI.DrawTexture(new Rect(mr.x - 4, mr.y - 4, mr.width + 8, mr.height + 8), frameTex);
            if (gm.map.texture) GUI.DrawTexture(mr, gm.map.texture);

            // 区域名
            var zoneStyle = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            foreach (var z in gm.world.zones)
            {
                if (z.name == "十字路の交差点" || z.hideOnMap || gm.world.places.Exists(p => p.name == z.name)) continue;
                bool bigZone = z.rect.width * z.rect.height > 5000f;
                Vector3 p = new Vector3(z.rect.center.x, 0, bigZone ? z.rect.yMax - 4f : z.rect.yMin - 2.5f);
                Vector2 sp = MapView.ToScreen(p, mr);
                Shadowed(new Rect(sp.x - 150, sp.y - 14, 300, 28), z.name, zoneStyle);
            }

            // 選択中の場所への経路(選んでいる間はその場所、決定済みなら目的地)
            Place sel = gm.map.cursor < gm.mapPlaces.Count ? gm.mapPlaces[gm.map.cursor] : null;
            if (gm.destination != null && gm.route.Count > 1)
                for (int i = 1; i < gm.route.Count; i++)
                    Line(MapView.ToScreen(gm.route[i - 1], mr), MapView.ToScreen(gm.route[i], mr), new Color(1f, 0.55f, 0.1f, 0.9f), 6f);

            // 場所の印
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
            var nameStyle = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            foreach (var p in gm.mapPlaces)
            {
                if (gm.Locked(p)) continue;
                Vector2 sp = MapView.ToScreen(p.Center, mr);
                bool isSel = p == sel;
                Color col = isSel ? Color.Lerp(Accent, Color.white, blink * 0.5f)
                          : p == gm.destination ? Accent
                          : gm.save.Visited(p) ? new Color(0.35f, 0.95f, 0.45f) : new Color(0.2f, 0.85f, 1f);
                float rad = isSel ? 16f : 11f;
                for (int k = 0; k < 4; k++)
                {
                    float a0 = k * Mathf.PI / 2 + Mathf.PI / 4, a1 = a0 + Mathf.PI / 2;
                    Line(sp + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * rad, sp + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * rad, col, isSel ? 6f : 4f);
                }
                nameStyle.normal.textColor = col;
                Shadowed(new Rect(sp.x - 150, sp.y + rad + 2, 300, 26), p.name, nameStyle);
            }

            // 自車
            var c4 = new Vector3[4];
            CarSpec.Corners(car.Center, car.yaw, c4);
            Color carCol = new Color(1f, 0.92f, 0.2f);
            for (int i = 0; i < 4; i++) Line(MapView.ToScreen(c4[i], mr), MapView.ToScreen(c4[(i + 1) % 4], mr), carCol, 4f);
            Vector2 cc = MapView.ToScreen(car.Center, mr), fwd = MapView.ToScreen(car.Center + CarSpec.Forward(car.yaw) * 6f, mr);
            Line(cc, fwd, carCol, 4f);
            Shadowed(new Rect(cc.x - 60, cc.y + 12, 120, 24), "現在地", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = carCol } });

            // 右側: 目的地の一覧
            var pr = new Rect(mr.xMax + pad, mr.y, side, mh);
            GUILayout.BeginArea(pr, solid);
            GUILayout.Label("目的地を選ぶ", title);
            GUILayout.Label("<color=#9ad>どこへ行っても自由です。選ぶと経路を案内します。</color>", new GUIStyle(small) { wordWrap = true });
            GUILayout.Space(8);
            bool farHeader = false;
            for (int i = 0; i <= gm.mapPlaces.Count; i++)
            {
                var p = i < gm.mapPlaces.Count ? gm.mapPlaces[i] : null;
                if (p != null && p.far && !farHeader)
                {
                    farHeader = true;
                    GUILayout.Label(gm.FarUnlocked ? "遠くの場所" : "遠くの場所 <size=14><color=#888>(近くの3か所に行くと行けます)</color></size>", header);
                }
                string name = p == null ? "目的地を設定しない" : gm.Locked(p) ? "???" : p.name;
                string mark = p == null || gm.Locked(p) ? "" : gm.save.Visited(p) ? "<color=#7e7>✓</color> " : "<color=#9df>●</color> ";
                string note = p == null || gm.Locked(p) ? "" : $"\n<size=14><color=#bbb>{p.note}</color></size>";
                if (p != null && p == gm.destination) note += "  <size=14><color=#fb6>(現在の目的地)</color></size>";
                Row(i == gm.map.cursor, mark + name + note, p != null && gm.Locked(p), p == null || gm.Locked(p) ? 36f : 56f);
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label($"到着した場所 {gm.mapPlaces.FindAll(p => gm.save.Visited(p)).Count} / {gm.mapPlaces.Count}", small);
            GUILayout.Label("<color=#9ad>選ぶ: 左スティック・十字 / 矢印    決定: A / Enter    閉じる: − / M</color>", new GUIStyle(small) { wordWrap = true });
            GUILayout.EndArea();
        }

        /// <summary>一覧の1行(選択中は背景を明るく)</summary>
        void Row(bool selected, string text, bool disabled, float height = 40f)
        {
            var r = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
            if (selected)
            {
                GUI.color = new Color(1f, 0.6f, 0.15f, 0.35f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Accent;
                GUI.DrawTexture(new Rect(r.x, r.y, 5, r.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            var st = new GUIStyle(label) { normal = { textColor = disabled ? new Color(0.5f, 0.5f, 0.5f) : Color.white }, alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(r.x + 14, r.y, r.width - 14, r.height), text, st);
        }

        /// <summary>影付きの文字(マップの上でも読めるように)</summary>
        static void Shadowed(Rect r, string text, GUIStyle st)
        {
            var sh = new GUIStyle(st) { normal = { textColor = new Color(0, 0, 0, 0.85f) } };
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, sh);
            GUI.Label(r, text, st);
        }

        // ---------------------------------------------------------------- メニュー
        void DrawMenu()
        {
            GUI.DrawTexture(new Rect(0, 0, vw, vh), panelTex);
            var r = new Rect(vw / 2 - 420, 70, 840, 900);
            GUILayout.BeginArea(r, solid);
            GUILayout.Label("メニュー", title);
            var sv = gm.save;
            GUILayout.Label($"駐車回数 {sv.parkings} 回    総走行距離 {sv.distance / 1000f:0.00} km    " +
                            $"到着した場所 {gm.mapPlaces.FindAll(p => sv.Visited(p)).Count} / {gm.mapPlaces.Count}", small);
            GUILayout.Space(10);
            for (int i = 0; i < gm.menu.Count; i++)
            {
                var item = gm.menu[i];
                string v = item.value?.Invoke() ?? "";
                if (v.Length > 0) v = item.adjust != null ? $"<color=#fd8>◀ {v} ▶</color>" : $"<color=#fd8>{v}</color>";
                Row(i == gm.menuCursor, item.label, false, 52f);
                var last = GUILayoutUtility.GetLastRect();
                GUI.Label(new Rect(last.x + 380, last.y, last.width - 390, last.height), v, new GUIStyle(label) { alignment = TextAnchor.MiddleRight });
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#9ad>選ぶ: 上下    切り替え: 左右 / 決定    決定: A / Enter    戻る: B / Backspace    閉じる: + / Esc</color>", small);
            GUILayout.EndArea();
        }

        void DrawHelp()
        {
            GUI.DrawTexture(new Rect(0, 0, vw, vh), panelTex);
            var r = new Rect(vw / 2 - 560, 70, 1120, 820);
            GUILayout.BeginArea(r, solid);
            GUILayout.Label("操作説明", title);
            GUILayout.Space(8);
            var head = new GUIStyle(header);
            GUILayout.BeginHorizontal();
            GUILayout.Label("操作", head, GUILayout.Width(400));
            GUILayout.Label("Joy-Con", head, GUILayout.Width(330));
            GUILayout.Label("キーボード", head);
            GUILayout.EndHorizontal();
            for (int i = 0; i < Controls.Table.GetLength(0); i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Controls.Table[i, 0], label, GUILayout.Width(400));
                GUILayout.Label($"<b>{Controls.Table[i, 1]}</b>", label, GUILayout.Width(330));
                GUILayout.Label($"<b>{Controls.Table[i, 2]}</b>", label);
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            GUILayout.Space(10);
            var note = new GUIStyle(small) { wordWrap = true };
            GUILayout.Label("<color=#9ad>ハンドル: スティックを倒した角度までハンドルが回り、離すと中央へ少しゆっくり戻ります(キーは押している間いっぱいまで回ります)。" +
                            "左スティック押し込み / Space で今の角度に固定でき、固定中はスティックを動かしてもハンドルは回りません。もう一度押すと解除されます。</color>", note);
            GUILayout.Label("<color=#9ad>走行中の危険なシフト操作(動いている間の P、前進中の R、後退中の D)は受け付けません。駐車枠に収まった状態で P に入れると、駐車回数と総走行距離を記録します。マップは止まっているときに開けます。</color>", note);
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#9ad>戻る: B / Backspace</color>", small);
            GUILayout.EndArea();
        }

        void DrawCarSelect()
        {
            GUI.DrawTexture(new Rect(0, 0, vw, vh), panelTex);
            var r = new Rect(vw / 2 - 640, 70, 1280, 880);
            GUI.Box(r, GUIContent.none, solid);
            GUI.Label(new Rect(r.x + 20, r.y + 14, 600, 40), "車の選択", title);

            // 3つの枠
            float sw = 400f, sh = 120f, gap = 20f;
            for (int i = 0; i < GameManager.CarSlots; i++)
            {
                var sr = new Rect(r.x + 20 + i * (sw + gap), r.y + 70, sw, sh);
                bool sel = i == gm.carCursor;
                GUI.color = sel ? new Color(1f, 0.6f, 0.15f, 0.35f) : new Color(1, 1, 1, 0.06f);
                GUI.DrawTexture(sr, Texture2D.whiteTexture);
                GUI.color = sel ? Accent : new Color(1, 1, 1, 0.2f);
                GUI.DrawTexture(new Rect(sr.x, sr.yMax - 5, sr.width, 5), Texture2D.whiteTexture);
                GUI.color = Color.white;
                string name = GameManager.CarNames[i];
                string sub = i == 0 ? (gm.save.carIndex == 0 ? "<color=#7e7>選択中</color>" : "") : "<color=#888>準備中</color>";
                GUI.Label(new Rect(sr.x, sr.y + 20, sr.width, 40), $"<b>{name}</b>", new GUIStyle(centerLabel) { fontSize = 24 });
                GUI.Label(new Rect(sr.x, sr.y + 66, sr.width, 30), sub, centerLabel);
            }

            var body = new Rect(r.x + 20, r.y + 210, r.width - 40, r.height - 270);
            if (gm.carCursor != 0)
            {
                GUI.Label(body, "<color=#888>この枠はまだ空いています</color>", new GUIStyle(centerLabel) { fontSize = 24 });
            }
            else
            {
                // 外観(自車をまわりから見たプレビュー)と諸元
                var pv = new Rect(body.x, body.y, 640, 420);
                RenderPreview(pv);
                if (previewTex) GUI.DrawTexture(pv, previewTex);
                GUILayout.BeginArea(new Rect(body.x + 670, body.y, body.width - 670, body.height));
                GUILayout.Label($"<b>{CarSpec.DisplayName}</b>", title);
                GUILayout.Label($"{CarSpec.ModelCode}  {CarSpec.ColorName}", small);
                GUILayout.Space(10);
                void Spec(string k, string v)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(k, small, GUILayout.Width(170));
                    GUILayout.Label(v, new GUIStyle(label) { wordWrap = true });
                    GUILayout.EndHorizontal();
                }
                Spec("全長×全幅×全高", $"{CarSpec.Length * 1000:#,0} × {CarSpec.Width * 1000:#,0} × {CarSpec.Height * 1000:#,0} mm");
                Spec("ホイールベース", $"{CarSpec.Wheelbase * 1000:#,0} mm");
                Spec("トレッド 前/後", $"{CarSpec.TrackFront * 1000:#,0} / {CarSpec.TrackRear * 1000:#,0} mm");
                Spec("最小回転半径", $"<b>{CarSpec.MinTurningRadius} m</b>");
                Spec("車両重量", $"{CarSpec.WeightKg:#,0} kg");
                Spec("エンジン", CarSpec.Engine);
                Spec("駆動 / 変速機", CarSpec.Drivetrain);
                Spec("タイヤ", CarSpec.Tire);
                GUILayout.EndArea();
            }
            GUI.Label(new Rect(r.x + 20, r.yMax - 44, r.width - 40, 30),
                "<color=#9ad>選ぶ: 左右    決定: A / Enter    戻る: B / Backspace</color>", small);
        }

        /// <summary>自車だけを映すカメラで、まわりを回りながら外観を描く</summary>
        void RenderPreview(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!previewCam)
            {
                previewCam = Util.Child("Car Preview Camera", transform, Vector3.zero).AddComponent<Camera>();
                previewCam.enabled = false;
                previewCam.clearFlags = CameraClearFlags.SolidColor;
                previewCam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
                previewCam.cullingMask = Layers.Mask(Layers.OwnBody, Layers.OwnCabin);
                previewCam.fieldOfView = 30f;
            }
            if (!previewTex)
                previewTex = new RenderTexture(Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height), 24) { antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave };
            var t = gm.car.transform;
            float a = Time.unscaledTime * 25f;
            Vector3 look = t.position + Vector3.up * 0.7f;
            Vector3 dir = Quaternion.Euler(0, gm.car.yaw + 35f + a, 0) * Vector3.forward;
            previewCam.transform.SetPositionAndRotation(look + dir * 10.5f + Vector3.up * 2.6f, Quaternion.LookRotation(-dir * 10.5f - Vector3.up * 2.6f));
            previewCam.targetTexture = previewTex;
            previewCam.Render();
        }
    }
}
