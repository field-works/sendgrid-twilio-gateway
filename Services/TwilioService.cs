using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Fax.V1;
using Twilio.Rest.Fax.V1.Fax;

namespace SendgridTwilioGateway.Services
{
    public static class TwilioService
    {
        static TwilioService()
        {
            TwilioClient.Init(Settings.TwilioUsername, Settings.TwilioPassword);
        }

        public static Task<FaxResource> SendAsync(CreateFaxOptions options)
        {
            return FaxResource.CreateAsync(options, TwilioClient.GetRestClient());
        }
    }
}
