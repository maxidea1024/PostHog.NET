using System;

namespace PostHog.Model
{
    internal class Capture : BaseAction
    {
        public Capture(string eventName, string? distinctId, Properties? properties = null, DateTime? timestamp = null)
            : base(eventName, distinctId, properties, timestamp)
        {
        }
    }
}
