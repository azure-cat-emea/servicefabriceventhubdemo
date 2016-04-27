// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#region Using Directives



#endregion

namespace Microsoft.AzureCat.Samples.DeviceActorService
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.AzureCat.Samples.DeviceActorService.Interfaces;
    using Microsoft.AzureCat.Samples.PayloadEntities;
    using Microsoft.ServiceBus.Messaging;
    using Microsoft.ServiceFabric.Actors.Runtime;
    using Microsoft.ServiceFabric.Data;
    using Newtonsoft.Json;

    [ActorService(Name = "DeviceActorService")]
    [StatePersistence(StatePersistence.Persisted)]
    public class DeviceActor : Actor, IDeviceActor
    {
        #region Private Fields

        private EventHubClient eventHubClient;

        #endregion

        #region Private Methods

        public void CreateEventHubClient()
        {
            DeviceActorService deviceActorService = this.ActorService as DeviceActorService;
            if (string.IsNullOrWhiteSpace(deviceActorService?.ServiceBusConnectionString) ||
                string.IsNullOrWhiteSpace(deviceActorService.EventHubName))
            {
                return;
            }
            this.eventHubClient = EventHubClient.CreateFromConnectionString(
                deviceActorService.ServiceBusConnectionString,
                deviceActorService.EventHubName);
            ActorEventSource.Current.Message($"Id=[{this.Id}] EventHubClient created");
        }

        #endregion

        #region Private Constants

        //************************************
        // States
        //************************************
        private const string QueueState = "queue";
        private const string MetadataState = "metadata";

        //***************************
        // Constants
        //***************************
        private const string DeviceId = "id";
        private const string Value = "value";
        private const string Timestamp = "timestamp";

        //************************************
        // Default Values
        //************************************
        private const int MinThresholdDefault = 30;
        private const int MaxThresholdDefault = 50;

        //************************************
        // Constants
        //************************************
        private const string Unknown = "Unknown";

        #endregion

        #region Actor Methods

        protected override async Task OnActivateAsync()
        {
            try
            {
                // Initialize States
                await this.StateManager.TryAddStateAsync(QueueState, new Queue<Payload>());
                ConditionalValue<Device> result = await this.StateManager.TryGetStateAsync<Device>(MetadataState);
                if (!result.HasValue)
                {
                    // The device id is a string with the following format: device<number>
                    string deviceIdAsString = this.Id.ToString();
                    long deviceId;
                    long.TryParse(deviceIdAsString.Substring(6), out deviceId);

                    Device metadata = new Device
                    {
                        DeviceId = deviceId,
                        Name = deviceIdAsString,
                        MinThreshold = MinThresholdDefault,
                        MaxThreshold = MaxThresholdDefault,
                        Model = Unknown,
                        Type = Unknown,
                        Manufacturer = Unknown,
                        City = Unknown,
                        Country = Unknown
                    };
                    await this.StateManager.TryAddStateAsync(MetadataState, metadata);
                }

                // Create EventHubClient
                this.CreateEventHubClient();
            }
            catch (Exception ex)
            {
                // Trace exception as ETW event
                ActorEventSource.Current.Error(ex);

                // Trace exception using Application Insights
                Program.TelemetryClient.TrackException(
                    ex,
                    new Dictionary<string, string>
                    {
                        {"ActorType", "DeviceActor"},
                        {"ActorId", this.Id.ToString()},
                        {"ServiceName", this.ActorService.Context.ServiceName.ToString()},
                        {"Partition", this.ActorService.Context.PartitionId.ToString()},
                        {"Node", this.ActorService.Context.NodeContext.NodeName}
                    });
            }
        }

        protected override Task OnDeactivateAsync()
        {
            return Task.FromResult(true);
        }

        #endregion

        #region IDeviceActor Methods

        public async Task ProcessEventAsync(Payload payload)
        {
            try
            {
                // Validate payload
                if (payload == null)
                {
                    return;
                }

                // Enqueue the new payload
                ConditionalValue<Queue<Payload>> queueResult = await this.StateManager.TryGetStateAsync<Queue<Payload>>(QueueState);
                if (queueResult.HasValue)
                {
                    Queue<Payload> queue = queueResult.Value;
                    queue.Enqueue(payload);

                    // The actor keeps the latest n payloads in a queue, where n is  
                    // defined by the QueueLength parameter in the Settings.xml file.
                    if (queue.Count > ((DeviceActorService) this.ActorService).QueueLength)
                    {
                        queue.Dequeue();
                    }
                }

                // Retrieve Metadata from the Actor state
                ConditionalValue<Device> metadataResult = await this.StateManager.TryGetStateAsync<Device>(MetadataState);
                Device metadata = metadataResult.HasValue
                    ? metadataResult.Value
                    : new Device
                    {
                        DeviceId = payload.DeviceId,
                        Name = payload.Name,
                        MinThreshold = MinThresholdDefault,
                        MaxThreshold = MaxThresholdDefault,
                        Model = Unknown,
                        Type = Unknown,
                        Manufacturer = Unknown,
                        City = Unknown,
                        Country = Unknown
                    };

                // Trace ETW event
                ActorEventSource.Current.Message($"Id=[{payload.DeviceId}] Value=[{payload.Value}] Timestamp=[{payload.Timestamp}]");

                // This ETW event is traced to a separate table with respect to the message
                ActorEventSource.Current.Telemetry(metadata, payload);

                // Track Application Insights event
                Program.TelemetryClient.TrackEvent(
                    new EventTelemetry
                    {
                        Name = "Telemetry",
                        Properties =
                        {
                            {"DeviceId", metadata.DeviceId.ToString(CultureInfo.InvariantCulture)},
                            {"Name", metadata.Name},
                            {"City", metadata.City},
                            {"Country", metadata.Country},
                            {"Manufacturer", metadata.Manufacturer},
                            {"Model", metadata.Model},
                            {"Type", metadata.Type},
                            {"MinThreshold", metadata.MinThreshold.ToString(CultureInfo.InvariantCulture)},
                            {"MaxThreshold", metadata.MaxThreshold.ToString(CultureInfo.InvariantCulture)},
                            {"Value", payload.Value.ToString(CultureInfo.InvariantCulture)},
                            {"Status", payload.Status},
                            {"Timestamp", payload.Timestamp.ToString(CultureInfo.InvariantCulture)},
                            {"ActorType", "DeviceActor"},
                            {"ActorId", this.Id.ToString()},
                            {"ServiceName", this.ActorService.Context.ServiceName.ToString()},
                            {"Partition", this.ActorService.Context.PartitionId.ToString()},
                            {"Node", this.ActorService.Context.NodeContext.NodeName}
                        },
                        Metrics =
                        {
                            {"TelemetryValue", payload.Value},
                            {"TelemetryCount", 1}
                        },
                    });

                // Real spikes happen when both Spike1 and Spike2 are equal to 1. By the way, you can change the logic
                if (payload.Value < metadata.MinThreshold || payload.Value > metadata.MaxThreshold)
                {
                    // Create EventData object with the payload serialized in JSON format 
                    Alert alert = new Alert
                    {
                        DeviceId = metadata.DeviceId,
                        Name = metadata.Name,
                        MinThreshold = metadata.MinThreshold,
                        MaxThreshold = metadata.MaxThreshold,
                        Model = metadata.Model,
                        Type = metadata.Type,
                        Manufacturer = metadata.Manufacturer,
                        City = metadata.City,
                        Country = metadata.Country,
                        Status = payload.Status,
                        Value = payload.Value,
                        Timestamp = payload.Timestamp
                    };
                    string json = JsonConvert.SerializeObject(alert);
                    using (EventData eventData = new EventData(Encoding.UTF8.GetBytes(json))
                    {
                        PartitionKey = payload.Name
                    })
                    {
                        // Create custom properties
                        eventData.Properties.Add(DeviceId, payload.DeviceId);
                        eventData.Properties.Add(Value, payload.Value);
                        eventData.Properties.Add(Timestamp, payload.Timestamp);

                        // Send the event to the event hub
                        await this.eventHubClient.SendAsync(eventData);

                        // Trace ETW event
                        ActorEventSource.Current.Message($"[Alert] Id=[{payload.DeviceId}] Value=[{payload.Value}] Timestamp=[{payload.Timestamp}]");

                        // This ETW event is traced to a separate table
                        ActorEventSource.Current.Alert(metadata, payload);

                        // Track Application Insights event
                        Program.TelemetryClient.TrackEvent(
                            new EventTelemetry
                            {
                                Name = "Alert",
                                Properties =
                                {
                                    {"DeviceId", metadata.DeviceId.ToString(CultureInfo.InvariantCulture)},
                                    {"Name", metadata.Name},
                                    {"City", metadata.City},
                                    {"Country", metadata.Country},
                                    {"Manufacturer", metadata.Manufacturer},
                                    {"Model", metadata.Model},
                                    {"Type", metadata.Type},
                                    {"MinThreshold", metadata.MinThreshold.ToString(CultureInfo.InvariantCulture)},
                                    {"MaxThreshold", metadata.MaxThreshold.ToString(CultureInfo.InvariantCulture)},
                                    {"Value", payload.Value.ToString(CultureInfo.InvariantCulture)},
                                    {"Status", payload.Status},
                                    {"Timestamp", payload.Timestamp.ToString(CultureInfo.InvariantCulture)},
                                    {"ActorType", "DeviceActor"},
                                    {"ActorId", this.Id.ToString()},
                                    {"ServiceName", this.ActorService.Context.ServiceName.ToString()},
                                    {"Partition", this.ActorService.Context.PartitionId.ToString()},
                                    {"Node", this.ActorService.Context.NodeContext.NodeName}
                                },
                                Metrics =
                                {
                                    {"AlertValue", payload.Value},
                                    {"AlertCount", 1}
                                },
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                // Trace exception as ETW event
                ActorEventSource.Current.Error(ex);

                // Trace exception using Application Insights
                Program.TelemetryClient.TrackException(
                    ex,
                    new Dictionary<string, string>
                    {
                        {"ActorType", "DeviceActor"},
                        {"ActorId", this.Id.ToString()},
                        {"ServiceName", this.ActorService.Context.ServiceName.ToString()},
                        {"Partition", this.ActorService.Context.PartitionId.ToString()},
                        {"Node", this.ActorService.Context.NodeContext.NodeName}
                    });
            }
        }

        public async Task SetData(Device data)
        {
            // Validate parameter
            if (data == null)
            {
                return;
            }

            // Save metadata to Actor state
            await this.StateManager.SetStateAsync(MetadataState, data);

            // Trace ETW event
            ActorEventSource.Current.Metadata(data);

            // Track Application Insights event
            Program.TelemetryClient.TrackEvent(
                new EventTelemetry
                {
                    Name = "Metadata",
                    Properties =
                    {
                        {"DeviceId", data.DeviceId.ToString(CultureInfo.InvariantCulture)},
                        {"Name", data.Name},
                        {"City", data.City},
                        {"Country", data.Country},
                        {"Manufacturer", data.Manufacturer},
                        {"Model", data.Model},
                        {"Type", data.Type},
                        {"MinThreshold", data.MinThreshold.ToString(CultureInfo.InvariantCulture)},
                        {"MaxThreshold", data.MaxThreshold.ToString(CultureInfo.InvariantCulture)},
                        {"ActorType", "DeviceActor"},
                        {"ActorId", this.Id.ToString()},
                        {"ServiceName", this.ActorService.Context.ServiceName.ToString()},
                        {"Partition", this.ActorService.Context.PartitionId.ToString()},
                        {"Node", this.ActorService.Context.NodeContext.NodeName}
                    }
                });
        }

        public async Task<Device> GetData()
        {
            // Retrieve Metadata from the Actor state
            Device metadata;
            ConditionalValue<Device> metadataResult = await this.StateManager.TryGetStateAsync<Device>(MetadataState);
            if (metadataResult.HasValue)
            {
                metadata = metadataResult.Value;
            }
            else
            {
                // The device id is a string with the following format: device<number>
                string deviceIdAsString = this.Id.ToString();
                long deviceId;
                long.TryParse(deviceIdAsString.Substring(6), out deviceId);

                metadata = new Device
                {
                    DeviceId = deviceId,
                    Name = deviceIdAsString,
                    MinThreshold = MinThresholdDefault,
                    MaxThreshold = MaxThresholdDefault,
                    Model = Unknown,
                    Type = Unknown,
                    Manufacturer = Unknown,
                    City = Unknown,
                    Country = Unknown
                };
            }
            return metadata;
        }

        #endregion
    }
}