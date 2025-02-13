﻿//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

namespace DurableTask.AzureStorage.Partitioning
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.ExceptionServices;
    using System.Threading.Tasks;
    using DurableTask.AzureStorage.Storage;
    using Microsoft.WindowsAzure.Storage;

    class LegacyPartitionManager : IPartitionManager
    {
        readonly AzureStorageOrchestrationService service;
        readonly AzureStorageClient azureStorageClient;
        readonly AzureStorageOrchestrationServiceSettings settings;

        readonly BlobLeaseManager leaseManager;
        readonly LeaseCollectionBalancer<BlobLease> leaseCollectionManager;

        public LegacyPartitionManager(
            AzureStorageOrchestrationService service,
            AzureStorageClient azureStorageClient)
        {
            this.service = service;
            this.azureStorageClient = azureStorageClient;
            this.settings = this.azureStorageClient.Settings;
            this.leaseManager = AzureStorageOrchestrationService.GetBlobLeaseManager(
                this.azureStorageClient,
                "default");

            this.leaseCollectionManager = new LeaseCollectionBalancer<BlobLease>(
                "default",
                settings,
                this.azureStorageClient.StorageAccountName,
                leaseManager,
                new LeaseCollectionBalancerOptions
                {
                    AcquireInterval = settings.LeaseAcquireInterval,
                    RenewInterval = settings.LeaseRenewInterval,
                    LeaseInterval = settings.LeaseInterval,
                    ShouldStealLeases = true,
                });
        }

        Task IPartitionManager.CreateLease(string leaseName)
        {
            return this.leaseManager.CreateLeaseIfNotExistAsync(leaseName);
        }

        Task IPartitionManager.CreateLeaseStore()
        {    
            TaskHubInfo hubInfo = new TaskHubInfo(this.settings.TaskHubName, DateTime.UtcNow, this.settings.PartitionCount);
            return this.leaseManager.CreateLeaseStoreIfNotExistsAsync(hubInfo, checkIfStale: true);
        }

        Task IPartitionManager.DeleteLeases()
        {
            return this.leaseManager.DeleteAllAsync().ContinueWith(t =>
            {
                if (t.Exception?.InnerExceptions?.Count > 0)
                {
                    foreach (Exception e in t.Exception.InnerExceptions)
                    {
                        StorageException storageException = e as StorageException;
                        if (storageException == null || storageException.RequestInformation.HttpStatusCode != 404)
                        {
                            ExceptionDispatchInfo.Capture(e).Throw();
                        }
                    }
                }
            });
        }

        Task<IEnumerable<BlobLease>> IPartitionManager.GetOwnershipBlobLeases()
        {
            return this.leaseManager.ListLeasesAsync();
        }

        async Task IPartitionManager.StartAsync()
        {
            await this.leaseCollectionManager.InitializeAsync();
            await this.leaseCollectionManager.SubscribeAsync(
                this.service.OnOwnershipLeaseAquiredAsync,
                this.service.OnOwnershipLeaseReleasedAsync);
            await this.leaseCollectionManager.StartAsync();
        }

        Task IPartitionManager.StopAsync()
        {
            return this.leaseCollectionManager.StopAsync();
        }
    }
}
