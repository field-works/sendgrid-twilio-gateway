using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using SendGrid.Helpers.Mail;

namespace SendgridTwilioGateway.Services
{
    public static class Settings
    {
        public static string ApiKey
        {
            get => Environment.GetEnvironmentVariable("SENDGRID_APIKEY") ?? "";
        }

        public static string TwilioUsername
        {
            get => Environment.GetEnvironmentVariable("TWILIO_USERNAME") ?? "";
        }

        public static string TwilioPassword
        {
            get => Environment.GetEnvironmentVariable("TWILIO_PASSWORD") ?? "";
        }

        public static string ConnectionString
        {
            get => Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING") ?? "";
        }

        public static EmailAddress FaxAgentAddr
        {
            get => ParseEmailAddress(Environment.GetEnvironmentVariable("FROM_ADDR") ?? "fax@example.com");
        }

        public static IEnumerable<EmailAddress> InboxAddr
        {
            get => ParseEmailAddresses(Environment.GetEnvironmentVariable("INBOX_ADDR") ?? "");
        }

        public static string ToPrefix
        {
            get => Environment.GetEnvironmentVariable("TO_PREFIX") ?? "";
        }

        public static Uri CallbackUrl
        {
            get => new Uri(Environment.GetEnvironmentVariable("OUTGOING_CALLBACK") ?? "");
        }

        public static string FromNumber
        {
            get => Environment.GetEnvironmentVariable("FROM_NUMBER") ?? "";
        }

        public static EmailAddress ParseEmailAddress(string addr)
        {
            try
            {
                var m = Regex.Match(addr, @"^(.*)<(.+)>");
                if (m.Success)
                    return new EmailAddress(m.Groups[2].Value, m.Groups[1].Value);
                return new EmailAddress(addr);
            }
            catch (Exception exn)
            {
                throw new ArgumentException("Bad Email address", exn);
            }
        }

        public static IEnumerable<EmailAddress> ParseEmailAddresses(string addrs)
        {
            if (string.IsNullOrEmpty(addrs))
                return Enumerable.Empty<EmailAddress>();
            return addrs.Split(',').Select(addr => ParseEmailAddress(addr));
        }
    }
}