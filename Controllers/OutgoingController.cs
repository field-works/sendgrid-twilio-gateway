using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SendGrid;
using SendGrid.Helpers.Mail;
using SendgridTwilioGateway.Extensions;
using SendgridTwilioGateway.Models;
using SendgridTwilioGateway.Services;
using Twilio;
using Twilio.Rest.Fax.V1;

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

        private string ToE164(string number)
        {
            if (number.StartsWith('0'))
                return string.Format("+{0}{1}", Settings.FaxStation.CountryCode, number.Substring(1));
            return number;
        }

        private async Task<CreateFaxOptions> ParseRequest(HttpRequest request)
        {
            try
            {
                var matchTo = Regex.Match(Request.Form["to"], Settings.FaxStation.ToPattern);
                if (!matchTo.Success)
                    throw new Exception("Bad request");
                var to_number = ToE164(matchTo.Groups[1].Value);

                var files = request.Form.Files
                    .Where(file => file.ContentType == "application/pdf");
                if (!files.Any())
                    throw new Exception("No PDF attachment.");
                if (files.Count() > 1)
                    throw new Exception("Too many PDF attachments.");

                var matchSubject = Regex.Match(Request.Form["subject"], @"{\s*(\S+)\s*}$");
                var quality = matchSubject.Success ? matchSubject.Groups[1].Value : Settings.FaxStation.Quality;

                var container = await BlobService.OpenContainerAsync(Settings.Azure);
                var mediaUrl = new Uri(await container.UploadFile(files.First()));
                var originalUrl = new Uri(request.GetEncodedUrl());
                return new CreateFaxOptions(to_number, mediaUrl)
                {
                    From = Settings.FaxStation.FromNumber,
                    Quality = quality.ToLower(),
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
            var rawHeaders = Request.Form["headers"].ToString();
            var msg = SendgridService.CreateMessage(Settings.FaxStation);
            try
            {
                // Set reply information.
                msg.SetSubject(Request.Form["subject"]);
                var headers = ParseHeaders(rawHeaders);
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
                msg.SetSubject(string.Format("[error] {0}", exn.Message));
                msg.AddContent(MimeType.Text, rawHeaders);
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