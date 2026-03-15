using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using DynamicMaps.Config;
using DynamicMaps.Data;
using DynamicMaps.Patches;
using DynamicMaps.UI.Components;
using DynamicMaps.Utils;
using EFT;
using UnityEngine;

namespace DynamicMaps.DynamicMarkers
{
    public class BossAreaMarkerProvider : IDynamicMarkerProvider
    {
        private MapView _lastMapView;

        /// <summary>
        /// Every boss we are currently aware of (alive, passes intel check).
        /// Each boss appears in exactly one cluster marker — never more.
        /// </summary>
        private HashSet<Player> _trackedBosses = new();

        /// <summary>
        /// Active cluster markers on the map.  One marker per cluster of
        /// nearby bosses (or a single boss if no neighbours overlap).
        /// </summary>
        private List<BossAreaMapMarker> _clusterMarkers = new();

        // ── lifecycle ────────────────────────────────────────────────────

        public void OnShowInRaid(MapView map)
        {
            _lastMapView = map;

            DiscoverBosses();

            Singleton<GameWorld>.Instance.OnPersonAdd += OnPersonAdd;
            GameWorldUnregisterPlayerPatch.OnUnregisterPlayer += OnUnregisterPlayer;
            PlayerOnDeadPatch.OnDead += OnBossDead;

            RebuildClusters();
        }

        public void OnHideInRaid(MapView map)
        {
            Singleton<GameWorld>.Instance.OnPersonAdd -= OnPersonAdd;
            GameWorldUnregisterPlayerPatch.OnUnregisterPlayer -= OnUnregisterPlayer;
            PlayerOnDeadPatch.OnDead -= OnBossDead;
        }

        public void OnRaidEnd(MapView map)
        {
            var gw = Singleton<GameWorld>.Instance;
            if (gw != null) gw.OnPersonAdd -= OnPersonAdd;

            GameWorldUnregisterPlayerPatch.OnUnregisterPlayer -= OnUnregisterPlayer;
            PlayerOnDeadPatch.OnDead -= OnBossDead;

            RemoveAllMarkers();
            _trackedBosses.Clear();
        }

        public void OnMapChanged(MapView map, MapDef mapDef)
        {
            _lastMapView = map;
            RebuildClusters();
        }

        public void OnDisable(MapView map)
        {
            var gw = Singleton<GameWorld>.Instance;
            if (gw != null) gw.OnPersonAdd -= OnPersonAdd;

            GameWorldUnregisterPlayerPatch.OnUnregisterPlayer -= OnUnregisterPlayer;
            PlayerOnDeadPatch.OnDead -= OnBossDead;

            RemoveAllMarkers();
            _trackedBosses.Clear();
        }

        public void RefreshMarkers()
        {
            if (!GameUtils.IsInRaid()) return;

            PruneDeadBosses();
            RebuildClusters();
        }

        /// <summary>
        /// Updates the radius of all existing cluster markers (called when config changes).
        /// </summary>
        public void UpdateAreaRadius()
        {
            // A radius change can alter which circles overlap, so rebuild fully.
            RebuildClusters();
        }

        // ── event handlers ───────────────────────────────────────────────

        private void OnPersonAdd(IPlayer iPlayer)
        {
            if (TryTrackBoss(iPlayer as Player))
            {
                RebuildClusters();
            }
        }

        private void OnUnregisterPlayer(IPlayer iPlayer)
        {
            var player = iPlayer as Player;
            if (player != null && _trackedBosses.Remove(player))
            {
                RebuildClusters();
            }
        }

        private void OnBossDead(Player player)
        {
            if (_trackedBosses.Remove(player))
            {
                RebuildClusters();
            }
        }

        // ── discovery / tracking ─────────────────────────────────────────

        private void DiscoverBosses()
        {
            if (!GameUtils.IsInRaid()) return;

            var gameWorld = Singleton<GameWorld>.Instance;
            foreach (var player in gameWorld.AllAlivePlayersList)
            {
                if (player.IsYourPlayer) continue;
                TryTrackBoss(player);
            }
        }

        /// <returns>true if a new boss was added to the tracked set.</returns>
        private bool TryTrackBoss(Player player)
        {
            if (player == null || player.IsHeadlessClient()) return false;
            if (!player.IsTrackedBoss()) return false;
            if (_trackedBosses.Contains(player)) return false;

            var intelLevel = GameUtils.GetIntelLevel();
            if (Settings.ShowBossIntelLevel.Value > intelLevel) return false;

            _trackedBosses.Add(player);
            return true;
        }

