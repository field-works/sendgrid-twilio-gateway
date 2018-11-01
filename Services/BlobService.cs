using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using SendgridTwilioGateway.Models;

namespace SendgridTwilioGateway.Services
{
    public static class BlobService
    {
        public static async Task<CloudBlobContainer> OpenContainerAsync(Azure settings)
        {
            var storageAccount = CloudStorageAccount.Parse(settings.StorageConnectionString);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(settings.ContainerSid);
            await container.CreateIfNotExistsAsync();
            return container;
        }

        private static async Task<string> UploadFromStreamAsync(this CloudBlobContainer container, Stream stream, string fileName, double sharedAccessExpiryHours)
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

        public static async Task<string> UploadFile(this CloudBlobContainer container, IFormFile file)
        {
            var fileName = Guid.NewGuid().ToString() + "_" + file.FileName;
            return await container.UploadFromStreamAsync(file.OpenReadStream(), fileName, 0.5);
        }

        public static async Task DeleteFile(this CloudBlobContainer container, string fileName)
        {
            var blob = container.GetBlockBlobReference(fileName);
            await blob.DeleteAsync();
        }
    }
}