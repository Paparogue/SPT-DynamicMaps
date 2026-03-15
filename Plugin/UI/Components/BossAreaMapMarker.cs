using System.Collections.Generic;
using DynamicMaps.Config;
using DynamicMaps.Utils;
using EFT;
using UnityEngine;

namespace DynamicMaps.UI.Components
{
    public class BossAreaMapMarker : MapMarker
    {
        private static float _checkInterval = 2f; // seconds between grid-cell checks
        private static Sprite _circleSprite;

        public IPlayer Boss { get; private set; }

        private float _gridSize;
        private Vector3 _lastSnappedPosition;
        private float _checkTimer = 0f;

        public BossAreaMapMarker()
        {
            // area markers are always somewhat visible regardless of layer
            ImageAlphaLayerStatus[LayerStatus.Hidden] = 0.15f;
            ImageAlphaLayerStatus[LayerStatus.Underneath] = 0.25f;
            ImageAlphaLayerStatus[LayerStatus.OnTop] = 1f;
            ImageAlphaLayerStatus[LayerStatus.FullReveal] = 1f;

            LabelAlphaLayerStatus[LayerStatus.Hidden] = 0.0f;
            LabelAlphaLayerStatus[LayerStatus.Underneath] = 0.0f;
            LabelAlphaLayerStatus[LayerStatus.OnTop] = 0.0f;
            LabelAlphaLayerStatus[LayerStatus.FullReveal] = 1.0f;
        }

        /// <summary>
        /// Creates a boss area marker — a semi-transparent circle whose position is snapped to a
        /// grid so that it only moves when the boss enters a different grid cell.
        /// </summary>
        public static BossAreaMapMarker Create(IPlayer boss, GameObject parent, Color color,
                                               string name, float areaRadius, float degreesRotation)
        {
            var mapPos = MathUtils.ConvertToMapPosition(boss.Position);
            var snapped = SnapPositionToGrid(mapPos, areaRadius);
            var diameter = areaRadius * 2f;
            var pivot = new Vector2(0.5f, 0.5f);

            var marker = Create<BossAreaMapMarker>(
                parent, name, "Boss Area", "",
                color, snapped, new Vector2(diameter, diameter),
                pivot, degreesRotation, 1f, true, GetCircleSprite());

            marker.Boss = boss;
            marker._gridSize = areaRadius;
            marker._lastSnappedPosition = snapped;
            marker.IsDynamic = true;
            marker.IsWorldScale = true;

            return marker;
        }

        private void LateUpdate()
        {
            if (Boss?.Transform?.Original == null)
            {
                return;
            }

            _checkTimer += Time.deltaTime;
            if (_checkTimer < _checkInterval)
            {
                return;
            }

            _checkTimer = 0f;

            var mapPos = MathUtils.ConvertToMapPosition(Boss.Position);
            var snapped = SnapPositionToGrid(mapPos, _gridSize);

            // only move the marker when the boss enters a different grid cell
            if (!MathUtils.ApproxEquals(snapped.x, _lastSnappedPosition.x)
             || !MathUtils.ApproxEquals(snapped.y, _lastSnappedPosition.y)
             || !MathUtils.ApproxEquals(snapped.z, _lastSnappedPosition.z))
            {
                _lastSnappedPosition = snapped;
                Move(snapped);
            }
        }

        /// <summary>
        /// Updates the visual radius when the config value changes at runtime.
        /// </summary>
        public void UpdateAreaRadius(float newRadius)
        {
            _gridSize = newRadius;
            var diameter = newRadius * 2f;
            Size = new Vector2(diameter, diameter);

            // re-snap to the new grid immediately
            if (Boss?.Transform?.Original != null)
            {
                var mapPos = MathUtils.ConvertToMapPosition(Boss.Position);
                var snapped = SnapPositionToGrid(mapPos, _gridSize);
                _lastSnappedPosition = snapped;
                Move(snapped);
            }
        }

        // ── helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Rounds <paramref name="pos"/> to the nearest grid-cell centre.
        /// Grid cell size equals <paramref name="gridSize"/> so the boss is
        /// always within one radius of the circle centre.
        /// </summary>
        private static Vector3 SnapPositionToGrid(Vector3 pos, float gridSize)
        {
            if (gridSize <= 0f)
            {
                return pos;
            }

            return new Vector3(
                Mathf.Round(pos.x / gridSize) * gridSize,
                Mathf.Round(pos.y / gridSize) * gridSize,
                pos.z);
        }

        /// <summary>
        /// Lazily creates (and caches) a white semi-transparent circle sprite.
        /// The marker's <see cref="MapMarker.Color"/> tints it at runtime.
        /// </summary>
        internal static Sprite GetCircleSprite()
        {
            if (_circleSprite != null)
            {
                return _circleSprite;
            }

            const int res = 128;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            var center = new Vector2(res / 2f, res / 2f);
            var radius = res / 2f - 1f;
            var borderWidth = 4f;

            for (var y = 0; y < res; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var dist = Vector2.Distance(new Vector2(x, y), center);

                    if (dist <= radius - borderWidth)
                    {
                        // semi-transparent fill
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, 0.18f));
                    }
                    else if (dist <= radius)
                    {
                        // slightly more opaque border ring
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, 0.55f));
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }

            tex.Apply();
            _circleSprite = Sprite.Create(
                tex,
                new Rect(0, 0, res, res),
                new Vector2(0.5f, 0.5f));

            return _circleSprite;
        }
    }
}
