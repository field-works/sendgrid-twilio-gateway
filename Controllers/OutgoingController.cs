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
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;
using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Fax.V1;
using Twilio.Rest.Fax.V1.Fax;
using SendgridTwilioGateway.Models;
using SendgridTwilioGateway.Services;
using SendgridTwilioGateway.Extensions;

namespace SendgridTwilioGateway.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OutgoingController : ControllerBase
    {
        private readonly Settings Settings;
        private readonly ILogger Logger;
        private readonly IMemoryCache Cache;

        public OutgoingController(
            IOptions<Settings> settings,
            ILogger<OutgoingController> logger,
            IMemoryCache cache)
        {
            this.Settings = settings.Value;
            this.Logger = logger;
            this.Cache = cache;
        }

        private async Task<CreateFaxOptions> ParseRequest(HttpRequest request)
        {
            try
            {
                var m = Regex.Match(Request.Form["to"], Settings.FaxStation.ToPattern);
                if (!m.Success)
                    throw new Exception("Bad request");
                var to_number = m.Groups[1].Value;

                var files = request.Form.Files
                    .Where(file => file.ContentType == "application/pdf");
                if (!files.Any())
                    throw new Exception("No PDF attachment.");
                if (files.Count() > 1)
                    throw new Exception("Too many PDF attachments.");

                var container = await BlobService.OpenContainerAsync(Settings.Azure);
                var mediaUrl = new Uri(await container.UploadFile(files.First()));
                var originalUrl = new Uri(request.GetEncodedUrl());
                return new CreateFaxOptions(to_number, mediaUrl)
                {
                    From = Settings.FaxStation.FromNumber,
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
            var msg = SendgridService.CreateMessage(Settings.FaxStation);
            try
            {
                // Set reply information.
                msg.SetSubject(Request.Form["subject"]);
                var headers = ParseHeaders(Request.Form["headers"]);
                msg.AddCcs(GetReplyTo(headers).AsEmailAddresses());
                msg.AddHeader("In-Reply-To", headers["Message-Id"]);
                // Send FAX
                TwilioClient.Init(Settings.Twilio.UserName, Settings.Twilio.Password);
                var options = await ParseRequest(Request);
                Logger.LogDebug(LoggingEvents.REQUEST_TO_TWILIO, "Request to Twilio:\n{0}", JsonConvert.SerializeObject(options));
                var result = await FaxResource.CreateAsync(options, TwilioClient.GetRestClient());
                Logger.LogDebug(LoggingEvents.RESULT_FROM_TWILIO, "Result from Twilio:\n{0}", JsonConvert.SerializeObject(result));
                // Store reply address.
                Cache.Set(result.Sid, msg);
            }
            catch (ArgumentException exn)
            {
                Logger.LogError(LoggingEvents.ERROR_ON_OUTGOING, exn, "Bad Request");
                msg.SetContent(exn);
                await msg.SendAsync(Settings.SendGrid);
            }
            catch (Exception exn)
            {
                Logger.LogError(LoggingEvents.ERROR_ON_TWILIO, exn, "FAX Sending Error");
                msg.SetContent(exn);
                await msg.SendAsync(Settings.SendGrid);
            }
            return Ok();
        }

        [HttpPost]
        [Route("sent")]
        public async Task<IActionResult> Sent()
        {
            Logger.LogInformation(LoggingEvents.FAX_SENT, "Request: {0}", JsonConvert.SerializeObject(Request.Form));
            var msg = SendgridService.CreateMessage(Settings.FaxStation);
            try
            {
                // Send received image to inbox.
                msg = Cache.GetOrCreate<SendGridMessage>(Request.Form["FaxSid"].ToString(), _ => null) ?? msg;
                var status = Request.Form["Status"].ToString();
                msg.SetSubject(string.Format("[{0}] {1}", status, msg.Personalizations[0].Subject));
                var text = Request.Form.Keys
                    .Select(key => string.Format("{0}: {1}", key, Request.Form[key].ToString()))
                    .Aggregate((a, b) => a + "\n" + b) + "\n\n";
                msg.AddContent(MimeType.Text, text + "\n\n");
                if (status == "delivered")
                    msg.AddAttachment(new Uri(Request.Form["MediaUrl"]));
                Logger.LogDebug(LoggingEvents.REQUEST_TO_SENDGRID, "Message:\n{0}", JsonConvert.SerializeObject(msg));
                var response = await msg.SendAsync(Settings.SendGrid);
                Logger.LogDebug(LoggingEvents.RESULT_FROM_SENDGRID, "Response:\n{0}", JsonConvert.SerializeObject(response));
                // Delete original media file.
                var container = await BlobService.OpenContainerAsync(Settings.Azure);
                await container.DeleteFile(Path.GetFileName((new Uri(Request.Form["OriginalMediaUrl"])).LocalPath));
                return Ok();
            }
            catch (Exception exn)
            {
                Logger.LogError(LoggingEvents.ERROR_ON_OUTGOING, exn, "FAX Finished Error");
                msg.SetContent(exn);
                await msg.SendAsync(Settings.SendGrid);
                return StatusCode(500);
            }
        }
    }
}