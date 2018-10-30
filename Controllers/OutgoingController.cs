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
using Microsoft.AspNetCore.Http.Extensions; 
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

        private static CreateFaxOptions ToOptions(HttpRequest request)
        {
            try
            {
                var m = Regex.Match(string.Join(',', request.Form["to"]), string.Format(@"^{0}([0-9+]+)@.+", Settings.ToPrefix));
                if (!m.Success)
                    throw new Exception("Destination number not found.");
                var to_number = m.Groups[1].Value;

                var files = request.Form.Files
                    .Where(file => file.ContentType == "application/pdf");
                if (!files.Any())
                    throw new Exception("No PDF Attachment.");
                if (files.Count() > 1)
                    throw new Exception("Too many PDF Attachments.");

                var mediaUrl = new Uri(BlobService.UploadFile(files.First()).Result);
                var originalUrl = new Uri(request.GetEncodedUrl());
                return new CreateFaxOptions(to_number, mediaUrl)
                {
                    From = Settings.TwilioNumber,
                    StatusCallback = new Uri(originalUrl.GetLeftPart(UriPartial.Authority) + "/api/outgoing/sent")
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

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            Logger.LogInformation(LoggingEvents.INCOMMING, "Request: {0}", JsonConvert.SerializeObject(Request.Form));
            var msg = SendgridService.CreateMessage();
            try
            {
                // Set reply information.
                var headers = ParseHeaders(Request.Form["headers"].ToString());
                msg.AddCcs(Settings.ParseEmailAddresses(GetReplyTo(headers)).ToList());
                msg.AddHeader("In-Reply-To", headers["Message-Id"]);
                // Send FAX
                var options = ToOptions(Request);
                Logger.LogDebug(LoggingEvents.REQUEST_TO_TWILIO, "Request to Twilio:\n{0}", JsonConvert.SerializeObject(options));
                var result = await TwilioService.SendAsync(options);
                Logger.LogDebug(LoggingEvents.RESULT_FROM_TWILIO, "Result from Twilio:\n{0}", JsonConvert.SerializeObject(result));
                // Store Email address.
                Cache.Set(result.Sid, msg);
            }
            catch (ArgumentException exn)
            {
                Logger.LogError(LoggingEvents.ERROR_ON_OUTGOING, exn, "Bad Request");
                await SendgridService.ReplyError(msg, exn);
            }
            catch (Exception exn)
            {
                Logger.LogError(LoggingEvents.ERROR_ON_TWILIO, exn, "FAX Sending Error");
                await SendgridService.ReplyError(msg, exn);
            }
            return Ok();
        }

        [HttpPost]
        [Route("sent")]
        public async Task<IActionResult> Sent()
        {
            Logger.LogInformation(LoggingEvents.FAX_SENT, "Request: {0}", JsonConvert.SerializeObject(Request.Form));
            var msg = SendgridService.CreateMessage();
            try
            {
                // Send recieved FAX image.
                msg = Cache.GetOrCreate<SendGridMessage>(Request.Form["FaxSid"].ToString(), _ => null) ?? msg;
                var status = Request.Form["Status"].ToString();
                var subject = string.Format("[{0}] Fax sent to {1}", status, Request.Form["To"].ToString());
                var text = Request.Form.Keys
                    .Select(key => string.Format("{0}: {1}", key, Request.Form[key].ToString()))
                    .Aggregate((a, b) => a + "\n" + b) + "\n\n";
                var mediaUrl = (status == "delivered") ? new Uri(Request.Form["MediaUrl"].ToString()) : null;
                SendgridService.SetContent(msg, subject, text, mediaUrl);
                Logger.LogDebug(LoggingEvents.REQUEST_TO_SENDGRID, "Message:\n{0}", JsonConvert.SerializeObject(msg));
                var response = await SendgridService.SendAsync(msg);
                Logger.LogDebug(LoggingEvents.RESULT_FROM_SENDGRID, "Response:\n{0}", JsonConvert.SerializeObject(response));
                // Delete original media file.
                var blobName = Path.GetFileName((new Uri(Request.Form["OriginalMediaUrl"].ToString())).LocalPath);
                await BlobService.DeleteFile(blobName);
                return Ok();
            }
            catch (Exception exn)
            {
                Logger.LogError(LoggingEvents.ERROR_ON_OUTGOING, exn, "FAX Finished Error");
                await SendgridService.ReplyError(msg, exn);
                return StatusCode(500);
            }
        }
    }
}