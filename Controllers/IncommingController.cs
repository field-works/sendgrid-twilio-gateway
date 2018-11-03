using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SendGrid;
using SendGrid.Helpers.Mail;
using SendgridTwilioGateway.Models;
using SendgridTwilioGateway.Services;
using SendgridTwilioGateway.Extensions;

namespace SendgridTwilioGateway.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class IncommingController : ControllerBase
    {
        private readonly Settings Settings;
        private readonly ILogger Logger;

        public IncommingController(
            IOptions<Settings> settings,
            ILogger<IncommingController> logger)
        {
            this.Settings = settings.Value;
            this.Logger = logger;
        }

        [HttpPost]
        [Produces("application/xml")]
        public IActionResult Post()
        {
            Logger.LogInformation("Incomming Request:\n{0}", JsonConvert.SerializeObject(Request.Form));
            return Ok(new TwilioResponse
            {
                Receive = new Receive
                {
                    Action = "/api/incomming/received"
                }
            });
        }

        private SendGridMessage CreateIncommingMessage()
        {
            var msg = new SendGridMessage();
            msg.SetFrom(Settings.FaxStation.AgentAddr.AsEmailAddress());
            msg.AddTos(Settings.FaxStation.InboxAddr.AsEmailAddresses());
            return msg;
        }

        private SendGridMessage CreateErrorMessage(Exception exn)
        {
            var msg = CreateIncommingMessage();
            msg.SetSubject("[error] FAX receiving failed");
            msg.AddContent(MimeType.Text, string.Format("{0}\n\n----- Recived request -----\n\n{1}",
                    exn.ToString(), JsonConvert.SerializeObject(Request.Form)));
            foreach (var file in Request.Form.Files)
                msg.AddAttachmentAsync(file.FileName, file.OpenReadStream());
            return msg;
        }

        [HttpPost]
        [Route("received")]
        public async Task<IActionResult> Received()
        {
            Logger.LogInformation("Received Request:\n{0}", JsonConvert.SerializeObject(Request.Form));
            var msg = CreateIncommingMessage();
            try
            {
                // Send received image to inbox.
                var from = Request.Form["From"].Any() ? Request.Form["From"].ToString() : "anonymous";
                msg.SetFrom(string.Format("{0}@{1}", from, Settings.FaxStation.MyHostname));
                var status = Request.Form["Status"].ToString();
                msg.SetSubject(string.Format("[{0}] Received FAX from {1}", status, from));
                var content = Request.Form.Keys
                    .Where(key => !key.EndsWith("MediaUrl"))
                    .OrderBy(key => key)
                    .Select(key => string.Format("{0}: {1}", key, Request.Form[key].ToString()))
                    .Aggregate((a, b) => a + "\n" + b);
                msg.AddContent(MimeType.Text, content + "\n\n");
                if (status == "received")
                    msg.AddAttachment(new Uri(Request.Form["MediaUrl"].ToString()));
                Logger.LogDebug("Message:\n{0}", JsonConvert.SerializeObject(msg));
                var response = await msg.SendAsync(Settings.SendGrid);
                Logger.LogDebug("Response:\n{0}", JsonConvert.SerializeObject(response));
            }
            catch (Exception exn)
            {
                Logger.LogError(exn, "Internal error");
                return StatusCode(500);
            }
            Response.ContentType = "application/xml";
            return Ok();
        }
    }
}
