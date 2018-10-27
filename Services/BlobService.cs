using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Linq;

namespace SendgridTwilioGateway.Services
{
    public static class BlobService
    {
        private static string ConnectionString
        {
            get => Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING") ?? "";
        }

        private static string ContainerSid
        {
            get => Environment.GetEnvironmentVariable("CONTAINER_SID") ?? "";
        }

        public static async Task<CloudBlobContainer> OpenContainerAsync()
        {
            var storageAccount = CloudStorageAccount.Parse(ConnectionString);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(ContainerSid);
            await container.CreateIfNotExistsAsync();
            return container;
        }

        public static async Task<string> UploadFromStreamAsync(CloudBlobContainer container, Stream stream, string fileName, double sharedAccessExpiryHours)
        {
            var blob = container.GetBlockBlobReference(fileName);
            var now = DateTime.Now;
            var sharedPolicy = new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPermissions.Read,
                SharedAccessStartTime = now,
                SharedAccessExpiryTime = now.AddHours(sharedAccessExpiryHours)
            };
            await blob.UploadFromStreamAsync(stream);
            var sas = blob.GetSharedAccessSignature(sharedPolicy);
            return blob.Uri.AbsoluteUri + sas;
        }

        public static async Task DeleteBlob(CloudBlobContainer container, string fileName)
        {
            var blob = container.GetBlockBlobReference(fileName);
            await blob.DeleteAsync();
        }

        public static async Task DeleteContainer(CloudBlobContainer container)
        {
            await container.FetchAttributesAsync();
            await container.DeleteAsync();
        }
    }
}