using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SendGrid;
using SendgridTwilioGateway.Models;
using SendgridTwilioGateway.Services;

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
            ILogger<OutgoingController> logger)
        {
            this.Settings = settings.Value;
            this.Logger = logger;
        }

        [HttpPost]
        [Produces("application/xml")]
        public IActionResult Post()
        {
            Logger.LogInformation(LoggingEvents.INCOMMING, "Request: {0}", JsonConvert.SerializeObject(Request.Form));
            return Ok(new TwilioResponse
            {
                Receive = new Receive
                {
                    Action = "/api/incomming/received"
                }
            });
        }

        [HttpPost]
        [Route("received")]
        public async Task<IActionResult> Received()
        {
            Logger.LogInformation(LoggingEvents.FAX_RECIEVED, "Request: {0}", JsonConvert.SerializeObject(Request.Form));
            var msg = SendgridService.CreateMessage(Settings.FaxStation);
            try
            {
                // Send received image to inbox.
                var status = Request.Form["Status"].ToString();
                msg.SetSubject(string.Format("[{0}] Fax received from {1}", status, Request.Form["From"].ToString()));
                var text = Request.Form.Keys
                    .Select(key => string.Format("{0}: {1}", key, Request.Form[key].ToString()))
                    .Aggregate((a, b) => a + "\n" + b);
                msg.AddContent(MimeType.Text, text + "\n\n");
                if (status == "received")
                    msg.AddAttachment(new Uri(Request.Form["MediaUrl"].ToString()));
                Logger.LogDebug(LoggingEvents.REQUEST_TO_TWILIO, "Message:\n{0}", JsonConvert.SerializeObject(msg));
                var response = await msg.SendAsync(Settings.SendGrid);
                Logger.LogDebug(LoggingEvents.RESULT_FROM_TWILIO, "Response:\n{0}", JsonConvert.SerializeObject(response));
                return Ok();
            }
            catch (Exception exn)
            {
                Logger.LogError(LoggingEvents.ERROR_ON_INCOMMING, exn, "FAX received Error");
                msg.SetContent(exn);
                var response = await msg.SendAsync(Settings.SendGrid);
                return StatusCode(500);
            }
        }
    }
}
