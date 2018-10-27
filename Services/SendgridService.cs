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

        private static async Task<HttpResponseMessage> GetRemoteFile(string uri)
        {
            var client = new HttpClient();
            return await client.SendAsync(new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(uri)
            });
        }

        private static byte[] GetStreamBytes(IFormFile file)
        {
            using (var ms = new MemoryStream())
            {
                file.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static void AddAttachment(SendGridMessage msg, IEnumerable<Uri> uris)
        {
            foreach(var uri in uris)
            {
                var response = GetRemoteFile(uri.AbsoluteUri).Result;
                if (response.IsSuccessStatusCode)
                    msg.AddAttachment(
                        Path.GetFileName(uri.LocalPath),
                        Convert.ToBase64String(response.Content.ReadAsByteArrayAsync().Result),
                        response.Content.Headers.ContentType.MediaType);
            }
        }

       private static SendGridMessage CreateMessage(
           EmailAddress from,
           IEnumerable<EmailAddress> tos,
           IEnumerable<EmailAddress> ccs,
           IEnumerable<EmailAddress> bccs,
           string subject,
           string text)
        {
            var msg = new SendGridMessage();
            msg.SetFrom(from);
            msg.AddTos(tos.ToList());
            foreach (var cc in ccs) msg.AddCc(cc);
            foreach (var bcc in bccs) msg.AddBcc(bcc);
            msg.SetSubject(subject);
            msg.AddContent(MimeType.Text, text);
            return msg;
        }

        public static async Task<Response> SendAsync(
           EmailAddress from, IEnumerable<EmailAddress> tos,
           IEnumerable<EmailAddress> ccs, IEnumerable<EmailAddress> bccs,
           string subject, string body,
           IEnumerable<Uri> attachments)
        {
            var msg = CreateMessage(from, tos, ccs, bccs, subject, body);
            AddAttachment(msg, attachments);
            var client = new SendGridClient(ApiKey);
            return await client.SendEmailAsync(msg);
        }

        public static async Task<Response> SendErrorAsync(
           EmailAddress from, IEnumerable<EmailAddress> tos,
           IEnumerable<EmailAddress> ccs, IEnumerable<EmailAddress> bccs,
           Exception exn)
        {
            var subject = string.Format("[error] {0}", exn.Message);
            var msg = CreateMessage(from, tos, ccs, bccs, subject, exn.ToString());
            var client = new SendGridClient(ApiKey);
            return await client.SendEmailAsync(msg);
        }
    }
}