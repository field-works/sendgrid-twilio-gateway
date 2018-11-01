namespace SendgridTwilioGateway.Models
{
    public class Settings
    {
        public Twilio Twilio { get; set; }
        public SendGrid SendGrid { get; set; }
        public Azure Azure { get; set; }
        public FaxStation FaxStation { get; set; }
    }

    public class Twilio
    {
        public string UserName { get; set; }
        public string Password { get; set; }
    }

    public class SendGrid
    {
        public string ApiKey { get; set; }
    }

    public class Azure
    {
        public string StorageConnectionString { get; set; }
        public string ContainerSid { get; set; }
    }

    public class FaxStation
    {
        public string CountryCode { get; set; }
        public string FromNumber { get; set; }
        public string ToPattern { get; set; }
        public string FromAddr { get; set; }
        public string InboxAddr { get; set; }
    }
}