using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
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
        public static async Task<CloudBlobContainer> OpenContainerAsync(string containerSid)
        {
            var storageAccount = CloudStorageAccount.Parse(Settings.StorageConnectionString);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(containerSid);
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

        private const string ContainerSid = "outgoing";

        public static async Task DeleteContainer(CloudBlobContainer container)
        {
            await container.FetchAttributesAsync();
            await container.DeleteAsync();
        }

        public static async Task<string> UploadFile(IFormFile file)
        {
            var container = await BlobService.OpenContainerAsync(ContainerSid);
            var blob = Guid.NewGuid().ToString() + "_" + file.FileName;
            return await BlobService.UploadFromStreamAsync(container, file.OpenReadStream(), blob, 0.5);
        }

        public static async Task DeleteFile(string blob)
        {
            var container = await BlobService.OpenContainerAsync(ContainerSid);
            await BlobService.DeleteBlob(container, blob);
        }
    }
}