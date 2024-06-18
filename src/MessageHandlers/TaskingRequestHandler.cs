using Microsoft.Azure.SpaceFx.MessageFormats.HostServices.Sensor;

namespace Microsoft.Azure.SpaceFx.HostServices.Sensor;

public partial class MessageHandler<T> {

    private void TaskingRequestHandler(MessageFormats.HostServices.Sensor.TaskingRequest? message, MessageFormats.Common.DirectToApp fullMessage) {
        if (message == null) return;
        {
            if (message == null) return;
            using (var scope = _serviceProvider.CreateScope()) {
                DateTime maxTimeToWait = DateTime.Now.Add(TimeSpan.FromMilliseconds(_appConfig.MESSAGE_RESPONSE_TIMEOUT_MS));
                MessageFormats.HostServices.Sensor.TaskingResponse? returnResponse = null;

                _logger.LogInformation("Processing message type '{messageType}' from '{sourceApp}' (trackingId: '{trackingId}' / correlationId: '{correlationId}')", message.GetType().Name, fullMessage.SourceAppId, message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);

                _logger.LogDebug("Passing message '{messageType}' to plugins (trackingId: '{trackingId}' / correlationId: '{correlationId}')", message.GetType().Name, message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);

                MessageFormats.HostServices.Sensor.TaskingRequest? pluginResult =
                   _pluginLoader.CallPlugins<MessageFormats.HostServices.Sensor.TaskingRequest?, Plugins.PluginBase>(
                       orig_request: message,
                       pluginDelegate: _pluginDelegates.TaskingRequest);

                _logger.LogDebug("Plugins finished processing '{messageType}' (trackingId: '{trackingId}' / correlationId: '{correlationId}')", message.GetType().Name, message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);

                // Update the request if our plugins changed it
                if (pluginResult == null) {
                    _logger.LogInformation("Plugins nullified '{messageType}'.  Dropping Message (trackingId: '{trackingId}' / correlationId: '{correlationId}')", message.GetType().Name, message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);
                    return;
                }

                // Update the request to match what we got from the plugins
                message = pluginResult;

                // Register a callback event to catch the response
                void TaskingResponseCallback(object? sender, MessageFormats.HostServices.Sensor.TaskingResponse _response) {
                    if (_response.ResponseHeader.TrackingId == message.RequestHeader.TrackingId) {
                        _logger.LogDebug("Received anticipated reply to '{messageType}' of type '{responseType}' from '{senderId}' (trackingId: '{trackingId}' / correlationId: '{correlationId}')", message.GetType().Name, _response.GetType().Name, sender?.ToString(), message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);
                        returnResponse = _response;
                        MessageHandler<MessageFormats.HostServices.Sensor.TaskingResponse>.MessageReceivedEvent -= TaskingResponseCallback;
                    }
                }

                // MTS is online.  Send the message and wait for a response
                if (_appConfig.ENABLE_ROUTING_TO_MTS) {
                    MessageHandler<MessageFormats.HostServices.Sensor.TaskingResponse>.MessageReceivedEvent += TaskingResponseCallback;

                    _logger.LogDebug("Forwarding '{messageType}' to {mtsAppId} (trackingId: '{trackingId}' / correlationId: '{correlationId}')", message.GetType().Name, $"platform-{nameof(MessageFormats.Common.PlatformServices.Mts).ToLower()}", message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);

                    _client.DirectToApp(appId: $"platform-{nameof(MessageFormats.Common.PlatformServices.Mts).ToLower()}", message: message).Wait();

                    while (returnResponse == null && DateTime.Now <= maxTimeToWait) {
                        Task.Delay(100).Wait();
                    }

                    returnResponse = returnResponse ?? new() {
                        ResponseHeader = new() {
                            TrackingId = message.RequestHeader.TrackingId,
                            CorrelationId = message.RequestHeader.CorrelationId,
                            Status = MessageFormats.Common.StatusCodes.Timeout,
                            Message = $"Timed out waiting for a response from 'platform-{nameof(MessageFormats.Common.PlatformServices.Mts).ToLower()}'."
                        }
                    };
                } else {
                    _logger.LogWarning("Unable to process '{messageType}'.  Routing to '{mtsAppId}' is disabled (ENABLE_ROUTING_TO_MTS = false) (trackingId: '{trackingId}' / correlationId: '{correlationId}')", message.GetType().Name, $"platform-{nameof(MessageFormats.Common.PlatformServices.Mts).ToLower()}", message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);
                    returnResponse = returnResponse ?? new() {
                        ResponseHeader = new() {
                            TrackingId = message.RequestHeader.TrackingId,
                            CorrelationId = message.RequestHeader.CorrelationId,
                            Status = MessageFormats.Common.StatusCodes.Rejected,
                            Message = $"Routing to 'platform-{nameof(MessageFormats.Common.PlatformServices.Mts).ToLower()}' has been disabled by config.  (ENABLE_ROUTING_TO_MTS = false)"
                        }
                    };
                }

                _logger.LogDebug("Passing message '{messageType}' and '{responseType}' to plugins (trackingId: '{trackingId}' / correlationId: '{correlationId}')", message.GetType().Name, returnResponse.GetType().Name, message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);

                (MessageFormats.HostServices.Sensor.TaskingRequest? output_request, MessageFormats.HostServices.Sensor.TaskingResponse? output_response) =
                                                _pluginLoader.CallPlugins<MessageFormats.HostServices.Sensor.TaskingRequest?, Plugins.PluginBase, MessageFormats.HostServices.Sensor.TaskingResponse>(
                                                    orig_request: message, orig_response: returnResponse,
                                                    pluginDelegate: _pluginDelegates.TaskingResponse);

                _logger.LogDebug("Plugins finished processing '{messageType}' and '{responseType}' (trackingId: '{trackingId}' / correlationId: '{correlationId}')", message.GetType().Name, returnResponse.GetType().Name, message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);


                if (output_response == null || output_request == null) {
                    _logger.LogInformation("Plugins nullified '{messageType}' or '{output_requestMessageType}'.  Dropping Message (trackingId: '{trackingId}' / correlationId: '{correlationId}')", returnResponse.GetType().Name, message.GetType().Name, message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);
                    return;
                }

                _logger.LogInformation("Response to request message type '{messageType}' is '{returnResponseStatus}' (Response Message is '{returnResponseMessage}') (trackingId: '{trackingId}' / correlationId: '{correlationId}')", message.GetType().Name, returnResponse.ResponseHeader.Status, returnResponse.ResponseHeader.Message, message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);


                if (returnResponse.ResponseHeader.Status == MessageFormats.Common.StatusCodes.Successful) {
                    _logger.LogTrace("Caching '{messageType}' (trackingId: '{trackingId}' / correlationId: '{correlationId}')", message.GetType().Name, message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);
                    _client.SaveCacheItem(cacheItem: message, cacheItemName: message.RequestHeader.TrackingId, expiration: DateTime.UtcNow.AddMinutes(15)).Wait();
                } else {
                    _logger.LogWarning("Unable to process '{messageType}'.  Response Status is '{returnResponseStatus}'.  Response Message is '{returnResponseMessage}' (trackingId: '{trackingId}' / correlationId: '{correlationId}')", message.GetType().Name, returnResponse.ResponseHeader.Status, returnResponse.ResponseHeader.Message, message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);
                }

                returnResponse = output_response;
                message = output_request;

                _logger.LogInformation("Routing message type '{messageType}' to '{appId}' (trackingId: '{trackingId}' / correlationId: '{correlationId}')", returnResponse.GetType().Name, fullMessage.SourceAppId, message.RequestHeader.TrackingId, message.RequestHeader.CorrelationId);
                UpdateSensorBroadcastResults(taskingRequest: message, taskingResponse: returnResponse, fullMessage: fullMessage);
                _client.DirectToApp(appId: fullMessage.SourceAppId, message: returnResponse);
            };
        }
    }

