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
        private Dictionary<Player, BossAreaMapMarker> _bossMarkers = [];

        public void OnShowInRaid(MapView map)
        {
            _lastMapView = map;

            TryAddMarkers();
            RemoveNonActiveBosses();

            Singleton<GameWorld>.Instance.OnPersonAdd += TryAddMarker;
            GameWorldUnregisterPlayerPatch.OnUnregisterPlayer += OnUnregisterPlayer;
            PlayerOnDeadPatch.OnDead += TryRemoveMarker;
        }

        public void OnHideInRaid(MapView map)
        {
            Singleton<GameWorld>.Instance.OnPersonAdd -= TryAddMarker;
            GameWorldUnregisterPlayerPatch.OnUnregisterPlayer -= OnUnregisterPlayer;
            PlayerOnDeadPatch.OnDead -= TryRemoveMarker;
        }

        public void OnRaidEnd(MapView map)
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld is not null)
            {
                gameWorld.OnPersonAdd -= TryAddMarker;
            }

            GameWorldUnregisterPlayerPatch.OnUnregisterPlayer -= OnUnregisterPlayer;
            PlayerOnDeadPatch.OnDead -= TryRemoveMarker;

            TryRemoveMarkers();
        }

        public void OnMapChanged(MapView map, MapDef mapDef)
        {
            _lastMapView = map;

            foreach (var boss in _bossMarkers.Keys.ToList())
            {
                TryRemoveMarker(boss);
                TryAddMarker(boss);
            }
        }

        public void OnDisable(MapView map)
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld is not null)
            {
                gameWorld.OnPersonAdd -= TryAddMarker;
            }

            GameWorldUnregisterPlayerPatch.OnUnregisterPlayer -= OnUnregisterPlayer;
            PlayerOnDeadPatch.OnDead -= TryRemoveMarker;

            TryRemoveMarkers();
        }

        public void RefreshMarkers()
        {
            if (!GameUtils.IsInRaid()) return;

            foreach (var boss in _bossMarkers.Keys.ToList())
            {
                TryRemoveMarker(boss);
                TryAddMarker(boss);
            }
        }

        /// <summary>
        /// Updates the radius of all existing boss area markers (called when config changes).
        /// </summary>
        public void UpdateAreaRadius()
        {
            var radius = Settings.BossAreaRadius.Value;
            foreach (var marker in _bossMarkers.Values)
            {
                marker.UpdateAreaRadius(radius);
            }
        }

        // ── private ─────────────────────────────────────────────────────

        private void TryAddMarkers()
        {
            if (!GameUtils.IsInRaid()) return;

            var gameWorld = Singleton<GameWorld>.Instance;
            foreach (var player in gameWorld.AllAlivePlayersList)
            {
                if (player.IsYourPlayer || _bossMarkers.ContainsKey(player))
                {
                    continue;
                }

                TryAddMarker(player);
            }
        }

        private void TryAddMarker(IPlayer iPlayer)
        {
            var player = iPlayer as Player;
            if (player is null || player.IsHeadlessClient())
            {
                return;
            }

            if (_lastMapView is null || _bossMarkers.ContainsKey(player))
            {
                return;
            }

            // only handle tracked bosses
            if (!player.IsTrackedBoss())
            {
                return;
            }

            var intelLevel = GameUtils.GetIntelLevel();
            if (Settings.ShowBossIntelLevel.Value > intelLevel)
            {
                return;
            }

            var color = Settings.BossColor.Value;
            var radius = Settings.BossAreaRadius.Value;

            var marker = _lastMapView.AddBossAreaMarker(
                player, "Boss Area", color, radius);

            _bossMarkers[player] = marker;
        }

        private void OnUnregisterPlayer(IPlayer iPlayer)
        {
            var player = iPlayer as Player;
            if (player is not null)
            {
                TryRemoveMarker(player);
            }
        }

        private void TryRemoveMarker(Player player)
        {
            if (!_bossMarkers.ContainsKey(player))
            {
                return;
            }

            _bossMarkers[player].ContainingMapView.RemoveMapMarker(_bossMarkers[player]);
            _bossMarkers.Remove(player);
        }

        private void TryRemoveMarkers()
        {
            foreach (var boss in _bossMarkers.Keys.ToList())
            {
                TryRemoveMarker(boss);
            }

            _bossMarkers.Clear();
        }

        private void RemoveNonActiveBosses()
        {
            var alivePlayers = new HashSet<Player>(Singleton<GameWorld>.Instance.AllAlivePlayersList);
            foreach (var boss in _bossMarkers.Keys.ToList())
            {
                if (boss.HasCorpse() || !alivePlayers.Contains(boss))
                {
                    TryRemoveMarker(boss);
                }
            }
        }

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
