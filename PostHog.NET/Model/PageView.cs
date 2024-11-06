using System;

namespace PostHog.Model
{
    internal class PageView : BaseAction
    {
        public PageView(string? distinctId, Properties? properties = null, DateTime? timestamp = null)
            : base("$pageview", distinctId, properties, timestamp)
        {
        }
    }
}
