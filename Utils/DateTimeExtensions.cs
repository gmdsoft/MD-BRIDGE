using System;

namespace MD.BRIDGE.Utils
{
    public static class DateTimeOffsetExtensions
    {
        public static bool IsBetween(this DateTimeOffset date, DateTimeOffset start, DateTimeOffset end)
        {
            return start < date && date <= end;
        }
    }
}
