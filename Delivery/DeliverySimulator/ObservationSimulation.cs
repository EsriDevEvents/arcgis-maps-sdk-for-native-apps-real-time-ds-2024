using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Tasks.NetworkAnalysis;
using DeliveryShared;

namespace DeliverySimulator;

public partial class ObservationSimulator : ObservableObject
{
    private readonly CompanyColor _company;
    private readonly Envelope _extent;
    private readonly RouteTask _routeTask;
    private readonly FeatureTable _startPointsTable;
    private List<Feature>? _startPointFeatures;
    private readonly Timer _observationTimer;
    private readonly Timer _startNewRoutesTimer;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly List<ActiveRoute> _routes = [];
    private readonly int _maxActiveRoutes = 10;
    private int _observationInterval = 1000;

    private UdpClient? _udpClient;

    public ObservationSimulator(CompanyColor company, Envelope extent, RouteTask routeTask, FeatureTable startPointsTable)
    {
        _company = company;
        _extent = extent;
        _routeTask = routeTask;
        _startPointsTable = startPointsTable;
        _observationTimer = new(TimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        _startNewRoutesTimer = new(StartNewRoutesTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        PropertyChanged += SimulationSource_PropertyChanged;
    }

    #region Properties

    [ObservableProperty]
    public int _speedAdjustment = 2;

    #endregion

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await CreateInitialRoutesAsync();
        IPEndPoint endPoint = new(IPAddress.Loopback, 8080);
        _udpClient = new();
        _udpClient.Connect(endPoint);
        _observationTimer.Change(100, _observationInterval / SpeedAdjustment);
        _startNewRoutesTimer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    internal Task DisconnectAsync()
    {
        _observationTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _startNewRoutesTimer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    private void SimulationSource_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpeedAdjustment))
        {
            _observationInterval = 1000 / SpeedAdjustment;
            _observationTimer.Change(100, _observationInterval);
        }
    }

    private async Task CreateInitialRoutesAsync()
    {
        _routes.Clear();

        var routesToCreate = Random.Shared.Next(_maxActiveRoutes / 2, _maxActiveRoutes);

        _startPointFeatures = [.. (await _startPointsTable.QueryFeaturesAsync(new() { WhereClause = "1=1" }))];
        for (int n = 0; n < routesToCreate; ++n)
        {
            var startPointFeature = _startPointFeatures[Random.Shared.Next(_startPointFeatures.Count - 1)];
            if (startPointFeature.Geometry is not MapPoint startPoint)
                continue;

            try
            {
                await StartNewRouteAsync(startPoint, true);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
            }
        }
    }

    private async Task StartNewRouteAsync(MapPoint startPoint, bool isEnRoute = false)
    {
        var endPoint = GetRandomMapPointWithinExtent(_extent);
        var route = await SolveRouteAsync(startPoint, endPoint);
        if (route.RouteGeometry is not Polyline routePath)
            throw new InvalidOperationException("Route path could not be found");

        var metersPerSecond = route.TotalLength / route.TotalTime.TotalSeconds;
        var activeRoute = new ActiveRoute(routePath, metersPerSecond);
        if (isEnRoute)
            activeRoute.SecondsTraveled = Random.Shared.NextDouble() * route.TotalTime.TotalSeconds;
        _routes.Add(activeRoute);
    }

    private async Task<Route> SolveRouteAsync(MapPoint startPoint, MapPoint endPoint)
    {
        var routeParams = await _routeTask.CreateDefaultParametersAsync();
        routeParams.OutputSpatialReference = SpatialReferences.WebMercator;
        routeParams.SetStops([new(startPoint), new(endPoint)]);
        var routeResult = await _routeTask.SolveRouteAsync(routeParams);
        return routeResult.Routes[0];
    }

    private static MapPoint GetRandomMapPointWithinExtent(Envelope extent)
    {
        return new MapPoint(
            extent.XMin + Random.Shared.NextDouble() * (extent.XMax - extent.XMin),
            extent.YMin + Random.Shared.NextDouble() * (extent.YMax - extent.YMin),
            extent.SpatialReference);
    }

    private void TimerCallback(object? o)
    {
        try
        {
            // only run the method if the previous run is complete
            if (!_semaphore.Wait(0))
            {
                Trace.WriteLine($"{DateTime.Now} | Skipped generate frame");
                return;
            }

            // update current routes (delete routes / start new routes if necessary)
            var routeList = _routes.ToList();
            foreach (var route in routeList)
            {
                if (Random.Shared.Next(0, 10) < 3)
                    continue; // skip the update 30% of the time to make it look more random

                var routeStatus = route.RouteStatus;
                SendObservationMessage(route);

                if (route.RouteStatus is RouteStatus.Complete)
                {
                    SendDeleteEntityMessage(route);
                    _routes.Remove(route);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error generating observations: {ex}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async void StartNewRoutesTimerCallback(object? o)
    {
        try
        {
            // only run the method if the previous run is complete
            if (!_semaphore.Wait(0))
            {
                Trace.WriteLine($"{DateTime.Now} | Skipped generate frame");
                return;
            }

            // start new routes
            if (_routes.Count < _maxActiveRoutes)
            {
                var startPointFeature = _startPointFeatures![Random.Shared.Next(_startPointFeatures.Count - 1)];
                if (startPointFeature.Geometry is MapPoint startPoint)
                {
                    try
                    {
                        await StartNewRouteAsync(startPoint);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(ex.ToString());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error starting route: {ex}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void SendObservationMessage(ActiveRoute route)
    {
        if (_udpClient is null)
            return;

        try
        {
            var point = route.CalculateNextPoint(1);
            var locationMessage = new LocationMessage()
            {
                MessageType = (int)MessageType.Add,
                EntityId = $"{_company}:{route.RouteId}",
                Company = Convert.ToInt32(_company),
                PayloadWeight = route.PayloadWeight,
                Speed = Math.Round(route.AverageMetersPerSecond * (0.75 + (Random.Shared.NextDouble() * (1.5 - 0.75))), 2),
                X = point.X,
                Y = point.Y,
                Heading = route.CurrentHeading
            };
            var json = JsonSerializer.Serialize(locationMessage);
            Debug.WriteLine(json);
            byte[] data = Encoding.UTF8.GetBytes(json);
            _udpClient.Send(data, data.Length);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SendObservationMessage: {ex.Message}");
        }
    }

    private void SendDeleteEntityMessage(ActiveRoute route)
    {
        if (_udpClient is null)
            return;

        try
        {
            var point = route.EndPoint;
            var locationMessage = new LocationMessage()
            {
                MessageType = (int)MessageType.Delete,
                EntityId = $"{_company}:{route.RouteId}",
                Company = Convert.ToInt32(_company),
                PayloadWeight = route.PayloadWeight,
                Speed = 0d,
                X = point.X,
                Y = point.Y,
                Heading = route.CurrentHeading
            };
            var json = JsonSerializer.Serialize(locationMessage);
            byte[] data = Encoding.UTF8.GetBytes(json);
            _udpClient.Send(data, data.Length);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SendDeleteEntityMessage: {ex.Message}");
        }
    }
}
