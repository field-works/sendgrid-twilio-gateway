using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using SendGrid;
using SendGrid.Helpers.Mail;
using SendgridTwilioGateway.Models;

namespace SendgridTwilioGateway.Services
{
    public static class SendgridService
    {
        private static string ApiKey
        {
            get => Environment.GetEnvironmentVariable("SENDGRID_APIKEY") ?? "";
        }

       public static void AddAddr(
           SendGridMessage msg,
           EmailAddress from,
           IEnumerable<EmailAddress> tos,
           IEnumerable<EmailAddress> ccs,
           IEnumerable<EmailAddress> bccs)
        {
            msg.SetFrom(from);
            msg.AddTos(tos.ToList());
            foreach (var cc in ccs) msg.AddCc(cc);
            foreach (var bcc in bccs) msg.AddBcc(bcc);
        }

        private static async Task<HttpResponseMessage> GetRemoteFile(string uri)
        {
            var client = new HttpClient();
            return await client.SendAsync(new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(uri)
            });
        }

        private static void AddAttachment(SendGridMessage msg, Uri uri)
        {
            var response = GetRemoteFile(uri.AbsoluteUri).Result;
            if (response.IsSuccessStatusCode)
                msg.AddAttachment(
                    Path.GetFileName(uri.LocalPath),
                    Convert.ToBase64String(response.Content.ReadAsByteArrayAsync().Result),
                    response.Content.Headers.ContentType.MediaType);
        }

        private static async Task<Response> SendAsync(SendGridMessage msg)
        {
            var client = new SendGridClient(ApiKey);
            return await client.SendEmailAsync(msg);
        }

        public static async Task<Response> SendAsync(SendGridMessage msg, string subject, string text, Uri attachment)
        {
            var client = new SendGridClient(ApiKey);
            msg.SetSubject(subject);
            msg.AddContent(MimeType.Text, text);
            AddAttachment(msg, attachment);
            return await client.SendEmailAsync(msg);
        }

        public static async Task<Response> SendAsync(SendGridMessage msg, Exception exn)
        {
            var client = new SendGridClient(ApiKey);
            msg.SetSubject(string.Format("[error] {0}", exn.Message));
            msg.AddContent(MimeType.Text, exn.ToString());
            return await client.SendEmailAsync(msg);
        }
    }
}