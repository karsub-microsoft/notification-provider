// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.Contracts
{
    using System;
    using System.Collections.Generic;

    public class MeetingRecurrenceDto
    {
        public MeetingRecurrencePatternDto Pattern { get; set; }

        public MeetingRecurrenceRangeDto Range { get; set; }
    }

    // Reference: https://docs.microsoft.com/en-us/graph/api/resources/recurrencepattern?view=graph-rest-1.0
    public class MeetingRecurrencePatternDto
    {
        /// <summary>
        /// Recurrence Pattern
        /// </summary>
        /// <remarks>
        /// Allowed Values: daily, weekly, absoluteMonthly, relativeMonthly, absoluteYearly, relativeYearly
        /// </remarks>
        public string PatternType { get; set; }

        /// <summary>
        /// The number of units between occurrences. The unit depends on the RecurrentType
        /// </summary>
        public int MeetingInterval { get; set; }

        /// <summary>
        /// The day of month in which the meeting is set (absoluteWeekly/absoluteYearly)
        /// </summary>
        public int DayOfMonth { get; set; }

        /// <summary>
        /// List of days of the week on which the meeting is set (weekly/relativeMonthly/relativeWeekly)
        /// </summary>
        public List<string> DaysOfWeek { get; set; }

        /// <summary>
        /// Month in which the meeting in set
        /// </summary>
        public string Month { get; set; }

        /// <summary>
        /// First day of the week (weekly)
        /// </summary>
        public string FirstDayOfWeek { get; set; }

        /// <summary>
        /// Specifies on which instance of the allowed days specified in <see cref="DaysOfWeek"/> the meeting is set (relativeMonthly/relativeYearly)
        /// </summary>
        public string WeekIndex { get; set; }

        public string PatternDescription { get; set; }
    }

    // https://docs.microsoft.com/en-us/graph/api/resources/recurrencerange?view=graph-rest-1.0
    public class MeetingRecurrenceRangeDto
    {
        /// <summary>
        /// The recurrence range
        /// </summary>
        /// <remarks>
        /// Allowed Values: endDate, noEnd, numbered
        /// </remarks>
        public string RangeType { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public int NumberOfOcurrences { get; set; }

        public string RangeDescription { get; set; }
    }
}
