using System;
using System.Threading;
using Esri.ArcGISRuntime.Geometry;

namespace DeliverySimulator;

public class ActiveRoute
{
    private static int _routeId = 0;

    public ActiveRoute(Polyline routePath, double routeSpeed)
    {
        RouteId = Interlocked.Increment(ref _routeId);
        RoutePath = routePath;
        AverageMetersPerSecond = routeSpeed;
        LastPoint = (MapPoint)StartPoint.Project(SpatialReferences.Wgs84);
        RouteStatus = RouteStatus.EnRoute;
        PayloadWeight = Random.Shared.NextInt64(1_000, 10_000);
    }

    #region Properties

    public int RouteId { get; }

    public Polyline RoutePath { get; private set; }

    public MapPoint StartPoint => RoutePath.Parts[0].StartPoint!;

    public MapPoint EndPoint => RoutePath.Parts[0].EndPoint!;

    public double AverageMetersPerSecond { get; private set; }

    public MapPoint LastPoint { get; private set; }

    public double SecondsTraveled { get; set; }

    public double DistanceTraveled { get; set; }

    public double CurrentHeading { get; private set; }

    public RouteStatus RouteStatus { get; private set; }

    public double PayloadWeight { get; set; }

    #endregion

    public MapPoint CalculateNextPoint(double seconds = 1d)
    {
        SecondsTraveled += seconds;
        DistanceTraveled = SecondsTraveled * AverageMetersPerSecond;
        var point = (MapPoint)RoutePath.CreatePointAlong(DistanceTraveled).Project(SpatialReferences.Wgs84);
        if (DistanceTraveled >= RoutePath.Length())
            RouteStatus = RouteStatus.Complete;
        CurrentHeading = GeometryEngine.DistanceGeodetic(LastPoint, point, null, null, GeodeticCurveType.Geodesic).Azimuth1;
        LastPoint = point;
        return LastPoint;
    }
}

public enum RouteStatus
{
    EnRoute = 0,
    Complete
}
