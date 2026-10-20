using System; using System.Collections.Generic; using UnityEngine; namespace MajdataPlay.Alpha { internal static class AlphaPathCurves {
static Vector3[] BuildStraightRoute(int count,Vector3 start,Vector3 end) { var result=new Vector3[Math.Max(2,count)]; for(var i=0;i<result.Length;i++)result[i]=Vector3.Lerp(start,end,i/(float)(result.Length-1));return result; }

    internal static Vector3[] BuildLightningRoute(
        IReadOnlyList<Vector3> originalRoute,
        Vector3 start,
        Vector3 end)
    {
        var firstCorner = -1;
        var lastCorner = -1;
        for (var i = 1; i < originalRoute.Count - 1; i++)
        {
            var incoming = originalRoute[i] - originalRoute[i - 1];
            var outgoing = originalRoute[i + 1] - originalRoute[i];
            if (incoming.sqrMagnitude <= 0.0001f || outgoing.sqrMagnitude <= 0.0001f)
                continue;
            if (Mathf.Abs(Vector2.SignedAngle(incoming, outgoing)) < 30f)
                continue;
            if (firstCorner < 0)
                firstCorner = i;
            lastCorner = i;
        }

        if (firstCorner < 1 || lastCorner <= firstCorner ||
            lastCorner >= originalRoute.Count - 1)
            return BuildStraightRoute(originalRoute.Count, start, end);

        // Preserve the two authored corners themselves. Resampling this polyline
        // here can place one route edge across a corner and creates a fourth,
        // diagonal direction that does not exist in the original s/z route.
        return new[]
        {
            start,
            originalRoute[firstCorner],
            originalRoute[lastCorner],
            end
        };
    }

    private static Vector3[] BuildOuterCircleRoute(
        IReadOnlyList<Vector3> originalRoute,
        Vector3 start,
        Vector3 end)
    {
        var route = new Vector3[originalRoute.Count];
        var originalSweep = 0f;
        for (var i = 1; i < originalRoute.Count; i++)
        {
            var previousAngle = Mathf.Atan2(originalRoute[i - 1].y, originalRoute[i - 1].x);
            var currentAngle = Mathf.Atan2(originalRoute[i].y, originalRoute[i].x);
            originalSweep += Mathf.DeltaAngle(
                previousAngle * Mathf.Rad2Deg,
                currentAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        }

        var startAngle = Mathf.Atan2(start.y, start.x);
        var endAngle = Mathf.Atan2(end.y, end.x);
        var sweep = endAngle - startAngle;
        sweep += Mathf.Round((originalSweep - sweep) / (Mathf.PI * 2f)) * Mathf.PI * 2f;
        if (originalSweep > 0f && sweep <= 0f)
            sweep += Mathf.PI * 2f;
        else if (originalSweep < 0f && sweep >= 0f)
            sweep -= Mathf.PI * 2f;

        var radius = start.magnitude;
        for (var i = 0; i < route.Length; i++)
        {
            var progress = i / Math.Max(1f, route.Length - 1f);
            var angle = startAngle + sweep * progress;
            route[i] = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                Mathf.Lerp(start.z, end.z, progress));
        }
        route[0] = start;
        route[^1] = end;
        return route;
    }

    internal static Vector3[] BuildTangentCircleRoute(
        IReadOnlyList<Vector3> originalRoute,
        Vector3 start,
        Vector3 end,
        bool centerAtOrigin)
    {
        var route = new Vector3[originalRoute.Count];
        if (!TryFindCircleSection(
                originalRoute,
                out var circleStart,
                out var circleEnd,
                out var center,
                out var radius,
                out var direction,
                centerAtOrigin) ||
            !TryGetDirectedTangentPoint(
                center, radius, start, originalRoute[circleStart],
                direction, true, out var entry) ||
            !TryGetDirectedTangentPoint(
                center, radius, end, originalRoute[circleEnd],
                direction, false, out var exit))
        {
            for (var i = 0; i < route.Length; i++)
                route[i] = Vector3.Lerp(start, end, i / (float)Math.Max(1, route.Length - 1));
            return route;
        }

        var startAngle = Mathf.Atan2(entry.y - center.y, entry.x - center.x);
        var endAngle = Mathf.Atan2(exit.y - center.y, exit.x - center.x);
        var sourceSweep = MeasureRouteSweep(
            originalRoute, circleStart, circleEnd, center, direction);
        var sweep = PreserveRouteTurns(
            GetDirectedSweep(startAngle, endAngle, direction),
            sourceSweep,
            direction);
        var entryLength = Vector3.Distance(start, entry);
        var arcLength = Mathf.Abs(sweep) * radius;
        var exitLength = Vector3.Distance(exit, end);
        var totalLength = entryLength + arcLength + exitLength;
        // Returning the array as allocated would hand back a route of all zeros,
        // collapsing the whole slide onto the origin, which reads on screen as the
        // slide simply not being there. Degenerate geometry falls back to the
        // straight line, same as when the tangent solve fails above.
        if (totalLength <= 0.0001f)
        {
            for (var i = 0; i < route.Length; i++)
                route[i] = Vector3.Lerp(start, end, i / (float)Math.Max(1, route.Length - 1));
            return route;
        }

        for (var i = 0; i < route.Length; i++)
        {
            var distance = totalLength * i / Math.Max(1f, route.Length - 1f);
            if (distance <= entryLength)
            {
                route[i] = Vector3.Lerp(
                    start,
                    entry,
                    entryLength > 0.0001f ? distance / entryLength : 1f);
            }
            else if (distance < entryLength + arcLength)
            {
                var arcProgress = arcLength > 0.0001f
                    ? (distance - entryLength) / arcLength
                    : 1f;
                var angle = startAngle + sweep * arcProgress;
                route[i] = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius,
                    Mathf.Lerp(start.z, end.z, distance / totalLength));
            }
            else
            {
                route[i] = Vector3.Lerp(
                    exit,
                    end,
                    exitLength > 0.0001f
                        ? (distance - entryLength - arcLength) / exitLength
                        : 1f);
            }
        }

        route[0] = start;
        route[^1] = end;
        return route;
    }

    internal static Vector3[] BuildAdaptiveTangentCircleRoute(
        IReadOnlyList<Vector3> originalRoute,
        Vector3 start,
        Vector3 end,
        bool centerAtOrigin,
        float maxStep = 0.08f)
        => BuildAdaptiveTangentCircleRoute(
            originalRoute, start, end, centerAtOrigin, null, maxStep);

    internal static Vector3[] BuildAdaptiveTangentCircleRouteAround(
        IReadOnlyList<Vector3> originalRoute,
        Vector3 start,
        Vector3 end,
        Vector3 center,
        float maxStep = 0.08f)
        => BuildAdaptiveTangentCircleRoute(
            originalRoute, start, end, false, center, maxStep);

    private static Vector3[] BuildAdaptiveTangentCircleRoute(
        IReadOnlyList<Vector3> originalRoute,
        Vector3 start,
        Vector3 end,
        bool centerAtOrigin,
        Vector3? requestedCenter,
        float maxStep)
    {
        maxStep = Mathf.Max(0.01f, maxStep);
        if (!TryFindCircleSection(
                originalRoute,
                out var circleStart,
                out var circleEnd,
                out var sourceCenter,
                out var radius,
                out var direction,
                centerAtOrigin))
        {
            return BuildSampledLine(start, end, maxStep);
        }

        var center = requestedCenter ?? sourceCenter;
        var centerOffset = center - sourceCenter;
        if (
            !TryGetDirectedTangentPoint(
                center, radius, start,
                originalRoute[circleStart] + centerOffset,
                direction, true, out var entry) ||
            !TryGetDirectedTangentPoint(
                center, radius, end,
                originalRoute[circleEnd] + centerOffset,
                direction, false, out var exit))
        {
            return BuildSampledLine(start, end, maxStep);
        }

        var points = new List<Vector3>();
        AppendSampledLine(points, start, entry, maxStep, includeStart: true);

        var startAngle = Mathf.Atan2(entry.y - center.y, entry.x - center.x);
        var endAngle = Mathf.Atan2(exit.y - center.y, exit.x - center.x);
        var sourceSweep = MeasureRouteSweep(
            originalRoute, circleStart, circleEnd, sourceCenter, direction);
        var sweep = PreserveRouteTurns(
            GetDirectedSweep(startAngle, endAngle, direction),
            sourceSweep,
            direction);
        var arcLength = Mathf.Abs(sweep) * radius;
        var arcSegments = Math.Max(1, Mathf.CeilToInt(arcLength / maxStep));
        for (var i = 1; i <= arcSegments; i++)
        {
            var progress = i / (float)arcSegments;
            var angle = startAngle + sweep * progress;
            points.Add(new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius,
                Mathf.Lerp(entry.z, exit.z, progress)));
        }

        AppendSampledLine(points, exit, end, maxStep, includeStart: false);
        points[0] = start;
        points[^1] = end;
        return points.ToArray();
    }

    private static Vector3[] BuildSampledLine(
        Vector3 start, Vector3 end, float maxStep)
    {
        var points = new List<Vector3>();
        AppendSampledLine(points, start, end, maxStep, includeStart: true);
        return points.ToArray();
    }

    private static void AppendSampledLine(
        ICollection<Vector3> points,
        Vector3 start,
        Vector3 end,
        float maxStep,
        bool includeStart)
    {
        var length = Vector3.Distance(start, end);
        var segments = Math.Max(1, Mathf.CeilToInt(length / maxStep));
        var first = includeStart ? 0 : 1;
        for (var i = first; i <= segments; i++)
            points.Add(Vector3.Lerp(start, end, i / (float)segments));
    }

    private static bool TryFindCircleSection(
        IReadOnlyList<Vector3> route,
        out int sectionStart,
        out int sectionEnd,
        out Vector3 center,
        out float radius,
        out float direction,
        bool centerAtOrigin)
    {
        sectionStart = 0;
        sectionEnd = 0;
        center = Vector3.zero;
        radius = 0f;
        direction = 1f;
        if (route.Count < 6)
            return false;

        var bestStart = -1;
        var bestEnd = -1;
        var bestDirection = 0f;
        var currentStart = -1;
        var currentDirection = 0f;
        for (var i = 1; i < route.Count - 1; i++)
        {
            var incoming = route[i] - route[i - 1];
            var outgoing = route[i + 1] - route[i];
            if (incoming.sqrMagnitude <= 0.0001f || outgoing.sqrMagnitude <= 0.0001f)
                continue;

            var turn = Vector2.SignedAngle(incoming, outgoing);
            var turnDirection = Mathf.Sign(turn);
            var isCircleTurn = Mathf.Abs(turn) >= 4f && Mathf.Abs(turn) <= 30f;
            if (isCircleTurn && (currentStart < 0 || turnDirection == currentDirection))
            {
                if (currentStart < 0)
                {
                    currentStart = i;
                    currentDirection = turnDirection;
                }
                continue;
            }

            if (currentStart >= 0 &&
                (bestStart < 0 || i - 1 - currentStart > bestEnd - bestStart))
            {
                bestStart = currentStart;
                bestEnd = i - 1;
                bestDirection = currentDirection;
            }
            currentStart = isCircleTurn ? i : -1;
            currentDirection = isCircleTurn ? turnDirection : 0f;
        }

        if (currentStart >= 0 &&
            (bestStart < 0 || route.Count - 2 - currentStart > bestEnd - bestStart))
        {
            bestStart = currentStart;
            bestEnd = route.Count - 2;
            bestDirection = currentDirection;
        }

        if (bestStart < 0 || bestEnd - bestStart < 2)
            return false;

        sectionStart = bestStart;
        sectionEnd = bestEnd;
        if (centerAtOrigin)
        {
            center = new Vector3(0f, 0f, route[sectionStart].z);
            radius = 0f;
            for (var i = sectionStart; i <= sectionEnd; i++)
                radius += Vector2.Distance(Vector2.zero, route[i]);
            radius /= sectionEnd - sectionStart + 1f;
        }
        else if (!TryFitCircle(route, sectionStart, sectionEnd, out center, out radius))
        {
            return false;
        }

        direction = bestDirection;
        return true;
    }

    private static bool TryFitCircle(
        IReadOnlyList<Vector3> points,
        int start,
        int end,
        out Vector3 center,
        out float radius)
    {
        center = Vector3.zero;
        radius = 0f;
        double xx = 0d, xy = 0d, x = 0d;
        double yy = 0d, y = 0d, count = 0d;
        double xb = 0d, yb = 0d, b = 0d;
        for (var i = start; i <= end; i++)
        {
            var px = (double)points[i].x;
            var py = (double)points[i].y;
            var value = px * px + py * py;
            xx += 4d * px * px;
            xy += 4d * px * py;
            x += 2d * px;
            yy += 4d * py * py;
            y += 2d * py;
            count += 1d;
            xb += 2d * px * value;
            yb += 2d * py * value;
            b += value;
        }

        var determinant = Determinant3(xx, xy, x, xy, yy, y, x, y, count);
        if (Math.Abs(determinant) <= 0.000001d)
            return false;

        var cx = Determinant3(xb, xy, x, yb, yy, y, b, y, count) / determinant;
        var cy = Determinant3(xx, xb, x, xy, yb, y, x, b, count) / determinant;
        var constant = Determinant3(xx, xy, xb, xy, yy, yb, x, y, b) / determinant;
        var radiusSquared = cx * cx + cy * cy + constant;
        if (radiusSquared <= 0.000001d)
            return false;

        center = new Vector3((float)cx, (float)cy, points[start].z);
        radius = Mathf.Sqrt((float)radiusSquared);
        return true;
    }

    private static double Determinant3(
        double a, double b, double c,
        double d, double e, double f,
        double g, double h, double i)
        => a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);

    private static bool TryGetDirectedTangentPoint(
        Vector3 center,
        float radius,
        Vector3 point,
        Vector3 preferred,
        float direction,
        bool entering,
        out Vector3 tangent)
    {
        tangent = preferred;
        var delta = point - center;
        var distanceSquared = delta.x * delta.x + delta.y * delta.y;
        var radiusSquared = radius * radius;
        if (distanceSquared < radiusSquared - 0.0001f)
            return false;
        if (Mathf.Abs(distanceSquared - radiusSquared) <= 0.0001f)
        {
            tangent = point;
            tangent.z = preferred.z;
            return true;
        }

        var baseScale = radiusSquared / distanceSquared;
        var offsetScale = radius * Mathf.Sqrt(distanceSquared - radiusSquared) /
                          distanceSquared;
        var basePoint = center + delta * baseScale;
        var perpendicular = new Vector3(-delta.y, delta.x, 0f);
        var first = basePoint + perpendicular * offsetScale;
        var second = basePoint - perpendicular * offsetScale;

        float Continuity(Vector3 candidate)
        {
            var radial = candidate - center;
            var circleDirection = direction >= 0f
                ? new Vector3(-radial.y, radial.x, 0f)
                : new Vector3(radial.y, -radial.x, 0f);
            var lineDirection = entering
                ? candidate - point
                : point - candidate;
            if (circleDirection.sqrMagnitude <= 0.000001f ||
                lineDirection.sqrMagnitude <= 0.000001f)
                return 1f;
            return Vector3.Dot(
                circleDirection.normalized,
                lineDirection.normalized);
        }

        var firstScore = Continuity(first);
        var secondScore = Continuity(second);
        if (Mathf.Abs(firstScore - secondScore) <= 0.0001f)
            tangent = (first - preferred).sqrMagnitude <=
                      (second - preferred).sqrMagnitude
                ? first
                : second;
        else
            tangent = firstScore > secondScore ? first : second;
        tangent.z = preferred.z;
        return true;
    }

    private static float GetDirectedSweep(float start, float end, float direction)
    {
        var sweep = end - start;
        if (direction >= 0f)
        {
            while (sweep < 0f)
                sweep += Mathf.PI * 2f;
        }
        else
        {
            while (sweep > 0f)
                sweep -= Mathf.PI * 2f;
        }
        return sweep;
    }

    private static float MeasureRouteSweep(
        IReadOnlyList<Vector3> route,
        int start,
        int end,
        Vector3 center,
        float direction)
    {
        var sweep = 0f;
        for (var index = start + 1; index <= end; index++)
        {
            var previous = route[index - 1] - center;
            var current = route[index] - center;
            if (previous.sqrMagnitude <= 0.000001f ||
                current.sqrMagnitude <= 0.000001f)
                continue;
            var delta = Vector2.SignedAngle(previous, current) * Mathf.Deg2Rad;
            if (direction >= 0f && delta < 0f)
                delta += Mathf.PI * 2f;
            else if (direction < 0f && delta > 0f)
                delta -= Mathf.PI * 2f;
            sweep += delta;
        }
        return sweep;
    }

    private static float PreserveRouteTurns(
        float sweep,
        float sourceSweep,
        float direction)
    {
        var turn = direction >= 0f ? Mathf.PI * 2f : -Mathf.PI * 2f;
        while (Mathf.Abs(sweep) + Mathf.PI < Mathf.Abs(sourceSweep))
            sweep += turn;
        return sweep;
    }


 public static bool AppendSingleOrbit(List<Vector3> p,Vector3 s,Vector3 e,int n,bool ccw) { return AlphaSlideCodePathGeometry.AppendSingleOrbit(p,s,e,n,ccw); }
}
internal static class SelectableOrbitPathGeometry
{
    public static void Append(
        AlphaRouteSource routeSource,
        List<Vector3> target,
        char startArea,
        int startPosition,
        bool startIsDZone,
        char orbitArea,
        int orbitPosition,
        bool orbitIsDZone,
        bool orbitIsNumber,
        char endArea,
        int endPosition,
        bool endIsDZone,
        string shape)
    {
        var start = Position(startArea, startPosition, startIsDZone);
        var end = Position(endArea, endPosition, endIsDZone);
        var lowerShape = shape == "P" ? "p" : "q";

        if (orbitIsNumber)
        {
            var orbitIndex = orbitArea == 'C'
                ? 0
                : orbitArea == 'O'
                    ? 9
                    : orbitPosition;
            if (!TryAppendLegacyNumericOrbit(
                    routeSource, target, start, end, orbitIndex, shape) &&
                !AlphaPathCurves.AppendSingleOrbit(
                    target, start, end, orbitIndex, shape == "P"))
                AppendLine(target, start, end);
            return;
        }

        if (orbitArea == 'C' || orbitPosition == 0)
        {
            if (!AppendTemplate(
                    routeSource, target,
                    startPosition, endPosition, lowerShape,
                    start, end, centerAtOrigin: true, null))
                AppendFallbackCircle(
                    target, start, end, lowerShape == "p");
            return;
        }

        var sourceShape = shape == "P" ? "pp" : "qq";
        var sourceStart = shape == "P"
            ? (orbitPosition + 5) % 8 + 1
            : orbitPosition % 8 + 1;
        var sourceEnd = (sourceStart + 3) % 8 + 1;
        var explicitCenter = orbitIsDZone
            ? Position(orbitArea, orbitPosition, true)
            : (Vector3?)null;
        if (!AppendTemplate(
                routeSource, target,
                sourceStart, sourceEnd, sourceShape,
                start, end, centerAtOrigin: false, explicitCenter))
            AppendLine(target, start, end);
    }

