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
using SendgridTwilioGateway.Extensions;

namespace SendgridTwilioGateway.Services
{
    public static class SendgridService
    {
       public static void AddAddr(
           SendGridMessage msg,
           EmailAddress from,
           IEnumerable<EmailAddress> tos,
           IEnumerable<EmailAddress> ccs)
        {
            msg.SetFrom(from);
            msg.AddTos(tos.ToList());
            foreach (var cc in ccs) msg.AddCc(cc);
        }

        public static SendGridMessage CreateMessage(FaxStation fax)
        {
            var msg = new SendGridMessage() { From = fax.FromAddr.AsEmailAddress() };
            msg.AddTos(fax.InboxAddr.AsEmailAddresses());
            return msg;
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

        public static void AddAttachment(this SendGridMessage msg, Uri uri)
        {
            var response = GetRemoteFile(uri.AbsoluteUri).Result;
            if (response.IsSuccessStatusCode)
                msg.AddAttachment(
                    Path.GetFileName(uri.LocalPath),
                    Convert.ToBase64String(response.Content.ReadAsByteArrayAsync().Result),
                    response.Content.Headers.ContentType.MediaType);
        }

        public static void SetContent(this SendGridMessage msg, Exception exn)
        {
            msg.SetSubject(string.Format("[error] {0}", exn.Message));
            msg.AddContent(MimeType.Text, exn.ToString());
        }

        public static async Task<Response> SendAsync(this SendGridMessage msg, Models.SendGrid settings)
        {
            var client = new SendGridClient(settings.ApiKey);
            return await client.SendEmailAsync(msg);
        }
    }
}