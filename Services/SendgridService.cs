using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using SendGrid;
using SendGrid.Helpers.Mail;
using SendgridTwilioGateway.Extensions;
using SendgridTwilioGateway.Models;

namespace SendgridTwilioGateway.Services
{
    public static class SendgridService
    {
        private static async Task<HttpResponseMessage> GetRemoteFile(string uri)
        {
            var client = new HttpClient();
            return await client.SendAsync(new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(uri)
            });
        }

        public static void AddAttachment(this SendGridMessage msg, Uri uri)
        {
            var response = GetRemoteFile(uri.AbsoluteUri).Result;
            if (response.IsSuccessStatusCode)
                msg.AddAttachment(
                    Path.GetFileName(uri.LocalPath),
                    Convert.ToBase64String(response.Content.ReadAsByteArrayAsync().Result),
                    response.Content.Headers.ContentType.MediaType);
        }

        public static async Task<Response> SendAsync(this SendGridMessage msg, Models.SendGrid settings)
        {
            var client = new SendGridClient(settings.ApiKey);
            return await client.SendEmailAsync(msg);
        }
    }
}