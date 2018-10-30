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
        private const string ContainerSid = "outgoing";

        private readonly ILogger Logger;
        private readonly IMemoryCache Cache;

        public OutgoingController(
            ILogger<OutgoingController> logger,
            IMemoryCache cache)
        {
            this.Logger = logger;
            this.Cache = cache;
        }

        private static async Task<string> UploadFile(IFormFile file)
        {
            var container = await BlobService.OpenContainerAsync(ContainerSid);
            var blob = Guid.NewGuid().ToString() + "_" + file.FileName;
            return await BlobService.UploadFromStreamAsync(container, file.OpenReadStream(), blob, 0.5);
        }

        private static async Task DeleteFile(string blob)
        {
            var container = await BlobService.OpenContainerAsync(ContainerSid);
            await BlobService.DeleteBlob(container, blob);
        }

        private static CreateFaxOptions ToOptions(IFormCollection form)
        {
            try
            {
                var m = Regex.Match(string.Join(',', form["to"]), string.Format(@"^{0}([0-9+]+)@.+", Settings.ToPrefix));
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
                    From = Settings.FromNumber,
                    StatusCallback = Settings.CallbackUrl
                };
            }
            catch (Exception exn)
            {
                throw new ArgumentException("Bad request", exn);
            }
        }

        private static IDictionary<string, string> ParseHeaders(string headers)
        {
            try
            {
                return headers.Split("\n")
                    .Select(line => line.Split(':'))
                    .Where(kv => kv.Length == 2)
                    .GroupBy(kv => kv[0].Trim())
                    .ToDictionary(g => g.Key, g => string.Join("\n", g.Select(kv => kv[1].Trim())), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exn)
            {
                throw new ArgumentException("Bad Headers", exn);
            }
        }

        private static string GetReplyTo(IDictionary<string, string> headers)
        {
            if (headers.ContainsKey("Reply-To"))
                return headers["Reply-To"];
            return headers["From"];
        }

        private async Task ReplyError(SendGridMessage msg, Exception exn)
        {
            SendgridService.SetContent(msg, exn);
            Logger.LogDebug(LoggingEvents.REQUEST_TO_SENDGRID, "Message:\n{0}", JsonConvert.SerializeObject(msg));
            var response = await SendgridService.SendAsync(msg);
            Logger.LogDebug(LoggingEvents.RESULT_FROM_SENDGRID, "Response:\n{0}", JsonConvert.SerializeObject(response));
        }

        private SendGridMessage CreateMessage()
        {
            var msg = new SendGridMessage() { From = Settings.FaxAgentAddr };
            msg.AddTos(Settings.InboxAddr.ToList());
            return msg;
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            Logger.LogInformation(LoggingEvents.REQUEST_TO_SENDFAX, "Request: {0}", JsonConvert.SerializeObject(Request.Form));
            var msg = CreateMessage();
            try
            {
                // Set reply information.
                var headers = ParseHeaders(Request.Form["headers"].ToString());
                msg.AddCcs(Settings.ParseEmailAddresses(GetReplyTo(headers)).ToList());
                msg.AddHeader("In-Reply-To", headers["Message-Id"]);
                // Send FAX
                var options = ToOptions(Request.Form);
                Logger.LogDebug(LoggingEvents.REQUEST_TO_TWILIO, "Request to Twilio:\n{0}", JsonConvert.SerializeObject(options));
                var result = await TwilioService.SendAsync(options);
                Logger.LogDebug(LoggingEvents.RESULT_FROM_TWILIO, "Result from Twilio:\n{0}", JsonConvert.SerializeObject(result));
                // Store Email address.
                Cache.Set(result.Sid, msg);
            }
            catch (ArgumentException exn)
            {
                Logger.LogError(LoggingEvents.ERROR_ON_SENDFAX, exn, "Bad Request");
                await ReplyError(msg, exn);
            }
            catch (Exception exn)
            {
                Logger.LogError(LoggingEvents.ERROR_ON_TWILIO, exn, "FAX Sending Error");
                await ReplyError(msg, exn);
            }
            return Ok();
        }

        [HttpPost]
        [Route("finished")]
        public async Task<IActionResult> Finished()
        {
            Logger.LogInformation(LoggingEvents.SENDFAX_FINISHED, "Request: {0}", JsonConvert.SerializeObject(Request.Form));
            var msg = CreateMessage();
            try
            {
                // Send recieved FAX image.
                msg = Cache.GetOrCreate<SendGridMessage>(Request.Form["FaxSid"].ToString(), _ => null) ?? msg;
                var status = Request.Form["Status"].ToString();
                var subject = string.Format("[{0}] Fax sent to {1}", status, Request.Form["To"].ToString());
                var text = Request.Form.Keys
                    .Select(key => string.Format("{0}: {1}", key, Request.Form[key].ToString()))
                    .Aggregate((a, b) => a + "\n" + b) + "\n\n";
                var mediaUrl = (status == "delvered") ? new Uri(Request.Form["MediaUrl"].ToString()) : null;
                SendgridService.SetContent(msg, subject, text, mediaUrl);
                Logger.LogDebug(LoggingEvents.REQUEST_TO_SENDGRID, "Message:\n{0}", JsonConvert.SerializeObject(msg));
                var response = await SendgridService.SendAsync(msg);
                Logger.LogDebug(LoggingEvents.RESULT_FROM_SENDGRID, "Response:\n{0}", JsonConvert.SerializeObject(response));
                // Delete original media file.
                var blobName = Path.GetFileName((new Uri(Request.Form["OriginalMediaUrl"].ToString())).LocalPath);
                await DeleteFile(blobName);
                return Ok();
            }
            catch (Exception exn)
            {
                Logger.LogError(LoggingEvents.ERROR_ON_SENDGRID, exn, "FAX Finished Error");
                await ReplyError(msg, exn);
                return StatusCode(500);
            }
        }
    }
}
