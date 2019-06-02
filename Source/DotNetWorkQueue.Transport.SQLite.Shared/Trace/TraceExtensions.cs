﻿using DotNetWorkQueue.Transport.RelationalDatabase.Basic.Command;
using OpenTracing;

namespace DotNetWorkQueue.Transport.SQLite.Shared.Trace
{
    /// <summary>
    /// Adds tags to spans
    /// </summary>
    public static class TraceExtensions
    {
        /// <summary>
        /// Adds tags from the command to the span
        /// </summary>
        /// <param name="span">The span.</param>
        /// <param name="command">The command.</param>
        public static void Add(this ISpan span, SendMessageCommand command)
        {
            var delay = command.MessageData.GetDelay();
            if (delay.HasValue)
                span.SetTag("MessageDelay",
                    delay.Value.ToString());

            var expiration = command.MessageData.GetExpiration();
            if (expiration.HasValue)
                span.SetTag("MessageExpiration",
                    expiration.Value.ToString());

            var priority = command.MessageData.GetPriority();
            if (priority.HasValue)
                span.SetTag("MessagePriority",
                    priority.Value.ToString());
        }
    }
}