    private static bool TryAppendLegacyNumericOrbit(
        AlphaRouteSource routeSource,
        List<Vector3> target,
        Vector3 start,
        Vector3 end,
        int orbitIndex,
        string shape)
    {
        if (orbitIndex is < 1 or > 8 || shape is not ("P" or "Q"))
            return false;

        var sourceShape = shape == "P" ? "pp" : "qq";
        var sourceStart = shape == "P"
            ? (orbitIndex + 5) % 8 + 1
            : orbitIndex % 8 + 1;
        var sourceEnd = (sourceStart + 3) % 8 + 1;
        return AppendTemplate(
            routeSource,
            target,
            sourceStart,
            sourceEnd,
            sourceShape,
            start,
            end,
            centerAtOrigin: false,
            explicitCenter: null);
    }

    private static bool AppendTemplate(
        AlphaRouteSource routeSource,
        List<Vector3> target,
        int sourceStart,
        int sourceEnd,
        string sourceShape,
        Vector3 actualStart,
        Vector3 actualEnd,
        bool centerAtOrigin,
        Vector3? explicitCenter)
    {
        if (routeSource == null ||
            !routeSource.TryGetSlideVisualRoute(
                $"{sourceStart}{sourceShape}{sourceEnd}[4:1]",
                out var sourcePath) ||
            sourcePath.Count < 2)
            return false;

        var originalRoute = new Vector3[sourcePath.Count + 2];
        originalRoute[0] = Position('K', sourceStart, false);
        for (var i = 0; i < sourcePath.Count; i++)
            originalRoute[i + 1] = sourcePath[i];
        originalRoute[^1] = Position('K', sourceEnd, false);

        var route = explicitCenter.HasValue
            ? AlphaPathCurves.BuildAdaptiveTangentCircleRouteAround(
                originalRoute, actualStart, actualEnd, explicitCenter.Value)
            : AlphaPathCurves.BuildAdaptiveTangentCircleRoute(
                originalRoute, actualStart, actualEnd, centerAtOrigin);
        var first = target.Count == 0 ? 0 : 1;
        for (var i = first; i < route.Length; i++)
            target.Add(route[i]);
        return true;
    }

