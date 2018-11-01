using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SendGrid.Helpers.Mail;

namespace SendgridTwilioGateway.Extensions
{
    public static class EmailAddressExtention
    {
        public static EmailAddress AsEmailAddress(this string addr)
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

        public static List<EmailAddress> AsEmailAddresses(this string addrs)
        {
            if (string.IsNullOrEmpty(addrs))
                return new List<EmailAddress>();
            return addrs.Split(',').Select(addr => AsEmailAddress(addr)).ToList();
        }
    }
}