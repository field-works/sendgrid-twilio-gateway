using Microsoft.Extensions.Logging;

namespace SendgridTwilioGateway.Models
{
    public static class LoggingEvents
    {
        public static readonly EventId RECEIVE_MAIL = 1;
        public static readonly EventId SEND_MAIL = 2;
        public static readonly EventId OUTGOING_ERROR = 9;
        public static readonly EventId REQUEST_TO_TWILIO = 100;
        public static readonly EventId RESULT_FROM_TWILIO = 101;

    }
} 