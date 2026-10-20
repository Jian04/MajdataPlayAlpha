using System; using System.Collections.Generic; using UnityEngine; using MajdataCore;
namespace MajdataPlay.Alpha { internal sealed class AlphaPathGeometry {
readonly List<Vector3> path=new(); readonly AlphaRouteSource slideRouteSource=new();
public static List<Vector3> Build(SlidePathData expression) {
var builder=new AlphaPathGeometry();
foreach(var segment in expression.segments) {
var a=ResolvePosition(segment.start,segment.startPosition,segment.startIsDZone);
var b=ResolvePosition(segment.end,segment.endPosition,segment.endIsDZone);
if(segment.shape=="V") {var m=ResolvePosition(segment.middle,segment.middlePosition,segment.middleIsDZone); builder.AppendSegment(a.Area,a.Position,a.IsDZone,m.Area,m.Position,m.IsDZone,"-"); builder.AppendSegment(m.Area,m.Position,m.IsDZone,b.Area,b.Position,b.IsDZone,"-");}
else if(segment.shape is "P" or "Q") { var m=ResolvePosition(segment.middle,segment.middlePosition,segment.middleIsDZone); builder.AppendSelectableOrbitSegment(a.Area,a.Position,a.IsDZone,m.Area,m.Position,m.IsDZone,segment.middle.source?.Length==1&&char.IsDigit(segment.middle.source[0]),b.Area,b.Position,b.IsDZone,segment.shape); }
else builder.AppendSegment(a.Area,a.Position,a.IsDZone,b.Area,b.Position,b.IsDZone,segment.shape);
} return builder.path; }
    private void AppendSelectableOrbitSegment(
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
        SelectableOrbitPathGeometry.Append(
            slideRouteSource,
            path,
            startArea,
            startPosition,
            startIsDZone,
            orbitArea,
            orbitPosition,
            orbitIsDZone,
            orbitIsNumber,
            endArea,
            endPosition,
            endIsDZone,
            shape);
    }

    private static (char Area, int Position, bool IsDZone) ResolvePosition(
        SlidePositionData parsed,
        int legacyPosition,
        bool legacyDZone)
    {
        return parsed != null && parsed.position != 0
            ? (parsed.area, parsed.position, parsed.isDZone)
            : ('K', legacyPosition, legacyDZone);
    }

