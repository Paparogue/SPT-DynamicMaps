// ============================================================================
// CHANGES TO Plugin/UI/ModdedMapScreen.cs
// ============================================================================
//
// 1. Add this using statement at the top of the file (with the other usings):
//
//    No new usings needed — BossAreaMarkerProvider is in DynamicMaps.DynamicMarkers
//    which is already imported.
//
// 2. In the ReadConfig() method, REPLACE the boss-marker wiring section.
//
//    Find this block (around line ~590 in the original):
//
//  ---- OLD CODE (REMOVE) -------------------------------------------------
//
//            // other player markers
//            var needOtherPlayerMarkers = Settings.ShowFriendlyPlayerMarkersInRaid.Value
//                                      || Settings.ShowEnemyPlayerMarkersInRaid.Value
//                                      || Settings.ShowBossMarkersInRaid.Value
//                                      || Settings.ShowScavMarkersInRaid.Value;
//
//            AddRemoveMarkerProvider<OtherPlayersMarkerProvider>(needOtherPlayerMarkers);
//            
//            if (needOtherPlayerMarkers)
//            {
//                var provider = GetMarkerProvider<OtherPlayersMarkerProvider>();
//                provider.ShowFriendlyPlayers = _serverConfig.allowShowFriendlyPlayerMarkersInRaid ? Settings.ShowFriendlyPlayerMarkersInRaid.Value : false;
//                provider.ShowEnemyPlayers = _serverConfig.allowShowEnemyPlayerMarkersInRaid ? Settings.ShowEnemyPlayerMarkersInRaid.Value : false;
//                provider.ShowScavs = _serverConfig.allowShowScavMarkersInRaid ? Settings.ShowScavMarkersInRaid.Value : false;
//                provider.ShowBosses = _serverConfig.allowShowBossMarkersInRaid ? Settings.ShowBossMarkersInRaid.Value : false;
//                
//                provider.RefreshMarkers();
//            }
//
//  ---- NEW CODE (INSERT) --------------------------------------------------
//
//            // boss area markers (separate provider — shows approximate area, not exact position)
//            var needBossAreaMarkers = _serverConfig.allowShowBossMarkersInRaid
//                                   && Settings.ShowBossMarkersInRaid.Value;
//            AddRemoveMarkerProvider<BossAreaMarkerProvider>(needBossAreaMarkers);
//            if (needBossAreaMarkers)
//            {
//                var bossProvider = GetMarkerProvider<BossAreaMarkerProvider>();
//                bossProvider.UpdateAreaRadius();
//                bossProvider.RefreshMarkers();
//            }
//
//            // other player markers (friendly, enemy PMCs, scavs — bosses excluded)
//            var needOtherPlayerMarkers = Settings.ShowFriendlyPlayerMarkersInRaid.Value
//                                      || Settings.ShowEnemyPlayerMarkersInRaid.Value
//                                      || Settings.ShowScavMarkersInRaid.Value;
//
//            AddRemoveMarkerProvider<OtherPlayersMarkerProvider>(needOtherPlayerMarkers);
//            
//            if (needOtherPlayerMarkers)
//            {
//                var provider = GetMarkerProvider<OtherPlayersMarkerProvider>();
//                provider.ShowFriendlyPlayers = _serverConfig.allowShowFriendlyPlayerMarkersInRaid ? Settings.ShowFriendlyPlayerMarkersInRaid.Value : false;
//                provider.ShowEnemyPlayers = _serverConfig.allowShowEnemyPlayerMarkersInRaid ? Settings.ShowEnemyPlayerMarkersInRaid.Value : false;
//                provider.ShowScavs = _serverConfig.allowShowScavMarkersInRaid ? Settings.ShowScavMarkersInRaid.Value : false;
//                
//                provider.RefreshMarkers();
//            }
//
//  ---- END OF CHANGE ------------------------------------------------------
//
// That's the only change needed in ModdedMapScreen.cs.
// Summary of what changed:
//   - Bosses are now handled by a separate BossAreaMarkerProvider
//   - The needOtherPlayerMarkers check no longer includes ShowBossMarkersInRaid
//   - provider.ShowBosses line removed (property no longer exists)
//   - New BossAreaMarkerProvider block added before the other-player block
