using System.Xml;

namespace SendgridTwilioGateway.Models
{
    [System.Xml.Serialization.XmlRoot("Response")]
    public class TwilioResponse
    {
        public Receive Receive { get; set; }
    }

    public class Receive
    {
        [System.Xml.Serialization.XmlAttribute("action")]
        public string Action { get; set; }
    }
}
