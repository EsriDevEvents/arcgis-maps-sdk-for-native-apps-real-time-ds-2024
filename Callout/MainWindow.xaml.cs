using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.RealTime;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;

namespace Callout
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private static readonly Envelope _extent
            = new(-112.07191257948598, 40.494668733340376, -111.70060766277847, 40.644571817187085, SpatialReferences.Wgs84);

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
            InitializeMap();
            _mapView.GeoViewTapped += GeoViewTapped;
        }

        #region Properties

        public Map Map { get; } = new(BasemapStyle.ArcGISStreets) { InitialViewpoint = new Viewpoint(_extent) };

        public DynamicEntityLayer DynamicEntityLayer
        {
            get => _dynamicEntityLayer;
            set
            {
                _dynamicEntityLayer = value;
                OnPropertyChanged();
            }
        }
        private DynamicEntityLayer _dynamicEntityLayer;

        public bool UseObservation { get; set; }

        public bool ShowCallout
        {
            get => _showCallout;
            set
            {
                _showCallout = value;
                OnPropertyChanged();
            }
        }
        private bool _showCallout;

        public bool ShowSelection
        {
            get => _showSelection;
            set
            {
                _showSelection = value;
                OnPropertyChanged();
            }
        }
        private bool _showSelection = true;

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? property = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

        #endregion

        #region UI

        [MemberNotNull(nameof(_dynamicEntityLayer))]
        private void InitializeMap()
        {
            // Set selection color for better visibility
            _mapView.SelectionProperties!.Color = Color.Red;

            // create the stream service
            var streamServiceUrl = "https://realtimegis2016.esri.com:6443/arcgis/rest/services/SandyVehicles/StreamServer";
            var streamService = new ArcGISStreamService(new Uri(streamServiceUrl));

            // create the layer
            _dynamicEntityLayer = new DynamicEntityLayer(streamService);
            _dynamicEntityLayer.TrackDisplayProperties.ShowPreviousObservations = true;
            _dynamicEntityLayer.TrackDisplayProperties.ShowTrackLine = true;
            _dynamicEntityLayer.TrackDisplayProperties.MaximumObservations = 50;

            // add the layer to the map
            Map.OperationalLayers.Add(_dynamicEntityLayer);
        }

        private void Mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UseObservation = ((ComboBox)sender).SelectedIndex == 0;
        }

        #endregion

        private async void GeoViewTapped(object? sender, GeoViewInputEventArgs args)
        {
            args.Handled = true;

            // clear any previous selection and callout
            DynamicEntityLayer.ClearSelection();
            _mapView.DismissCallout();

            // identify an observation (similar to other layer types)
            // - DynamicEntityLayer only identifies observations (not DynamicEntity objects)
            var results = await _mapView.IdentifyLayerAsync(DynamicEntityLayer, args.Position, 2d, false);
            if (results.GeoElements.FirstOrDefault() is not DynamicEntityObservation observation)
                return;

            if (UseObservation)
            {
                if (ShowSelection)
                {
                    // select the dynamic entity observation (non-moving)
                    DynamicEntityLayer.SelectDynamicEntityObservation(observation);
                }
                if (ShowCallout)
                {
                    // show the static callout for this observation (non-moving)
                    var calloutDef = new CalloutDefinition(observation)
                    {
                        TextExpression = "$feature.vehicletype + ': ' + Replace($feature.vehiclename, 'TRUCK', '')",
                        DetailTextExpression = "'Speed: ' + $feature.speed + ' mph'"
                    };
                    _mapView.ShowCalloutForGeoElement(observation, args.Position, calloutDef);
                }
            }
            else
            {
                // retrieve the DynamicEntity associated with the identified observation
                var entity = observation.GetDynamicEntity();
                if (entity is null)
                    return;

                if (ShowSelection)
                {
                    // select the dynamic entity (moving)
                    DynamicEntityLayer.SelectDynamicEntity(entity);
                }
                if (ShowCallout)
                {
                    // the callout takes care of moving and updating when the dynamic entity changes
                    // TextExpression and DetailTextExpression will also change when a new observation comes in
                    var calloutDef = new CalloutDefinition(entity)
                    {
                        TextExpression = "$feature.vehicletype + ': ' + Replace($feature.vehiclename, 'TRUCK', '')",
                        DetailTextExpression = "'Speed: ' + $feature.speed + ' mph'"
                    };
                    _mapView.ShowCalloutForGeoElement(entity, args.Position, calloutDef);
                }
            }
        }
    }
}
