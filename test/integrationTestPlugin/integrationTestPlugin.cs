

namespace Microsoft.Azure.SpaceFx.HostServices.Sensor.Plugins;
public class IntegrationTestPlugin : Microsoft.Azure.SpaceFx.HostServices.Sensor.Plugins.PluginBase {
    // Temperature sensor simulates a broadcast sensor to validate the unknown destination scenario works
    const string SENSOR_TEMPERATURE_ID = "DemoTemperatureSensor";

    // HelloWorld sensor is a simple request/reply sensor to validate the direct path scenario works
    const string SENSOR_ID = "DemoHelloWorldSensor";
    readonly ConcurrentBag<string> CLIENT_IDS = new();
    public override ILogger Logger { get; set; }

    public IntegrationTestPlugin() {
        LoggerFactory loggerFactory = new();
        Logger = loggerFactory.CreateLogger<IntegrationTestPlugin>();
    }

    public override Task BackgroundTask() => Task.Run(async () => {
        Random random_num_generator = new Random();
        int temperatureReading = 0;
        string? clientID;

        // Generate some fake Sensor Data every second after a tasking request.
        while (true) {
            // Loop through the client IDs we received for sensor data and send it out on direct path
            while (CLIENT_IDS.TryTake(out clientID)) {
                SensorData sensorData = new() {
                    ResponseHeader = new() {
                        TrackingId = Guid.NewGuid().ToString(),
                        CorrelationId = Guid.NewGuid().ToString(),
                        Status = StatusCodes.Successful,
                        AppId = clientID
                    },
                    DestinationAppId = clientID,
                    SensorID = SENSOR_ID,
                    GeneratedTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                    ExpirationTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.MaxValue.ToUniversalTime()),
                    Data = Google.Protobuf.WellKnownTypes.Any.Pack(new Google.Protobuf.WellKnownTypes.StringValue() { Value = "Hello Space World!" })
                };

                Logger.LogInformation("Sending SensorData '{sensor}' to {clientId}", SENSOR_ID, clientID);

                // Route the message back to the Sensor Service so it looks like it came in as request sensor data
                await Core.DirectToApp(Core.GetAppID().Result, sensorData);
            }


            // Generate a fake sensor and send it out without any destination (unknown destination user scenario)
            temperatureReading = random_num_generator.Next(10, 50);

            SensorData temperatureProbe = new() {
                ResponseHeader = new() {
                    TrackingId = Guid.NewGuid().ToString(),
                },
                SensorID = SENSOR_TEMPERATURE_ID,
                Data = Google.Protobuf.WellKnownTypes.Any.Pack(new Google.Protobuf.WellKnownTypes.StringValue() { Value = $"Temperature: {temperatureReading}" })
            };

            Logger.LogInformation("Sending SensorData '{sensor}'", SENSOR_TEMPERATURE_ID);

            // Route the message back to the Sensor Service so it looks like it came in as request sensor data
            await Core.DirectToApp(Core.GetAppID().Result, temperatureProbe);

            await Task.Delay(1000);
        }
    });

    public override void ConfigureLogging(ILoggerFactory loggerFactory) => Logger = loggerFactory.CreateLogger<IntegrationTestPlugin>();

    public override Task<PluginHealthCheckResponse> PluginHealthCheckResponse() => Task.FromResult(new MessageFormats.Common.PluginHealthCheckResponse());
    public override Task<SensorData?> SensorData(SensorData? input_request) => Task.FromResult(input_request);
    public override Task<SensorsAvailableRequest?> SensorsAvailableRequest(SensorsAvailableRequest? input_request) => Task.FromResult(input_request);

    /// <summary>
    /// Add the fake sensors back to the response
    /// </summary>
    public override Task<(SensorsAvailableRequest?, SensorsAvailableResponse?)> SensorsAvailableResponse(SensorsAvailableRequest? input_request, SensorsAvailableResponse? input_response) => Task.Run(() => {
        if (input_request == null || input_response == null) return (input_request, input_response);

        input_response.ResponseHeader.Status = StatusCodes.Successful;
        input_response.Sensors.Add(new SensorsAvailableResponse.Types.SensorAvailable() { SensorID = SENSOR_ID });
        input_response.Sensors.Add(new SensorsAvailableResponse.Types.SensorAvailable() { SensorID = SENSOR_TEMPERATURE_ID });

        return (input_request, input_response);
    });

    public override Task<TaskingPreCheckRequest?> TaskingPreCheckRequest(TaskingPreCheckRequest? input_request) => Task.FromResult(input_request);

    /// <summary>
    /// Respond back with a successful status if the app is tasking the fake sensor
    /// </summary>
    public override Task<(TaskingPreCheckRequest?, TaskingPreCheckResponse?)> TaskingPreCheckResponse(TaskingPreCheckRequest? input_request, TaskingPreCheckResponse? input_response) => Task.Run(() => {
        if (input_request == null || input_response == null) return (input_request, input_response);

        // Flip it to success
        input_response.ResponseHeader.Status = StatusCodes.Successful;

        return (input_request, input_response);
    });

    public override Task<TaskingRequest?> TaskingRequest(TaskingRequest? input_request) => Task.FromResult(input_request);

    /// <summary>
    /// Add the requesting ID to the client IDs
    /// </summary>
    public override Task<(TaskingRequest?, TaskingResponse?)> TaskingResponse(TaskingRequest? input_request, TaskingResponse? input_response) => Task.Run(() => {
        if (input_request == null || input_response == null) return (input_request, input_response);

        // Flip it to success
        input_response.ResponseHeader.Status = StatusCodes.Successful;
        input_response.ResponseHeader.Message = "Response message intercepted by integration test plugin";
        input_response.SensorID = input_request.SensorID;

        // Add the client ID to the list so we can direct send it Sensor Data
        if (!CLIENT_IDS.Contains(input_request.RequestHeader.AppId))
            CLIENT_IDS.Add(input_request.RequestHeader.AppId);


        return (input_request, input_response);
    });
}