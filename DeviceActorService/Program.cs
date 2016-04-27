// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#region Using Directives



#endregion

namespace Microsoft.AzureCat.Samples.DeviceActorService
{
    using System;
    using System.Threading;
    using Microsoft.ApplicationInsights;
    using Microsoft.ServiceFabric.Actors.Runtime;

    internal static class Program
    {
        /// <summary>
        /// Application Insights Telemetry Client static field
        /// </summary>
        internal static TelemetryClient TelemetryClient;

        /// <summary>
        /// This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            try
            {
                TelemetryClient = new TelemetryClient();
            }
            catch (Exception)
            {
                // ignored
            }
            try
            {
                // Create default garbage collection settings for all the actor types
                ActorGarbageCollectionSettings actorGarbageCollectionSettings = new ActorGarbageCollectionSettings(300, 60);

                // This line registers your actor class with the Fabric Runtime.
                // The contents of your ServiceManifest.xml and ApplicationManifest.xml files
                // are automatically populated when you build this project.
                // For more information, see http://aka.ms/servicefabricactorsplatform

                ActorRuntime.RegisterActorAsync<DeviceActor>(
                    (context, actorType) => new DeviceActorService(
                        context,
                        actorType,
                        () => new DeviceActor(),
                        null,
                        new ActorServiceSettings
                        {
                            ActorGarbageCollectionSettings = actorGarbageCollectionSettings
                        })).GetAwaiter().GetResult();

                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception e)
            {
                TelemetryClient.TrackException(e);
                ActorEventSource.Current.ActorHostInitializationFailed(e);
                throw;
            }
            finally
            {
                TelemetryClient.Flush();
            }
        }
    }
}