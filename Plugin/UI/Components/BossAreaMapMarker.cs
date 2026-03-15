using System.Collections.Generic;
using System.Linq;
using DynamicMaps.Config;
using DynamicMaps.Utils;
using EFT;
using UnityEngine;

namespace DynamicMaps.UI.Components
{
    public class BossAreaMapMarker : MapMarker
    {
        private static float _checkInterval = 2f; // seconds between grid-cell checks

        // Cache keyed by resolution so a config-driven opacity change
        // regenerates the sprite while still caching within the same session.
        private static Dictionary<float, Sprite> _circleSpriteCache = new();

        /// <summary>
        /// All bosses represented by this marker.  A single marker may cover
        /// more than one boss when their circles overlap on the same layer.
        /// </summary>
        public List<IPlayer> Bosses { get; private set; } = new();

        private float _baseRadius;
        private Vector3 _lastComputedCenter;
        private float _lastComputedRadius;
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
        /// Creates a boss area marker — a semi-transparent circle whose position
        /// is derived from one or more bosses.  When multiple bosses are present
        /// their individual grid-snapped circles are merged into a single larger
        /// circle that covers all of them.
        /// </summary>
        public static BossAreaMapMarker Create(List<IPlayer> bosses, GameObject parent, Color color,
                                               string name, float areaRadius, float degreesRotation)
        {
            var (center, radius) = ComputeMergedCircle(bosses, areaRadius);
            var diameter = radius * 2f;
            var pivot = new Vector2(0.5f, 0.5f);

            var marker = Create<BossAreaMapMarker>(
                parent, name, "Boss Area", "",
                color, center, new Vector2(diameter, diameter),
                pivot, degreesRotation, 1f, true, GetCircleSprite());

            marker.Bosses = new List<IPlayer>(bosses);
            marker._baseRadius = areaRadius;
            marker._lastComputedCenter = center;
            marker._lastComputedRadius = radius;
            marker.IsDynamic = true;
            marker.IsWorldScale = true;

            return marker;
        }

        private void LateUpdate()
        {
            // Only consider bosses whose transforms are still alive
            var valid = Bosses.Where(b => b?.Transform?.Original != null).ToList();
            if (valid.Count == 0)
            {
                return;
            }

            _checkTimer += Time.deltaTime;
            if (_checkTimer < _checkInterval)
            {
                return;
            }

            _checkTimer = 0f;

            var (center, radius) = ComputeMergedCircle(valid, _baseRadius);

            bool posChanged = !MathUtils.ApproxEquals(center.x, _lastComputedCenter.x)
                           || !MathUtils.ApproxEquals(center.y, _lastComputedCenter.y)
                           || !MathUtils.ApproxEquals(center.z, _lastComputedCenter.z);
            bool sizeChanged = !MathUtils.ApproxEquals(radius, _lastComputedRadius);

            if (posChanged || sizeChanged)
            {
                _lastComputedCenter = center;
                _lastComputedRadius = radius;
                var diameter = radius * 2f;
                Size = new Vector2(diameter, diameter);
                Move(center);
            }
        }

        /// <summary>
        /// Updates the base radius when the config value changes at runtime.
        /// </summary>
        public void UpdateAreaRadius(float newRadius)
        {
            _baseRadius = newRadius;

            var valid = Bosses.Where(b => b?.Transform?.Original != null).ToList();
            if (valid.Count > 0)
            {
                var (center, radius) = ComputeMergedCircle(valid, _baseRadius);
                _lastComputedCenter = center;
                _lastComputedRadius = radius;
                var diameter = radius * 2f;
                Size = new Vector2(diameter, diameter);
                Move(center);
            }
        }

        /// <summary>
        /// Called when the opacity config value changes.  Invalidates the
        /// cached sprite so the next GetCircleSprite() rebuilds it.
        /// Also refreshes the current marker's sprite immediately.
        /// </summary>
        public void UpdateOpacity()
        {
            // Clear the cache so it regenerates with the new opacity
            _circleSpriteCache.Clear();
            
            // Update our own image sprite
            if (Image != null)
            {
                Image.sprite = GetCircleSprite();
            }
        }

        // ── helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Computes a single circle that covers all supplied bosses.
        /// Each boss position is grid-snapped first; the result is a
        /// minimum enclosing circle centred on their centroid.
        /// </summary>
        private static (Vector3 center, float radius) ComputeMergedCircle(
            IEnumerable<IPlayer> bosses, float baseRadius)
        {
            var snapped = bosses
                .Where(b => b?.Transform?.Original != null)
                .Select(b => SnapPositionToGrid(
                    MathUtils.ConvertToMapPosition(b.Position), baseRadius))
                .ToList();

            if (snapped.Count == 0)
                return (Vector3.zero, baseRadius);

            if (snapped.Count == 1)
                return (snapped[0], baseRadius);

            // Centroid of all snapped positions
            var centroid = Vector3.zero;
            foreach (var p in snapped) centroid += p;
            centroid /= snapped.Count;

            // Radius = furthest snapped point from centroid + base radius
            float maxDist = 0f;
            foreach (var p in snapped)
            {
                var dist = Vector2.Distance(
                    new Vector2(p.x, p.y),
                    new Vector2(centroid.x, centroid.y));
                if (dist > maxDist) maxDist = dist;
            }

            return (centroid, maxDist + baseRadius);
        }

        /// <summary>
        /// Rounds <paramref name="pos"/> to the nearest grid-cell centre.
        /// Grid cell size equals <paramref name="gridSize"/> so a boss is
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
        /// Uses a high-resolution texture with anti-aliased edges to avoid
        /// pixelation when the circle is large on the map.
        /// </summary>
        internal static Sprite GetCircleSprite()
        {
            var fillOpacity = Settings.BossAreaOpacity.Value;

            if (_circleSpriteCache.TryGetValue(fillOpacity, out var cached) && cached != null)
            {
                return cached;
            }

            // FIX: Resolution increased from 128 to 512 to eliminate pixelation
            // on large circles.
            const int res = 512;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var center = new Vector2(res / 2f, res / 2f);
            var radius = res / 2f - 1f;
            var borderWidth = 6f;

            // Anti-alias width in pixels — smooths the outer edge and inner
            // border-to-fill transition so it doesn't look jagged.
            var aaWidth = 2.0f;

            // Border opacity scales with fill opacity but stays more visible
            var borderOpacity = Mathf.Clamp01(fillOpacity + 0.35f);

            for (var y = 0; y < res; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var dist = Vector2.Distance(new Vector2(x, y), center);

                    if (dist > radius + aaWidth)
                    {
                        // fully outside — transparent
                        tex.SetPixel(x, y, Color.clear);
                    }
                    else if (dist > radius)
                    {
                        // anti-aliased outer edge
                        var t = 1f - (dist - radius) / aaWidth;
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, borderOpacity * t));
                    }
                    else if (dist > radius - borderWidth)
                    {
                        // border ring
                        // smooth inner edge of border into fill
                        var innerEdgeDist = (radius - borderWidth) - dist;
                        if (innerEdgeDist < -aaWidth)
                        {
                            tex.SetPixel(x, y, new Color(1f, 1f, 1f, borderOpacity));
                        }
                        else
                        {
                            tex.SetPixel(x, y, new Color(1f, 1f, 1f, borderOpacity));
                        }
                    }
                    else
                    {
                        // semi-transparent fill — opacity driven by config
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, fillOpacity));
                    }
                }
            }

            tex.Apply();
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, res, res),
                new Vector2(0.5f, 0.5f));

            _circleSpriteCache[fillOpacity] = sprite;
            return sprite;
        }
    }
}
