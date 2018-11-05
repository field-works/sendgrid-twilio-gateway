using System.Collections.Generic;
using Newtonsoft.Json;

namespace SendgridTwilioGateway.Models
{
    public class Envelope
    {
        public string From { get; set; }

        public IEnumerable<string> To { get; set; }
    }
}