    private void AppendSegment(
        char segmentStartArea,
        int segmentStartPosition,
        bool segmentStartIsDZone,
        char segmentEndArea,
        int segmentEndPosition,
        bool segmentEndIsDZone,
        string segmentShape)
    {
        const int baseSegments = 256;
        var isMultiLoopSpiral = segmentShape.Length > 1 &&
                                (segmentShape[0] == '<' || segmentShape[0] == '>');
        var loopCount = isMultiLoopSpiral ? segmentShape.Length : 1;
        var segments = baseSegments * loopCount;
        var start = AreaPosition(segmentStartArea, segmentStartPosition, segmentStartIsDZone);
        var end = AreaPosition(segmentEndArea, segmentEndPosition, segmentEndIsDZone);
        var firstSample = path.Count == 0 ? 0 : 1;
        if (segmentShape is "-" or "w")
        {
            for (var i = firstSample; i <= segments; i++)
                path.Add(Vector3.Lerp(start, end, i / (float)segments));
            return;
        }

        if (segmentShape == "v")
        {
            AppendLine(start, Vector3.zero, firstSample, segments / 2);
            AppendLine(Vector3.zero, end, 1, segments / 2);
            return;
        }

        if (segmentShape is "s" or "z")
        {
            var opposite = (segmentStartPosition + 3) % 8 + 1;
            if (slideRouteSource == null || !slideRouteSource.TryGetSlideVisualRoute(
                    $"{segmentStartPosition}{segmentShape}{opposite}[4:1]", out var source))
                throw new InvalidOperationException("Missing s/z slide route prefab.");
            var original = new List<Vector3> { AreaPosition('K', segmentStartPosition, false) };
            original.AddRange(source);
            original.Add(AreaPosition('K', opposite, false));
            var lightning = AlphaPathCurves.BuildLightningRoute(original, start, end);
            for (var i = firstSample; i < lightning.Length; i++) path.Add(lightning[i]);
            return;
        }

        if (segmentShape is "p" or "q")
        {
            if (!AppendOriginalTangentRoute(
                    segmentStartPosition,
                    segmentEndPosition,
                    segmentShape,
                    start,
                    end,
                    firstSample,
                    centerAtOrigin: true))
                AppendTangentCircle(start, end, segmentShape == "p", firstSample);
            return;
        }

        // rp and rq are key-slide shapes in their own right, so their authored route
        // can be inherited exactly the way pp's and qq's is. Without them named here
        // they fell through to the branches below and were drawn as another shape
        // entirely, which a mixed path like "1rp5d[8:1]-E7[8:1]" could reach.
        //
        // The fallback reads the last character rather than the first: it is 'p' or
        // 'q' for all four shapes, so pp and qq keep the direction they always had.
        if (segmentShape is "pp" or "qq" or "rp" or "rq")
        {
            if (!AppendOriginalTangentRoute(
                    segmentStartPosition,
                    segmentEndPosition,
                    segmentShape,
                    start,
                    end,
                    firstSample,
                    centerAtOrigin: false))
                AppendTangentCircle(start, end, segmentShape[^1] == 'p', firstSample);
            return;
        }

        if (loopCount == 1 && (segmentStartArea == 'C' || segmentEndArea == 'C'))
        {
            var bezierDirection = segmentShape.StartsWith("<", StringComparison.Ordinal) ? -1f : 1f;
            var delta = end - start;
            var control = (start + end) * 0.5f +
                          new Vector3(-delta.y, delta.x) * (0.35f * bezierDirection);
            for (var i = firstSample; i <= segments; i++)
            {
                var t = i / (float)segments;
                var inverse = 1f - t;
                path.Add(inverse * inverse * start +
                         2f * inverse * t * control +
                         t * t * end);
            }
            return;
        }

        var startAngle = Mathf.Atan2(start.y, start.x);
        var endAngle = Mathf.Atan2(end.y, end.x);
        var deltaAngle = ResolveAngleDelta(
            startAngle,
            endAngle,
            segmentShape[0],
            segmentStartPosition);
        var arcDirection = Mathf.Sign(deltaAngle);
        if (loopCount > 1)
            deltaAngle += arcDirection * Mathf.PI * 2f * (loopCount - 1);
        var startRadius = start.magnitude;
        var endRadius = end.magnitude;
        for (var i = firstSample; i <= segments; i++)
        {
            var t = i / (float)segments;
            var angle = startAngle + deltaAngle * t;
            var radius = Mathf.Lerp(startRadius, endRadius, t);
            path.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
    }

    private void AppendLine(Vector3 start, Vector3 end, int firstSample, int segments)
    {
        for (var i = firstSample; i <= segments; i++)
            path.Add(Vector3.Lerp(start, end, i / (float)segments));
    }

    private bool AppendOriginalTangentRoute(
        int startPosition,
        int endPosition,
        string segmentShape,
        Vector3 actualStart,
        Vector3 actualEnd,
        int firstSample,
        bool centerAtOrigin)
    {
        if (slideRouteSource == null ||
            !slideRouteSource.TryGetSlideVisualRoute(
                $"{startPosition}{segmentShape}{endPosition}[4:1]",
                out var sourcePath) ||
            sourcePath.Count < 2)
            return false;

        var originalRoute = new Vector3[sourcePath.Count + 2];
        originalRoute[0] = AreaPosition('K', startPosition, false);
        for (var i = 0; i < sourcePath.Count; i++)
            originalRoute[i + 1] = sourcePath[i];
        originalRoute[^1] = AreaPosition('K', endPosition, false);

        // Touch routes need geometry samples, not a one-to-one remap of prefab
        // bars. Preserve both tangent points explicitly so the rendered
        // polyline cannot bridge across a line/arc join.
        var route = AlphaPathCurves.BuildAdaptiveTangentCircleRoute(
            originalRoute,
            actualStart,
            actualEnd,
            centerAtOrigin);
        for (var i = firstSample; i < route.Length; i++)
            path.Add(route[i]);
        return true;
    }

    private void AppendTangentCircle(
        Vector3 start,
        Vector3 end,
        bool clockwise,
        int firstSample)
    {
        const float circleRadius = 2.3f;
        const float maxStep = 0.08f;
        var startAngle = Mathf.Atan2(start.y, start.x);
        var endAngle = Mathf.Atan2(end.y, end.x);
        var startTangent = TangentAngle(start.magnitude, startAngle, clockwise, true);
        var endTangent = TangentAngle(end.magnitude, endAngle, clockwise, false);
        var tangentStart = new Vector3(Mathf.Cos(startTangent), Mathf.Sin(startTangent)) * circleRadius;
        var tangentEnd = new Vector3(Mathf.Cos(endTangent), Mathf.Sin(endTangent)) * circleRadius;
        AppendAdaptiveLine(start, tangentStart, firstSample == 0, maxStep);

        var sweep = clockwise
            ? -Mathf.Repeat(startTangent - endTangent, Mathf.PI * 2f)
            : Mathf.Repeat(endTangent - startTangent, Mathf.PI * 2f);
        var arcSegments = Math.Max(
            1,
            Mathf.CeilToInt(Mathf.Abs(sweep) * circleRadius / maxStep));
        for (var i = 1; i <= arcSegments; i++)
        {
            var angle = startTangent + sweep * (i / (float)arcSegments);
            path.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * circleRadius);
        }
        AppendAdaptiveLine(
            tangentEnd, end, includeStart: false, maxStep: maxStep);
    }

    private void AppendAdaptiveLine(
        Vector3 start,
        Vector3 end,
        bool includeStart,
        float maxStep)
    {
        var segments = Math.Max(
            1,
            Mathf.CeilToInt(Vector3.Distance(start, end) / maxStep));
        var first = includeStart ? 0 : 1;
        for (var i = first; i <= segments; i++)
            path.Add(Vector3.Lerp(start, end, i / (float)segments));
    }

    private static float TangentAngle(float radius, float pointAngle, bool clockwise, bool entering)
    {
        if (radius <= 2.3001f)
            return pointAngle;
        var offset = Mathf.Acos(2.3f / radius);
        var sign = clockwise == entering ? -1f : 1f;
        return pointAngle + sign * offset;
    }

    private static Vector3 AreaPosition(char area, int index, bool dZone)
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

private static float ResolveAngleDelta(float a,float b,char shape,int pos)=>(float)TouchSlideDirection.Sweep(a,b,pos,shape);
} }