    private static void AppendFallbackCircle(
        List<Vector3> target,
        Vector3 start,
        Vector3 end,
        bool clockwise)
    {
        const float radius = 2.3f;
        const int samples = 256;
        var startAngle = Mathf.Atan2(start.y, start.x);
        var endAngle = Mathf.Atan2(end.y, end.x);
        var startTangent = TangentAngle(
            start.magnitude, startAngle, clockwise, true);
        var endTangent = TangentAngle(
            end.magnitude, endAngle, clockwise, false);
        var tangentStart = new Vector3(
            Mathf.Cos(startTangent), Mathf.Sin(startTangent)) * radius;
        var tangentEnd = new Vector3(
            Mathf.Cos(endTangent), Mathf.Sin(endTangent)) * radius;
        AppendLine(target, start, tangentStart);
        var sweep = clockwise
            ? -Mathf.Repeat(startTangent - endTangent, Mathf.PI * 2f)
            : Mathf.Repeat(endTangent - startTangent, Mathf.PI * 2f);
        for (var i = 1; i <= samples; i++)
        {
            var angle = startTangent + sweep * (i / (float)samples);
            Add(target, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
        AppendLine(target, tangentEnd, end);
    }

    private static void AppendLine(
        List<Vector3> target,
        Vector3 start,
        Vector3 end)
    {
        var samples = Math.Max(
            1, Mathf.CeilToInt(Vector3.Distance(start, end) / 0.08f));
        var first = target.Count == 0 ? 0 : 1;
        for (var i = first; i <= samples; i++)
            Add(target, Vector3.Lerp(start, end, i / (float)samples));
    }

    private static void Add(List<Vector3> target, Vector3 point)
    {
        if (target.Count == 0 ||
            (target[^1] - point).sqrMagnitude > 0.000001f)
            target.Add(point);
    }

    private static float TangentAngle(
        float radius,
        float pointAngle,
        bool clockwise,
        bool entering)
    {
        if (radius <= 2.3001f)
            return pointAngle;
        var offset = Mathf.Acos(2.3f / radius);
        return pointAngle + (clockwise == entering ? -offset : offset);
    }

    private static Vector3 Position(char area, int index, bool dZone)
    {
        if (area == 'C')
            return Vector3.zero;
        var angleOffset = area is 'A' or 'B' or 'K'
            ? Mathf.PI * 5f / 8f
            : Mathf.PI * 6f / 8f;
        if (area == 'K' && dZone)
            angleOffset += Mathf.PI / 8f;
        var radius = area switch
        {
            'K' => 4.8f,
            'A' or 'D' => 4.1f,
            'B' => 2.3f,
            'E' => 3f,
            _ => 0f
        };
        var angle = -index * Mathf.PI / 4f + angleOffset;
        return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }
}

}