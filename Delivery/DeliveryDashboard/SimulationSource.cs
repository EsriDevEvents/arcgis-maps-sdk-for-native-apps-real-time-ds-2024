using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.RealTime;
using DeliveryShared;

namespace DeliveryDashboard;

// Custom data source that reads and aggregates UDP messages into a single source
// Note: extending DynamicEntityDataSource may be slightly different on other platforms
public partial class SimulationSource : DynamicEntityDataSource
{
    public const string EntityIdField = "EntityId";
    public const string CompanyField = "Company";
    public const string NameField = "Name";
    public const string HeadingField = "Heading";
    public const string PayloadWeightField = "PayloadWeight";
    public const string SpeedField = "Speed";

    private CancellationTokenSource? _cancellationSource;
    private Task? _receiveTask;

    public SimulationSource()
    {
    }

    // loads the data and provides schema and unique entity field for the API
    // - called explicitly by LoadAsync or implicitly when layer is rendered in a view
    // - a call to LoadAsync will not complete until this async method returns - this allows for async data preparation
    protected override Task<DynamicEntityDataSourceInfo> OnLoadAsync()
    {
        // forward schema / entity Id / metadata to the API
        var info = new DynamicEntityDataSourceInfo(EntityIdField, GetSchema())
        {
            SpatialReference = SpatialReferences.Wgs84
        };
        return Task.FromResult(info);
    }

    // binds to the UDP endpoint and starts the flow of observations
    // - called explicitly by ConnectAsync or implicitly when layer is rendered in a view
    protected override Task OnConnectAsync(CancellationToken cancellationToken)
    {
        _cancellationSource = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveDataAsync(_cancellationSource.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    // stops the flow of observations be disconnecting from UDP endpoint
    protected override async Task OnDisconnectAsync()
    {
        _cancellationSource?.Cancel();
        if (_receiveTask is not null)
            await _receiveTask;
    }

    // the main processing loop of the data source
    // - parses UDP messages from simulators and adds observations to the data source
    public async Task ReceiveDataAsync(CancellationToken cancellationToken)
    {
        // bind the UDP endpoint to parse and aggregate messages from multiple simulators
        IPEndPoint endPoint = new(IPAddress.Any, 8080);
        using UdpClient udpClient = new();
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.Bind(endPoint);

        // loop until DisconnectAsync cancels the task
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // receive and parse the message from a simulator (messages are JSON format)
                byte[] receiveBytes = udpClient.Receive(ref endPoint);
                string message = Encoding.UTF8.GetString(receiveBytes);
                var locationMessage = JsonSerializer.Deserialize<LocationMessage>(message)
                    ?? throw new InvalidOperationException("Invalid Message");

                // MessageType property tells what kind of message we got
                if (locationMessage.MessageType != (int)MessageType.Delete)
                {
                    // add an observation
                    var point = new MapPoint(locationMessage.X, locationMessage.Y, SpatialReferences.Wgs84);
                    var attrs = CreateObservationAttributes(locationMessage);
                    AddObservation(point, attrs);
                }
                else
                {
                    // delete an entity if the simulator says the delivery is complete
                    await DeleteEntityAsync(locationMessage.EntityId);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }
    }

    private static List<KeyValuePair<string, object?>> CreateObservationAttributes(LocationMessage locationMessage)
    {
        return
        [
            new(EntityIdField, locationMessage.EntityId),
            new(CompanyField, locationMessage.Company),
            new(NameField, locationMessage.Name),
            new(PayloadWeightField, locationMessage.PayloadWeight),
            new(SpeedField, locationMessage.Speed),
            new(HeadingField, locationMessage.Heading)
        ];
    }

    private static List<Field> GetSchema()
    {
        return
        [
            new(FieldType.Text, EntityIdField, EntityIdField.ToUpper(), 256),
            new(FieldType.Int32, CompanyField, CompanyField.ToUpper(), 4),
            new(FieldType.Text, NameField, NameField.ToUpper(), 256),
            new(FieldType.Float64, PayloadWeightField, PayloadWeightField.ToUpper(), 8),
            new(FieldType.Float64, SpeedField, SpeedField.ToUpper(), 8),
            new(FieldType.Float64, HeadingField, HeadingField.ToUpper(), 8)
        ];
    }
}
