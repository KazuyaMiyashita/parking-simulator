using System.Collections.Generic;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>画面表示(IMGUI)。1920x1080 を基準に拡大縮小する。</summary>
    public class HUD : MonoBehaviour
    {
        public GameManager gm;
        public bool menuOpen = true;
        public bool helpOpen;
        public bool showGuideInfo = true;
        public bool showMirrorsHud = true;

        int startSel;
        Vector2 scroll;
        float vw, vh = 1080f;

        Font font;
        bool styled;
        GUIStyle panel, label, small, big, huge, title, centerLabel, header;
        Texture2D panelTex, solidTex, frameTex;
        GUIStyle solid;

        struct ToastMsg { public string text; public Color color; public float until; }
        readonly List<ToastMsg> toasts = new List<ToastMsg>();

        public void Toast(string text, Color c, float dur = 2.5f)
        {
            toasts.RemoveAll(t => t.text == text);
            toasts.Add(new ToastMsg { text = text, color = c, until = Time.unscaledTime + dur });
            if (toasts.Count > 4) toasts.RemoveAt(0);
        }

        public void SetMenu(bool open)
        {
            menuOpen = open;
            if (open && gm) startSel = gm.startIndex;
            if (gm) gm.ApplyInputLock();
            // メニュー表示中はゲームの時間と音を完全に止める
            Time.timeScale = open ? 0f : 1f;
            AudioListener.pause = open;
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        void Start()
        {
            if (menuOpen) SetMenu(true);
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

            if (gm.rig.MirrorsActive && showMirrorsHud) DrawMirrors();
            if (gm.rig.BackMonitorVisible) DrawBackMonitor();
            DrawTopInfo();
            DrawInstruments();
            if (gm.showSensors) DrawSensors();
            DrawToasts();
            if (gm.showResult && gm.lastResult != null && !menuOpen && !helpOpen) DrawResult();
            if (helpOpen && !menuOpen) DrawHelp();
            if (menuOpen) DrawMenu();
        }

        void DrawTopInfo()
        {
            var car = gm.car;
            string mode = gm.rig.mode == ViewMode.Top2D ? "2D 俯瞰" + (gm.rig.wide ? "(広域)" : "")
                        : gm.rig.mode == ViewMode.Driver3D ? "3D 運転席" : "3D 外から";
            float h = 130f;
            JudgeResult live = null;
            if (showGuideInfo)
            {
                live = gm.nearSpot != null ? ParkingJudge.Measure(gm.nearSpot, car) : null;
                if (live != null && live.nearTarget) h += live.spot.parallel ? 84f : 58f;
                else live = null;
            }
            GUILayout.BeginArea(new Rect(16, 16, 620, h), panel);
            GUILayout.Label($"{CarSpec.DisplayName} 駐車シミュレーター", title);
            GUILayout.Label($"視点: {mode}   <color=#9ad>V:視点切替  H:操作説明  Esc:設定  F5:スタート地点へ戻る</color>", small);
            GUILayout.Label($"現在地: <b>{gm.world.ZoneName(car.Center)}</b>" + (gm.nearSpot != null ? $"   <color=#9df>{gm.nearSpot.name}</color>" : ""), small);
            GUILayout.Label(Pad.Status() + (Pad.Connected ? "   <color=#9ad>−: 操作説明</color>" : "   <color=#888>(未接続)</color>"), small);
            if (live != null)
            {
                string M(float v) => v >= 0 ? $"{v * 100:0}cm" : $"<color=#f66>{v * 100:0}cm</color>";
                string a = Mathf.Abs(live.angleErr) < 1.5f ? $"{live.angleErr:+0.0;-0.0;0.0}°" : $"<color=#fc6>{live.angleErr:+0.0;-0.0}°</color>";
                GUILayout.Label($"<b>位置ガイド</b>  左余白 {M(live.leftMargin)}   右余白 {M(live.rightMargin)}   角度 {a}", label);
                if (live.spot.parallel)
                    GUILayout.Label($"縁石まで {live.curbDist * 100:0}cm   枠はみ出し {(live.protrusion > 0 ? $"<color=#f66>{live.protrusion * 100:0}cm</color>" : "なし")}", label);
                else if (live.protrusion > 0)
                    GUILayout.Label($"<color=#f66>通路側にはみ出し {live.protrusion * 100:0}cm</color>", small);
            }
            GUILayout.EndArea();
        }

        void DrawInstruments()
        {
            var car = gm.car;
            var r = new Rect(16, vh - 236, 640, 220);
            GUI.Box(r, GUIContent.none, panel);

            // 速度
            float kmh = car.SpeedKmh;
            GUI.Label(new Rect(r.x + 16, r.y + 8, 260, 50), $"{Mathf.Abs(kmh):0.0} <size=20>km/h</size>", big);
            GUI.Label(new Rect(r.x + 16, r.y + 48, 260, 26), kmh > 0.05f ? "▲ 前進" : kmh < -0.05f ? "▼ 後退" : "停止", small);

            // シフト
            string[] g = { "P", "R", "N", "D" };
            for (int i = 0; i < 4; i++)
            {
                bool on = (int)car.gear == i;
                var gr = new Rect(r.x + 16 + i * 46, r.y + 78, 40, 40);
                GUI.color = on ? new Color(1f, 0.85f, 0.2f) : new Color(1, 1, 1, 0.3f);
                GUI.DrawTexture(gr, Texture2D.whiteTexture);
                GUI.color = Color.white;
                var st = new GUIStyle(centerLabel) { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = on ? Color.black : Color.white } };
                GUI.Label(gr, g[i], st);
            }
            if (car.parkingBrake)
                GUI.Label(new Rect(r.x + 200, r.y + 84, 100, 30), "<color=#f44><b>(P)</b></color>", label);

            // ペダル
            Bar(new Rect(r.x + 16, r.y + 154, 200, 14), car.throttle, new Color(0.3f, 0.9f, 0.4f), "アクセル");
            Bar(new Rect(r.x + 16, r.y + 194, 200, 14), car.brake, new Color(1f, 0.35f, 0.3f), "ブレーキ");

            // ハンドル
            Vector2 c = new Vector2(r.x + 370, r.y + 110);
            DrawSteeringIcon(c, 62f, car.wheelDeg);
            float turns = car.wheelDeg / 360f;
            string dir = Mathf.Abs(car.wheelDeg) < 1f ? "中立" : car.wheelDeg > 0 ? "右" : "左";
            float radius = CarSpec.OuterFrontRadius(car.SteerDeg);
            bool full = Mathf.Abs(car.wheelDeg) >= CarSpec.MaxWheelDeg - 0.5f;
            GUILayout.BeginArea(new Rect(r.x + 448, r.y + 12, 190, 200));
            GUILayout.Label("ハンドル", small);
            GUILayout.Label($"<b>{dir} {Mathf.Abs(turns):0.00}回転</b>", label);
            GUILayout.Label($"{(full ? "<color=#fc6>フルロック</color>" : $"{Mathf.Abs(car.wheelDeg):0}°")}", small);
            GUILayout.Label($"タイヤ {car.SteerDeg:+0.0;-0.0;0.0}°", small);
            GUILayout.Label(float.IsInfinity(radius) ? "回転半径 ∞" : $"回転半径 {radius:0.00}m", small);
            GUILayout.Label($"接触{car.collisions} 脱輪{car.curbHits} 切返{car.directionChanges}", small);
            GUILayout.Label($"{(car.mouseSteer ? "<color=#9df>マウス操舵</color>" : "")}", small);
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
                ? new Rect(vw / 2 - 240, vh - 330, 480, 300)
                : new Rect(vw - 552, vh - 690, 520, 325);
            GUI.DrawTexture(new Rect(r.x - 6, r.y - 30, r.width + 12, r.height + 36), frameTex);
            GUI.DrawTextureWithTexCoords(r, car.parts.rtBack, new Rect(1, 0, -1, 1)); // バックモニターは鏡像表示
            GUI.Label(new Rect(r.x + 4, r.y - 28, r.width, 24), "<size=15>バックモニター  後方を直接確認してください</size>", small);

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
            if (gm.showPath)
            {
                Polyline(cam, r, gm.path.rearLeft, new Color(1f, 0.6f, 0.1f), 3f);
                Polyline(cam, r, gm.path.rearRight, new Color(1f, 0.6f, 0.1f), 3f);
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
            float y = gm.rig.MirrorsActive && showMirrorsHud ? 170 : 24;
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

        void DrawHelp()
        {
            var r = new Rect(vw / 2 - 420, 60, 840, 620);
            GUILayout.BeginArea(r, solid);
            GUILayout.Label("操作説明", title);
            GUILayout.Label("<color=#fd6><b>キーボード</b></color>", label);
            string[] lines =
            {
                "<b>W / ↑</b>  アクセル(押している間だけ踏み込みが増える)",
                "<b>S / ↓</b>  ブレーキ(軽く押すとクリープ速度を調整)   <b>Space</b>  強くブレーキ",
                "<b>A D / ← →</b>  ハンドル(Shift で速く回す)   <b>C</b>  ハンドルを中立に戻す",
                "<b>M</b>  マウスハンドル(画面上のマウスの左右位置がハンドル角)",
                "<b>1 / 2 / 3 / 4</b>  シフト P / R / N / D    <b>Q / E</b>  シフトを1段上/下",
                "<b>B</b>  電動パーキングブレーキ(アクセルで自動解除)",
                "<b>V / Tab</b>  視点切替(2D 俯瞰 → 3D 運転席 → 3D 外から)",
                "2D: <b>ホイール / + -</b> ズーム, <b>F</b> 広域表示",
                "3D運転席: <b>右ドラッグ</b> 見回す, <b>Z</b> 左を見る, <b>X</b> 右を見る, <b>Z+X</b> 振り返る, <b>ホイール</b> 視野角",
                "<b>G</b> 予想進路線   <b>K</b> バックモニター   <b>O</b> センサー音   <b>L</b> 位置ガイド",
                "<b>Enter</b>  近くの駐車枠で判定(P に入れても自動判定)   <b>F5 / Backspace</b>  スタート地点へ戻る",
                "<b>Esc / F1</b>  設定メニュー   <b>H</b>  この画面を閉じる",
                "<color=#fd6><b>Joy-Con (L)+(R) / ゲームパッド</b></color>",
                "<b>Lスティック</b>  ハンドル(倒した量で回す速さ)   <b>Lスティック押し / 十字↓</b>  中立に戻す",
                "<b>ZR</b> アクセル   <b>ZL</b> ブレーキ   <b>L</b> 強くブレーキ   <b>R</b> パーキングブレーキ",
                "<b>X / B / Y / A</b>  シフト P / R / N / D",
                "<b>十字↑</b> 視点切替   <b>十字← →</b> 左右を見る   <b>右スティック</b> 見回す(2D はズーム)",
                "<b>Rスティック押し</b> 判定   <b>キャプチャー</b> スタート地点へ戻る   <b>+</b> 設定   <b>−</b> この画面",
                "設定メニュー: <b>十字</b> スタート地点を選ぶ, <b>A</b> スタート, <b>B / +</b> 閉じる",
                "<color=#9ad>D/R に入れてブレーキを離すとクリープで動き出します。</color>",
                "<color=#9ad>最小回転半径 5.3m(ハンドルいっぱいで外側前輪がえがく円の半径)を再現しています。</color>",
            };
            foreach (var l in lines) GUILayout.Label(l, l.StartsWith("<color=#9ad") ? small : label);
            GUILayout.EndArea();
        }

        void DrawResult()
        {
            var res = gm.lastResult;
            var r = new Rect(vw / 2 - 330, 170, 660, 560);
            GUILayout.BeginArea(r, solid);
            GUILayout.Label($"駐車判定  <size=18>{res.spotName}</size>", title);
            GUILayout.BeginHorizontal();
            var rankCol = res.rank == "S" ? "#ffd84a" : res.rank == "A" ? "#7df" : res.rank == "B" ? "#9e9" : res.rank == "-" ? "#aaa" : "#f99";
            GUILayout.Label($"<color={rankCol}>{res.rank}</color>", huge, GUILayout.Width(150), GUILayout.Height(100));
            GUILayout.BeginVertical();
            GUILayout.Space(12);
            GUILayout.Label($"<size=40><b>{res.score}</b></size> 点", label);
            GUILayout.Label(res.nearTarget ? (res.inside ? "<color=#9e9>枠内に駐車できました</color>" : "<color=#fc6>枠からはみ出しています</color>") : "", label);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            if (res.nearTarget)
            {
                var s = res.spot;
                GUILayout.Label($"左余白 {res.leftMargin * 100:0} cm   右余白 {res.rightMargin * 100:0} cm   (白線内側まで)", label);
                GUILayout.Label($"角度のずれ {res.angleErr:+0.0;-0.0;0.0}°   " +
                                (s.parallel ? $"縁石まで {res.curbDist * 100:0} cm" : $"通路側 {(res.protrusion > 0 ? $"{res.protrusion * 100:0}cm はみ出し" : $"{-res.protrusion * 100:0}cm 内側")}"), label);
            }
            GUILayout.Label($"接触 {res.collisions} 回   脱輪 {res.curbHits} 回   切り返し {res.switches} 回   時間 {res.time:0.0} 秒", label);
            GUILayout.Space(6);
            foreach (var n in res.notes) GUILayout.Label("・" + n, label);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("続ける (Enter / A)", GUILayout.Height(44))) gm.showResult = false;
            if (GUILayout.Button("やり直し (F5 / Y)", GUILayout.Height(44))) gm.Respawn();
            if (GUILayout.Button("設定 (+)", GUILayout.Height(44))) { gm.showResult = false; SetMenu(true); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ---------------------------------------------------------------- 設定メニュー
        void DrawMenu()
        {
            var starts = gm.world.starts;
            var names = new string[starts.Count];
            for (int i = 0; i < names.Length; i++) names[i] = starts[i].name;
            var r = new Rect(vw / 2 - 560, 60, 1120, 900);
            GUI.DrawTexture(new Rect(0, 0, vw, vh), panelTex);
            GUILayout.BeginArea(r, solid);
            GUILayout.Label("駐車シミュレーター 設定", title);
            GUILayout.Label($"自車: {CarSpec.DisplayName}  {CarSpec.ModelCode}  {CarSpec.ColorName}   " +
                            $"全長 {CarSpec.Length * 1000:#,0}mm  全幅 {CarSpec.Width * 1000:#,0}mm  最小回転半径 {CarSpec.MinTurningRadius}m", small);
            GUILayout.Label("<color=#9ad>時間が止まった町で、自分だけが運転の練習をできます。自宅の月極駐車場・公道の十字路・コンビニ・自動車教習所(S字/クランク/方向転換/縦列駐車)を自由に走れます。</color>", new GUIStyle(small) { wordWrap = true });
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("スタート地点", header);
            startSel = GUILayout.SelectionGrid(Mathf.Clamp(startSel, 0, names.Length - 1), names, 3, GUILayout.Height(40 * Mathf.CeilToInt(names.Length / 3f)), GUILayout.Width(1050));
            GUILayout.Label("<color=#9ad>どの地点も P・パーキングブレーキ ON で停車した状態から始まります。F5 / キャプチャーボタンで選んだ地点に戻ります。</color>", small);
            GUILayout.Space(8);
            GUILayout.Label("補助機能", header);
            GUILayout.BeginHorizontal();
            gm.showPath = GUILayout.Toggle(gm.showPath, " 予想進路線 (G)", GUILayout.Width(250));
            gm.rig.backMonitor = GUILayout.Toggle(gm.rig.backMonitor, " バックモニター (K)", GUILayout.Width(250));
            gm.sensorSound = GUILayout.Toggle(gm.sensorSound, " センサー音 (O)", GUILayout.Width(220));
            gm.showSensors = GUILayout.Toggle(gm.showSensors, " センサー表示");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            showGuideInfo = GUILayout.Toggle(showGuideInfo, " 位置ガイド (L)", GUILayout.Width(250));
            showMirrorsHud = GUILayout.Toggle(showMirrorsHud, " ミラーを画面に表示(3D)", GUILayout.Width(330));
            gm.rig.mirrorTiltInReverse = GUILayout.Toggle(gm.rig.mirrorTiltInReverse, " リバース連動ドアミラー");
            GUILayout.EndHorizontal();
            gm.rig.driverFov = Slider("運転席の視野角(縦)", gm.rig.driverFov, 35f, 90f, 1f, "°");
            gm.steerRate = Slider("キーでハンドルを回す速さ", gm.steerRate, 180f, 900f, 10f, "°/秒");
            GUILayout.EndScrollView();

            GUILayout.Label("<color=#9ad>Joy-Con: 十字← → スタート地点 / A この地点からスタート / B・+ 閉じる</color>", small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<b>この地点からスタート (A)</b>", new GUIStyle(GUI.skin.button) { richText = true, fontSize = 22 }, GUILayout.Height(54)))
                StartFromSelection();
            if (GUILayout.Button("閉じる (Esc / +)", GUILayout.Height(54), GUILayout.Width(220))) SetMenu(false);
            if (GUILayout.Button("終了", GUILayout.Height(54), GUILayout.Width(120))) Application.Quit();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void StartFromSelection()
        {
            gm.startIndex = startSel;
            SetMenu(false);
            gm.Respawn();
        }

        /// <summary>メニュー表示中の Joy-Con / ゲームパッド操作</summary>
        public void PadMenuInput()
        {
            if (!menuOpen || !gm) return;
            int n = gm.world.starts.Count;
            if (Pad.Down(JoyButton.Right) || Pad.Down(JoyButton.Left))
                startSel = (startSel + (Pad.Down(JoyButton.Right) ? 1 : -1) + n) % n;
            if (Pad.Down(JoyButton.Down)) startSel = Mathf.Min(n - 1, startSel + 3);
            if (Pad.Down(JoyButton.Up)) startSel = Mathf.Max(0, startSel - 3);
            if (Pad.Down(JoyButton.A)) StartFromSelection();
            else if (Pad.Down(JoyButton.B)) SetMenu(false);
        }

        float Slider(string name, float v, float min, float max, float step, string unit)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, GUILayout.Width(250));
            if (GUILayout.Button("−", GUILayout.Width(40))) v -= step;
            GUILayout.BeginVertical(GUILayout.Width(440));
            GUILayout.Space(12);
            v = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(440));
            GUILayout.EndVertical();
            if (GUILayout.Button("+", GUILayout.Width(40))) v += step;
            v = Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
            string fmt = step >= 1f ? "0" : step >= 0.1f ? "0.0" : "0.00";
            GUILayout.Label($" {v.ToString(fmt)} {unit}", GUILayout.Width(140));
            GUILayout.EndHorizontal();
            return v;
        }
    }
}
