using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DeliveryShared;
using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.RealTime;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;

namespace DeliveryDashboard;

public partial class MainWindow : Window
{
    private static readonly Envelope _extent = new(
        -13053376.10252461, 3851361.7018923508, -13029715.044618936, 3863009.455819231,
        SpatialReferences.WebMercator);
    private readonly Map _map = new(BasemapStyle.ArcGISTopographic);
    private readonly SimulationSource _simulationSource;
    private readonly DynamicEntityLayer _deliveryLayer;

    public ObservableCollection<CompanyStatistics> CompanyStats { get; set; } = [];

    static MainWindow()
    {
        // Use of Esri location services, including basemaps, requires authentication using either an ArcGIS identity or an API Key.
        // 1. ArcGIS identity: An ArcGIS named user account that is a member of an organization in ArcGIS Online or ArcGIS Enterprise.
        // 2. API key: API key: a permanent key that grants access to location services and premium content in your applications.
        //    Visit your ArcGIS Developers Dashboard to create a new API key or access an existing API key.
        ArcGISRuntimeEnvironment.ApiKey = "";

        if (string.IsNullOrEmpty(ArcGISRuntimeEnvironment.ApiKey))
            MessageBox.Show("Use of Esri location services, including basemaps, requires authentication using a valid API Key", "Authentication Error");

        ArcGISRuntimeEnvironment.Initialize();
    }

    public MainWindow()
    {
        InitializeComponent();

        _simulationSource = new SimulationSource();

        _deliveryLayer = new DynamicEntityLayer(_simulationSource);
        _deliveryLayer.TrackDisplayProperties.MaximumObservations = 20;
        _deliveryLayer.TrackDisplayProperties.ShowPreviousObservations = true;
        _deliveryLayer.TrackDisplayProperties.ShowTrackLine = true;

        var rendererJson = File.ReadAllText(@"Content\PreviousRenderer.json");
        _deliveryLayer.TrackDisplayProperties.PreviousObservationRenderer = Renderer.FromJson(rendererJson);
        _deliveryLayer.Renderer = Renderer.FromJson(File.ReadAllText(@"Content\Renderer.json"));

        _map.OperationalLayers.Add(_deliveryLayer);

        // events to track statistics
        _simulationSource.DynamicEntityReceived += SimulationSource_DynamicEntityReceived;
        _simulationSource.DynamicEntityPurged += SimulationSource_DynamicEntityPurged;
        _simulationSource.DynamicEntityObservationReceived += SimulationSource_DynamicEntityObservationReceived;

        _map.InitialViewpoint = new Viewpoint(_extent);
        _mapView.Map = _map;
        _statisticsPanel.ItemsSource = CompanyStats;
    }

    // called when a new entity is received (EntityIdField not seen before)
    private void SimulationSource_DynamicEntityReceived(object? sender, DynamicEntityEventArgs args)
    {
        Dispatcher.Invoke(() =>
        {
            // try to get the company statistics object for the associated company
            var company = (CompanyColor)GetAttribute<int>(args.DynamicEntity.Attributes, SimulationSource.CompanyField);
            var companyStats = CompanyStats.FirstOrDefault(ts => ts.Company == company);
            if (companyStats is null)
            {
                // create a new comapny statistics object for the UI panel
                companyStats = new CompanyStatistics { Company = company, BackgroundBrush = GetCompanyColorBrush(company, 0.5) };
                CompanyStats.Add(companyStats);
            }

            // track statistics
            ++companyStats.ActiveCount;

            var activePayload = GetAttribute<double>(args.DynamicEntity.Attributes, SimulationSource.PayloadWeightField);
            companyStats.ActivePayloadWeight += activePayload;

            FlashCompanyStatsItem(args.DynamicEntity);
        });
    }

