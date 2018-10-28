using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using SendGrid;
using SendGrid.Helpers.Mail;
using Twilio.Rest.Fax.V1;
using SendgridTwilioGateway.Models;
using SendgridTwilioGateway.Services;

namespace SendgridTwilioGateway.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OutgoingController : ControllerBase
    {
        private readonly ILogger Logger;
        private readonly IMemoryCache Cache;

        public OutgoingController(
            ILogger<OutgoingController> logger,
            IMemoryCache cache)
        {
            this.Logger = logger;
            this.Cache = cache;
        }

        private static string FromNumber
        {
            get => Environment.GetEnvironmentVariable("FROM_NUMBER") ?? "";
        }

        private static EmailAddress ParseEmailAddress(string addr)
        {
            var m = Regex.Match(addr, @"^(.*)<(.+)>");
            if (m.Success)
                return new EmailAddress(m.Groups[2].Value, m.Groups[1].Value);
            return new EmailAddress(addr);
        }

        private static IEnumerable<EmailAddress> ParseEmailAddresses(string addrs)
        {
            if (string.IsNullOrEmpty(addrs))
                return Enumerable.Empty<EmailAddress>();
            return addrs.Split(',').Select(addr => ParseEmailAddress(addr));
        }

        private static EmailAddress FaxAgentAddr
        {
            get => ParseEmailAddress(Environment.GetEnvironmentVariable("FAX_AGENT_ADDR") ?? "fax@example.com");
        }

        private static IEnumerable<EmailAddress> InboxAddr
        {
            get => ParseEmailAddresses(Environment.GetEnvironmentVariable("INBOX_ADDR") ?? "");
        }

        private static IEnumerable<EmailAddress> CcAddr
        {
            get => ParseEmailAddresses(Environment.GetEnvironmentVariable("CC_ADDR") ?? "");
        }

        private static IEnumerable<EmailAddress> BccAddr
        {
            get => ParseEmailAddresses(Environment.GetEnvironmentVariable("BCC_ADDR") ?? "");
        }

        private static Uri CallbackUrl
        {
            get => new Uri(Environment.GetEnvironmentVariable("OUTGOING_CALLBACK") ?? "");
        }

        private static async Task<string> UploadFile(IFormFile file)
        {
            var container = await BlobService.OpenContainerAsync();
            var blob = Guid.NewGuid().ToString() + "_" + file.FileName;
            return await BlobService.UploadFromStreamAsync(container, file.OpenReadStream(), blob, 0.5);
        }

        private static async Task DeleteFile(string blob)
        {
            var container = await BlobService.OpenContainerAsync();
            await BlobService.DeleteBlob(container, blob);
        }

        private static CreateFaxOptions ToOptions(IFormCollection form)
        {
            var m = Regex.Match(string.Join(',', form["to"]), @"^([0-9+]+)@.+");
            if (!m.Success)
                throw new Exception("Destination number not found.");
            var to_number = m.Groups[1].Value;

            var files = form.Files;
            if (!files.Any())
                throw new Exception("No Attachment.");
            if (files.Count() > 1)
                throw new Exception("Too many Attachments.");

            var uri = new Uri(UploadFile(files.First()).Result);

            return new CreateFaxOptions(to_number, uri)
            {
                From = FromNumber,
                StatusCallback = CallbackUrl
            };
        }

        private static IDictionary<string, string> ParseHeaders(string headers)
        {
            return headers.Split("\n")
                .Select(line => {
                    var keyValue = line.Split(":");
                    return new { Key = keyValue[0].Trim(), Value = keyValue[1].Trim() };
                })
                .GroupBy(kv => kv.Key)
                .ToDictionary(g => g.Key, g => string.Join("\n", g.Select(kv => kv.Value)), StringComparer.OrdinalIgnoreCase);
        }

        private static string GetReplyTo(IDictionary<string, string> headers)
        {
            if (headers.ContainsKey("Reply-To"))
                return headers["Reply-To"];
            return headers["From"];
        }

        [HttpPost]
        public async void Post()
        {
            var headers = ParseHeaders(Request.Form["headers"].ToString().Trim());
            Logger.LogDebug(LoggingEvents.RECEIVE_MAIL, "Headers: {0}", JsonConvert.SerializeObject(headers));
            var replyAddr = ParseEmailAddresses(GetReplyTo(headers));
            var msg = SendgridService.CreateMessage(FaxAgentAddr, replyAddr, CcAddr, BccAddr);
            msg.AddHeader("In-Reply-To", headers["Message-Id"]);
            try
            {

                var options = ToOptions(Request.Form);
                Logger.LogInformation(LoggingEvents.REQUEST_TO_TWILIO, "Request to Twilio:\n{0}", JsonConvert.SerializeObject(options));
                var result = await TwilioService.SendAsync(options);
                Cache.Set(result.Sid, msg);
                Logger.LogInformation(LoggingEvents.RESULT_FROM_TWILIO, "Result from Twilio:\n{0}", JsonConvert.SerializeObject(result));
            }
            catch (Exception exn)
            {
                Logger.LogError(LoggingEvents.OUTGOING_ERROR, exn, "Outgoing Error");
                var response = await SendgridService.SendAsync(msg, exn);
            }
        }

        [HttpPost]
        [Route("callback")]
        public async void Callback()
        {
            Logger.LogInformation(0, "Fax status:\n{0}", JsonConvert.SerializeObject(Request.Form));
            var blob = Path.GetFileName((new Uri(Request.Form["OriginalMediaUrl"].ToString())).LocalPath);
            var sid = Request.Form["FaxSid"].ToString();
            var msg = Cache.GetOrCreate<SendGridMessage>(sid, _ => SendgridService.CreateMessage(FaxAgentAddr, InboxAddr, CcAddr, BccAddr));
            var subject = string.Format("[{0}] Fax sent to {1}", Request.Form["Status"].ToString(), Request.Form["To"].ToString());
            var text = Request.Form.Keys
                .Select(key => string.Format("{0}: {1}", key, Request.Form[key].ToString()))
                .Aggregate((a, b) => a + "\n" + b) + "\n\n";
            var mediaUrl = new Uri(Request.Form["MediaUrl"].ToString());
            var response = await SendgridService.SendAsync(msg, subject, text, mediaUrl);
            await DeleteFile(blob);
        }
    }
}
