using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Tasks.NetworkAnalysis;
using DeliveryShared;
using Esri.ArcGISRuntime;

namespace DeliverySimulator;

public partial class MainWindow : Window
{
    private const string MobileMapPackagePath = @"Content\SanDiegoNetwork.mmpk";
    private const string StartPointsGeodatabasePath = @"Content\start_points.geodatabase";

    private static readonly Envelope _extent = new(
        -13053376.10252461, 3851361.7018923508, -13029715.044618936, 3863009.455819231,
        SpatialReferences.WebMercator);
    private CancellationTokenSource? _cancellationSource;

    private GeodatabaseFeatureTable? _startPointsTable;
    private RouteTask? _routeTask;
    private ObservationSimulator? _simulator;

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
        _companyColorCombo.SelectedIndex = 1;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var mmpk = await MobileMapPackage.OpenAsync(MobileMapPackagePath);
            _routeTask = await RouteTask.CreateAsync(mmpk.Maps[0].TransportationNetworks[0]);

            var startPointsGeodatabase = await Geodatabase.OpenAsync(StartPointsGeodatabasePath);
            _startPointsTable = startPointsGeodatabase.GetGeodatabaseFeatureTable("StartPoints")
                ?? throw new InvalidOperationException("Could not load start points table");

            _simulator = new ObservationSimulator((CompanyColor)_companyColorCombo.SelectedItem, _extent,
                _routeTask ?? throw new InvalidOperationException("Invalid route task"),
                _startPointsTable ?? throw new InvalidOperationException("Invalid start points table"));

            _cancellationSource = new CancellationTokenSource();
            await _simulator.ConnectAsync(_cancellationSource.Token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
    }

    private void CompanyColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.AddedItems.Count <= 0)
                return;

            Background = (CompanyColor)e.AddedItems[0]! switch
            {
                CompanyColor.Red => new SolidColorBrush(Colors.Red),
                CompanyColor.Blue => new SolidColorBrush(Colors.Blue),
                CompanyColor.Green => new SolidColorBrush(Colors.Green),
                CompanyColor.Purple => new SolidColorBrush(Colors.Purple),
                _ => new SolidColorBrush(Colors.Black),
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
    }
}