    // called when the last observation of an entity has been purged from the system
    // - DeleteEntityAsync from the data source will cause this
    private void SimulationSource_DynamicEntityPurged(object? sender, DynamicEntityEventArgs args)
    {
        Dispatcher.Invoke(() =>
        {
            // get the company statistics object for the associated company
            var company = (CompanyColor)GetAttribute<int>(args.DynamicEntity.Attributes, SimulationSource.CompanyField);
            var companyStats = CompanyStats.FirstOrDefault(ts => ts.Company == company);
            if (companyStats is null)
                return;

            // track stats
            --companyStats.ActiveCount;
            ++companyStats.TotalDeliveries;

            var activePayload = GetAttribute<double>(args.DynamicEntity.Attributes, SimulationSource.PayloadWeightField);
            companyStats.ActivePayloadWeight -= activePayload;

            FlashCompanyStatsItem(args.DynamicEntity);
        });
    }

    // called when any new observation is received
    private void SimulationSource_DynamicEntityObservationReceived(object? sender, DynamicEntityObservationEventArgs args)
    {
        // get the company statistics object for the associated company
        var company = (CompanyColor)GetAttribute<int>(args.Observation.Attributes, SimulationSource.CompanyField);
        var companyStats = CompanyStats.FirstOrDefault(ts => ts.Company == company);
        if (companyStats is null)
            return;

        // update max speed if necessary
        var speed = GetAttribute<double>(args.Observation.Attributes, SimulationSource.SpeedField);
        if (speed <= companyStats.MaxSpeed)
            return;

        Dispatcher.Invoke(() => companyStats.MaxSpeed = speed);
    }

    private async void GeoViewTapped(object sender, GeoViewInputEventArgs args)
    {
        try
        {
            _mapView.DismissCallout();

            var result = await _mapView.IdentifyLayerAsync(_deliveryLayer, args.Position, 2d, false);
            if (result.GeoElements.FirstOrDefault() is not DynamicEntityObservation observation)
                return;

            if (observation.GetDynamicEntity() is not DynamicEntity entity)
                return;

            // can use Text or TextExpression here - Text will not be updated when a new observation is received
            var calloutDef = new CalloutDefinition(entity)
            {
                Text = $"{entity.Attributes[SimulationSource.EntityIdField]}".Replace(":", " - Truck: "),
                DetailTextExpression = "'Speed: ' + $feature.Speed + ' mph'"
            };
            _mapView.ShowCalloutForGeoElement(entity, args.Position, calloutDef);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Identify Error");
        }
    }

    public static T? GetAttribute<T>(IDictionary<string, object?> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out object? obj) || obj is null)
            return default;

        return (T)Convert.ChangeType(obj, typeof(T));
    }

    private void FlashCompanyStatsItem(DynamicEntity? entity)
    {
        if (entity is null)
            return;

        var company = (CompanyColor)GetAttribute<int>(entity.Attributes, SimulationSource.CompanyField);
        var companyStats = CompanyStats.FirstOrDefault(ts => ts.Company == company);
        if (companyStats is null)
            return;

        Dispatcher.BeginInvoke(async () =>
        {
            companyStats.BackgroundBrush = GetCompanyColorBrush(company, 1.00);
            await Task.Delay(200);
            companyStats.BackgroundBrush = GetCompanyColorBrush(company, 0.80);
            await Task.Delay(200);
            companyStats.BackgroundBrush = GetCompanyColorBrush(company, 0.60);
            await Task.Delay(200);
            companyStats.BackgroundBrush = GetCompanyColorBrush(company, 0.50);
        });
    }

    private static SolidColorBrush GetCompanyColorBrush(CompanyColor companyColor, double opacity)
    {
        var brush = companyColor switch
        {
            CompanyColor.Red => new SolidColorBrush(Colors.Red),
            CompanyColor.Blue => new SolidColorBrush(Colors.Blue),
            CompanyColor.Green => new SolidColorBrush(Colors.Green),
            CompanyColor.Purple => new SolidColorBrush(Colors.Purple),
            _ => new SolidColorBrush(Colors.Black),
        };
        brush.Opacity = opacity;
        return brush;
    }
}

public partial class CompanyStatistics : ObservableObject
{
    [ObservableProperty]
    private CompanyColor _company;

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private int _totalDeliveries;

    [ObservableProperty]
    private double _activePayloadWeight;

    [ObservableProperty]
    private double _maxSpeed;

    [ObservableProperty]
    private SolidColorBrush? _backgroundBrush;
}
