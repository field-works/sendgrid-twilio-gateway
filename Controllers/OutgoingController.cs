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
            var matchTo = Regex.Match(Request.Form["to"], @"^(\+?[0-9]+)@.+");
            if (!matchTo.Success)
                throw new ArgumentException("Bad number format");
            var to_number = ToE164(matchTo.Groups[1].Value);

            var files = request.Form.Files
                .Where(file => file.ContentType == "application/pdf");
            if (!files.Any())
                throw new ArgumentException("No PDF attachment.");
            if (files.Count() > 1)
                throw new ArgumentException("Too many PDF attachments.");

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

        private static IDictionary<string, string> ParseHeaders(string headers)
        {
            return headers.Split("\n")
                .Select(line => line.Split(':'))
                .Where(kv => kv.Length == 2)
                .GroupBy(kv => kv[0].Trim())
                .ToDictionary(g => g.Key, g => string.Join("\n", g.Select(kv => kv[1].Trim())), StringComparer.OrdinalIgnoreCase);
        }

        private string GetReplyTo(IDictionary<string, string> headers)
        {
            if (headers.ContainsKey("Reply-To"))
                return headers["Reply-To"];
            return headers["From"];
        }

        private SendGridMessage CreateOutgoingMessage()
        {
            var msg = new SendGridMessage();
            msg.SetFrom(Settings.FaxStation.AgentAddr.AsEmailAddress());
            var headers = ParseHeaders(Request.Form["headers"]);
            msg.AddTos(GetReplyTo(headers).AsEmailAddresses());
            msg.AddHeader("In-Reply-To", headers["Message-Id"]);
            return msg;
        }

        private static void SetErrorMessage(SendGridMessage msg, IFormCollection form, string subject, Exception exn)
        {
            msg.SetSubject(string.Format("[error] {0}", subject));
            msg.AddContent(MimeType.Text, string.Format("{0}\n\n----- Original message -----\n\n{1}\n\n{2}",
                    exn.ToString(), form["headers"], form["text"]));
            foreach (var file in form.Files)
                msg.AddAttachmentAsync(file.FileName, file.OpenReadStream());
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            Logger.LogInformation("Outgoing Request:\n{0}", JsonConvert.SerializeObject(Request.Form));
            var msg = CreateOutgoingMessage();
            try
            {
                // TODO: check from domain
                dynamic envelope = JsonConvert.DeserializeObject(Request.Form["envelope"]);
                // Send FAX
                TwilioClient.Init(Settings.Twilio.UserName, Settings.Twilio.Password);
                var options = await ParseRequest(Request);
                Logger.LogDebug("Request to Twilio:\n{0}", JsonConvert.SerializeObject(options));
                var result = await FaxResource.CreateAsync(options, TwilioClient.GetRestClient());
                Logger.LogDebug("Result from Twilio:\n{0}", JsonConvert.SerializeObject(result));
                // Store reply message
                msg.SetSubject(Request.Form["Subject"]);
                Cache.Set(result.Sid, msg);
            }
            catch (ArgumentException exn)
            {
                Logger.LogError(exn, "Bad request");
                SetErrorMessage(msg, Request.Form, "Bad request", exn);
                await msg.SendAsync(Settings.SendGrid);
            }
            catch (Twilio.Exceptions.TwilioException exn)
            {
                Logger.LogError(exn, "FAX delivery failed");
                SetErrorMessage(msg, Request.Form, "FAX delivery failed", exn);
                await msg.SendAsync(Settings.SendGrid);
            }
            catch (Exception exn)
            {
                Logger.LogError(exn, "Internal error");
                return StatusCode(500);
            }
            return Ok();
        }

        [HttpPost]
        [Route("sent")]
        public async Task<IActionResult> Sent()
        {
            Logger.LogInformation("Sent Request:\n{0}", JsonConvert.SerializeObject(Request.Form));
            try
            {
                // reply received FAX image.
                var msg = Cache.Get<SendGridMessage>(Request.Form["FaxSid"].ToString());
                var status = Request.Form["Status"].ToString();
                if (status == "delivered")
                {
                    msg.SetSubject(string.Format("[{0}] {1}", status, msg.Personalizations[0].Subject));
                    msg.AddCcs(Settings.FaxStation.InboxAddr.AsEmailAddresses());
                    msg.AddAttachment(new Uri(Request.Form["MediaUrl"]));
                }
                else
                {
                    msg.SetSubject(string.Format("[{0}] {1}", status, Request.Form["ErrorMessage"]));
                    msg.AddAttachment(new Uri(Request.Form["OriginalMediaUrl"]));
                }
                var content = Request.Form.Keys
                    .Where(key => !key.EndsWith("MediaUrl"))
                    .OrderBy(key => key)
                    .Select(key => string.Format("{0}: {1}", key, Request.Form[key].ToString()))
                    .Aggregate((a, b) => a + "\n" + b);
                msg.AddContent(MimeType.Text, content + "\n\n");
                Logger.LogDebug("Message:\n{0}", JsonConvert.SerializeObject(msg));
                var response = await msg.SendAsync(Settings.SendGrid);
                Logger.LogDebug("Response:\n{0}", JsonConvert.SerializeObject(response));
                // Delete original media file.
                var container = await BlobService.OpenContainerAsync(Settings.Azure);
                await container.DeleteFile(Path.GetFileName((new Uri(Request.Form["OriginalMediaUrl"])).LocalPath));
            }
            catch (Exception exn)
            {
                Logger.LogError(exn, "FAX FinisheInternal error");
                return StatusCode(500);
            }
            return Ok();
        }
    }
}