using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Xml;
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
    public class Receive
    {
        [System.Xml.Serialization.XmlAttribute("action")]
        public string Action { get; set; }
    }
    public class Response
    {
        public Receive Receive { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class IncommingController : ControllerBase
    {
        private readonly ILogger Logger;
        public IncommingController(ILogger<OutgoingController> logger)
        {
            this.Logger = logger;
        }

        [HttpPost]
        [Produces("application/xml")]
        public IActionResult Post()
        {
            Logger.LogInformation(LoggingEvents.INCOMMING, "Request: {0}", JsonConvert.SerializeObject(Request.Form));
            return Ok(new Response
            {
                Receive = new Receive
                {
                    Action = Request.Host + "/api/incomming/received"
                }
            });
        }

        [HttpPost]
        [Route("received")]
        public async Task<IActionResult> Received()
        {
            Logger.LogInformation(LoggingEvents.FAX_RECIEVED, "Request: {0}", JsonConvert.SerializeObject(Request.Form));
            var msg = SendgridService.CreateMessage();
            try
            {
                var status = Request.Form["Status"].ToString();
                var subject = string.Format("[{0}] Fax received from {1}", status, Request.Form["From"].ToString());
                var text = Request.Form.Keys
                    .Select(key => string.Format("{0}: {1}", key, Request.Form[key].ToString()))
                    .Aggregate((a, b) => a + "\n" + b) + "\n\n";
                var mediaUrl = (status == "received") ? new Uri(Request.Form["MediaUrl"].ToString()) : null;
                SendgridService.SetContent(msg, subject, text, mediaUrl);
                Logger.LogDebug(LoggingEvents.REQUEST_TO_TWILIO, "Message:\n{0}", JsonConvert.SerializeObject(msg));
                var response = await SendgridService.SendAsync(msg);
                Logger.LogDebug(LoggingEvents.RESULT_FROM_TWILIO, "Response:\n{0}", JsonConvert.SerializeObject(response));
                return Ok();
            }
            catch (Exception exn)
            {
                Logger.LogError(LoggingEvents.ERROR_ON_INCOMMING, exn, "FAX received Error");
                var response = await SendgridService.ReplyError(msg, exn);
                return StatusCode(500);
            }
        }
    }
}
