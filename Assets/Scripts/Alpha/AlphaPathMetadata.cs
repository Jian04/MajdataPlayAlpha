using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MajdataCore;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Notes.Slide;

namespace MajdataPlay.Alpha
{
    internal static class AlphaPathMetadata
    {
        // D-key partitions use ViewAlpha scene sensor centers, not visual Touch positions.
        static readonly System.Numerics.Vector2[] DKeyJudgeCenters =
        {
            new(1.62650549f, 3.938353f),
            new(3.94485164f, 1.62148356f),
            new(3.94751048f, -1.61941564f),
            new(1.61114442f, -3.936492f),
            new(-1.64038944f, -3.94012547f),
            new(-3.94514728f, -1.62148356f),
            new(-3.936285f, 1.64304817f),
            new(-1.65250111f, 3.948101f),
            new(0.812810063f, 1.96196878f),
            new(1.96196854f, 0.8128096f),
            new(1.96205711f, -0.8125733f),
            new(0.8128096f, -1.96196854f),
            new(-0.8125733f, -1.96205711f),
            new(-1.96196854f, -0.8128096f),
            new(-1.96205711f, 0.8125733f),
            new(-0.8128096f, 1.96196854f),
            new(0f, -0.00187438063f),
            new(1.79302583E-06f, 4.430204f),
            new(3.13248539f, 3.13248539f),
            new(4.430204f, -1.80301515E-06f),
            new(3.13248539f, -3.13248539f),
            new(1.80301049E-06f, -4.430204f),
            new(-3.13248539f, -3.13248539f),
            new(-4.430201f, -1.80301515E-06f),
            new(-3.13248539f, 3.13248539f),
            new(1.80301049E-06f, 3.071927f),
            new(2.17217946f, 2.17217946f),
            new(3.071927f, -1.80301049E-06f),
            new(2.17217946f, -2.17217946f),
            new(1.80301049E-06f, -3.071927f),
            new(-2.17217946f, -2.17217946f),
            new(-3.071927f, -1.80301049E-06f),
            new(-2.17217946f, 2.17217946f),
        };
        internal static Vector3 Position(char area, int index, bool d = false)
        {
            if (area == 'C') return Vector3.zero;
            var radius = area switch { 'K' => 4.8f, 'A' or 'D' => 4.1f, 'B' => 2.3f, 'E' => 3f, _ => 0 };
            var angle = -index * Mathf.PI / 4 + (area is 'K' or 'A' or 'B' ? 5 : 6) * Mathf.PI / 8 + (area == 'K' && d ? Mathf.PI / 8 : 0);
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        internal static SensorArea Sensor(SlidePositionData p) => p.area switch
        {
            'C' => SensorArea.C, 'B' => SensorArea.B1 + p.position - 1,
            'D' => SensorArea.D1 + p.position - 1, 'E' => SensorArea.E1 + p.position - 1,
            'K' when p.isDZone => SensorArea.D1 + p.position - 1,
            _ => SensorArea.A1 + p.position - 1
        };
        internal static ExtendSlideMetadata Build(SlidePathData expression)
        {
            var path = AlphaPathGeometry.Build(expression);
            var distances = new float[path.Count];
            for (var i = 1; i < path.Count; i++) distances[i] = distances[i - 1] + Vector3.Distance(path[i - 1], path[i]);
            var length = distances[distances.Length - 1];
            if (length < .001f) throw new FormatException("轨迹长度为零");
            Vector3 Sample(float distance)
            {
                var upper = Array.BinarySearch(distances, distance);
                if (upper >= 0) return path[upper];
                upper = ~upper;
                if (upper == 0) return path[0];
                if (upper >= path.Count) return path[path.Count - 1];
                return Vector3.Lerp(path[upper - 1], path[upper], (distance - distances[upper - 1]) / Math.Max(.00001f, distances[upper] - distances[upper - 1]));
            }
            var count = Math.Max(2, Mathf.RoundToInt(length / (float)MajdataPlay.Scenes.Game.Parsing.SlideGeo.DefaultDistance));
            var poses = new SlidePose[count + 1];
            for (var i = 0; i <= count; i++)
            {
                var at = length * i / count;
                var pos = Sample(at);
                var tangent = Sample(Math.Min(length, at + .025f)) - Sample(Math.Max(0, at - .025f));
                poses[i] = new SlidePose(pos.x, pos.y, Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg + 180, at);
            }
            var centers = new List<(int Sensor, System.Numerics.Vector2 Center)>();
            var tail = expression.segments[expression.segments.Count - 1].end;
            var keyOnly = expression.head.IsKey && expression.segments.All(s => s.start.IsKey && s.end.IsKey && (!s.hasMiddle || s.middle.IsKey));
            var dKeyRoute = keyOnly && (expression.head.isDZone || tail.isDZone || expression.segments.Any(s => s.start.isDZone || s.end.isDZone || s.hasMiddle && s.middle.isDZone));
            var mixedMiddle = expression.segments.Any(s => s.start.isDZone != expression.head.isDZone || s.end.isDZone != tail.isDZone || s.hasMiddle && s.middle.isDZone != expression.head.isDZone);
            foreach (var area in new[] { 'A', 'B', 'C', 'D', 'E' })
            {
                if (dKeyRoute && !TouchSlideSensorRoute.AllowsKeyPathGroup(area, expression.head.isDZone, tail.isDZone, mixedMiddle)) continue;
                for (var i = 1; i <= (area == 'C' ? 1 : 8); i++)
                {
                    var pos = Position(area, i);
                    var sensor = (int)Sensor(new SlidePositionData { area = area, position = i });
                    centers.Add((sensor, dKeyRoute ? DKeyJudgeCenters[sensor] : new System.Numerics.Vector2(pos.x, pos.y)));
                }
            }
            var points = path.ConvertAll(p => new System.Numerics.Vector2(p.x, p.y));
            var zones = new List<TouchSlideSensorRoute.Zone>(TouchSlideSensorRoute.Build(points, centers, length / count));
            var start = Sensor(expression.head);
            var end = Sensor(expression.segments[expression.segments.Count - 1].end);
            if (zones.Count == 0 || zones[0].Sensor != (int)start) zones.Insert(0, new TouchSlideSensorRoute.Zone((int)start, 0));
            if (zones[zones.Count - 1].Sensor != (int)end) zones.Add(new TouchSlideSensorRoute.Zone((int)end, 1));
            var areas = new ParsingSlideArea[zones.Count];
            for (var i = 0; i < zones.Count; i++)
            {
                var push = Math.Max(0, Mathf.FloorToInt(zones[i].Entry * count));
                var finish = i == zones.Count - 1 ? count + 1 : Math.Max(push, Mathf.FloorToInt(zones[i + 1].Entry * count));
                areas[i] = new ParsingSlideArea(i == zones.Count - 1 ? count + 1 : push, finish, (SensorArea)zones[i].Sensor, null);
            }
            var fraction = Math.Max(1f / count, 1 - zones[zones.Count - 1].Entry);
            var final = poses[poses.Length - 1];
            var angle = (final.RotZ - 180) * Mathf.Deg2Rad;
            var left = final.X < 0;
            var ok = new SlidePose(final.X - Mathf.Cos(angle) * 2.05f, final.Y - Mathf.Sin(angle) * 2.05f, final.RotZ - 180 + (left ? 180 : 0), 0);
            return new ExtendSlideMetadata(areas, fraction, length, poses, false, ok, left ? SlideOkType.StraightL : SlideOkType.StraightR, SlideFlag.None, true, zones.ConvertAll(z => z.Entry).ToArray());
        }
    }
}
