using Microsoft.Extensions.Logging;

namespace SendgridTwilioGateway.Models
{
    public static class LoggingEvents
    {
        public static readonly EventId REQUEST_TO_SENDFAX = 10;
        public static readonly EventId SENDFAX_FINISHED = 11;
        public static readonly EventId ERROR_ON_SENDFAX = 19;
        public static readonly EventId REQUEST_TO_TWILIO = 100;
        public static readonly EventId RESULT_FROM_TWILIO = 101;
        public static readonly EventId ERROR_ON_TWILIO = 109;

        public static readonly EventId REQUEST_TO_SENDGRID = 200;
        public static readonly EventId RESULT_FROM_SENDGRID = 201;
        public static readonly EventId ERROR_ON_SENDGRID = 209;
    }
} 