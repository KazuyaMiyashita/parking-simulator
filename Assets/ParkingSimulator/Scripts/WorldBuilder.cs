using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// 練習用の世界をまとめて作る: 公道と十字路、自宅の月極駐車場、コンビニ、自動車教習所。
    /// 歩行者や走っている車はいない(止まった世界で自分だけが運転できる)。
    /// </summary>
    public static class WorldBuilder
    {
        public static WorldResult Build(Transform parent, int seed = 7)
        {
            Kit.InitMaterials();
            var w = new WorldResult { root = new GameObject("World Map").transform };
            w.root.SetParent(parent, false);
            var rng = new System.Random(seed);

            Util.Box("Ground", w.root, new Vector3(0, -0.05f, 20f), new Vector3(420f, 0.1f, 400f), Kit.Grass, null, false, false);
            TownBuilder.Build(w.root, w, rng);
            SchoolBuilder.Build(w.root, w, rng);

            // 交差点まわりの区域名(区域は登録順に判定)
            w.zones.Insert(0, new Zone { name = "十字路の交差点", rect = Rect.MinMaxRect(-16f, -16f, 16f, 16f) });
            w.starts.Insert(1, new StartPoint { name = "交差点の手前(公道)", rearAxle = new Vector3(-48f, 0, 1.7f), yaw = 90f });
            return w;
        }
    }
}
