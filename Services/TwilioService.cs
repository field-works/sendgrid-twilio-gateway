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
        private static string TwilioUsername
        {
            get => Environment.GetEnvironmentVariable("TWILIO_USERNAME") ?? "";
        }

        private static string TwilioPassword
        {
            get => Environment.GetEnvironmentVariable("TWILIO_PASSWORD") ?? "";
        }

        static TwilioService()
        {
            TwilioClient.Init(TwilioUsername, TwilioPassword);
        }

        public static Task<FaxResource> SendAsync(CreateFaxOptions options)
        {
            return FaxResource.CreateAsync(options, TwilioClient.GetRestClient());
        }
    }
}