        private void PruneDeadBosses()
        {
            var alivePlayers = new HashSet<Player>(
                Singleton<GameWorld>.Instance.AllAlivePlayersList);

            _trackedBosses.RemoveWhere(b => b.HasCorpse() || !alivePlayers.Contains(b));
        }

        // ── marker management ────────────────────────────────────────────

        private void RemoveAllMarkers()
        {
            foreach (var marker in _clusterMarkers)
            {
                marker.ContainingMapView?.RemoveMapMarker(marker);
            }

            _clusterMarkers.Clear();
        }

        /// <summary>
        /// Removes all existing markers, computes boss clusters, and creates
        /// one merged circle marker per cluster.
        /// </summary>
        private void RebuildClusters()
        {
            RemoveAllMarkers();

            if (_lastMapView == null || _trackedBosses.Count == 0) return;

            var radius = Settings.BossAreaRadius.Value;

            // FIX: Merge threshold increased from 1.5× to 2.2× radius.
            // Two circles visually touch when their centre-to-centre distance
            // equals 2× the radius.  Using 2.2× gives a comfortable margin so
            // circles that are touching or barely overlapping always merge into
            // a single large circle instead of appearing as two fat circles.
            var mergeThreshold = radius * 2.2f;

            var aliveBosses = _trackedBosses
                .Where(b => b?.Transform?.Original != null && !b.HasCorpse())
                .ToList();

            if (aliveBosses.Count == 0) return;

            var clusters = BuildClusters(aliveBosses, radius, mergeThreshold);

            var color = Settings.BossColor.Value;

            foreach (var cluster in clusters)
            {
                var bossList = cluster.Cast<IPlayer>().ToList();
                var marker = _lastMapView.AddBossAreaMarker(
                    bossList, "Boss Area", color, radius);

                _clusterMarkers.Add(marker);
            }
        }

        // ── clustering ───────────────────────────────────────────────────

        /// <summary>
        /// Groups bosses whose grid-snapped circles overlap beyond the merge
        /// threshold, but only when they are on the same map layer.
        /// Uses union-find for transitive merging (A overlaps B, B overlaps C
        /// → all three share one circle).
        /// </summary>
        private List<List<Player>> BuildClusters(
            List<Player> bosses, float radius, float mergeThreshold)
        {
            var n = bosses.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            void Union(int a, int b)
            {
                parent[Find(a)] = Find(b);
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var posI = MathUtils.ConvertToMapPosition(((IPlayer)bosses[i]).Position);
                    var posJ = MathUtils.ConvertToMapPosition(((IPlayer)bosses[j]).Position);

                    // Only merge bosses that are on the same map layer
                    if (!AreSameLayer(posI, posJ)) continue;

                    var snappedI = SnapToGrid(posI, radius);
                    var snappedJ = SnapToGrid(posJ, radius);

                    var dist = Vector2.Distance(
                        new Vector2(snappedI.x, snappedI.y),
                        new Vector2(snappedJ.x, snappedJ.y));

                    if (dist < mergeThreshold)
                    {
                        Union(i, j);
                    }
                }
            }

            var groups = new Dictionary<int, List<Player>>();
            for (int i = 0; i < n; i++)
            {
                var root = Find(i);
                if (!groups.ContainsKey(root))
                {
                    groups[root] = new List<Player>();
                }
                groups[root].Add(bosses[i]);
            }

            return groups.Values.ToList();
        }

        /// <summary>
        /// Returns true if both map-space positions resolve to the same layer level.
        /// </summary>
        private bool AreSameLayer(Vector3 mapPosA, Vector3 mapPosB)
        {
            if (_lastMapView == null) return false;

            var levelA = _lastMapView.GetLayerLevelForPosition(mapPosA);
            var levelB = _lastMapView.GetLayerLevelForPosition(mapPosB);

            if (levelA == null || levelB == null) return false;

            return levelA.Value == levelB.Value;
        }

        private static Vector3 SnapToGrid(Vector3 pos, float gridSize)
        {
            if (gridSize <= 0f) return pos;

            return new Vector3(
                Mathf.Round(pos.x / gridSize) * gridSize,
                Mathf.Round(pos.y / gridSize) * gridSize,
                pos.z);
        }

        // ── unused lifecycle hooks ───────────────────────────────────────

        public void OnShowOutOfRaid(MapView map)
        {
            // do nothing
        }

        public void OnHideOutOfRaid(MapView map)
        {
            // do nothing
        }
    }
}
