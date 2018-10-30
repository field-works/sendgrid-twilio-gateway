using Microsoft.Extensions.Logging;

namespace SendgridTwilioGateway.Models
{
    public static class LoggingEvents
    {
        public static readonly EventId INCOMMING = 10;
        public static readonly EventId FAX_SENT = 11;
        public static readonly EventId ERROR_ON_OUTGOING = 19;
        public static readonly EventId OUTGOING = 20;
        public static readonly EventId FAX_RECIEVED = 21;
        public static readonly EventId ERROR_ON_INCOMMING = 29;
        public static readonly EventId REQUEST_TO_TWILIO = 100;
        public static readonly EventId RESULT_FROM_TWILIO = 101;
        public static readonly EventId ERROR_ON_TWILIO = 109;

        public static readonly EventId REQUEST_TO_SENDGRID = 200;
        public static readonly EventId RESULT_FROM_SENDGRID = 201;
    }
} 