    private void UpdateSensorBroadcastResults(MessageFormats.HostServices.Sensor.TaskingRequest taskingRequest, MessageFormats.HostServices.Sensor.TaskingResponse taskingResponse, MessageFormats.Common.DirectToApp fullMessage) {
        // Retrieve the Sensor's collection for broadcast data
        ListValue? sensor_subscriptions = _client.GetCacheItem<ListValue>(cacheItemName: CACHE_KEYS.SENSOR_SUBSCRIPTIONS_PREFIX + taskingRequest.SensorID).Result ?? new ListValue();

        if (taskingResponse.ResponseHeader.Status == MessageFormats.Common.StatusCodes.Successful) {
            _logger.LogInformation("{MethodName} enabling access to '{orig_app}' for sensor data broadcast from '{sensorId}' (trackingId: '{trackingId}' / correlationId: '{correlationId}')",
                                    nameof(UpdateSensorBroadcastResults),
                                    fullMessage.SourceAppId,
                                    taskingRequest.SensorID,
                                    taskingRequest.RequestHeader.TrackingId,
                                    taskingRequest.RequestHeader.CorrelationId);

            // If the sensor subscription isn't here, go ahead and add it
            if (!sensor_subscriptions.Values.Contains(new Value() { StringValue = fullMessage.SourceAppId })) {
                sensor_subscriptions.Values.Add(new Value() { StringValue = fullMessage.SourceAppId });
                _ = _client.SaveCacheItem(cacheItem: sensor_subscriptions, cacheItemName: CACHE_KEYS.SENSOR_SUBSCRIPTIONS_PREFIX + taskingRequest.SensorID, expiration: DateTime.UtcNow.AddHours(24)).Result;
            }
        } else {
            _logger.LogInformation("{MethodName} revoking access to '{orig_app}' for sensor data broadcast from '{sensorId}' (trackingId: '{trackingId}' / correlationId: '{correlationId}')",
                                    nameof(UpdateSensorBroadcastResults),
                                    fullMessage.SourceAppId,
                                    taskingRequest.SensorID,
                                    taskingRequest.RequestHeader.TrackingId,
                                    taskingRequest.RequestHeader.CorrelationId);

            if (sensor_subscriptions.Values.Contains(new Google.Protobuf.WellKnownTypes.Value() { StringValue = fullMessage.SourceAppId })) {
                sensor_subscriptions.Values.Remove(new Google.Protobuf.WellKnownTypes.Value() { StringValue = fullMessage.SourceAppId });
                _ = _client.SaveCacheItem(cacheItem: sensor_subscriptions, cacheItemName: CACHE_KEYS.SENSOR_SUBSCRIPTIONS_PREFIX + taskingRequest.SensorID, expiration: DateTime.UtcNow.AddHours(24)).Result;
            }
        }
    }

}
