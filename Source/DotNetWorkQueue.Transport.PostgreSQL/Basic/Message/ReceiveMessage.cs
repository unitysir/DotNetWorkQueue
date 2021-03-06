﻿// ---------------------------------------------------------------------
//This file is part of DotNetWorkQueue
//Copyright © 2015-2020 Brian Lehnen
//
//This library is free software; you can redistribute it and/or
//modify it under the terms of the GNU Lesser General Public
//License as published by the Free Software Foundation; either
//version 2.1 of the License, or (at your option) any later version.
//
//This library is distributed in the hope that it will be useful,
//but WITHOUT ANY WARRANTY; without even the implied warranty of
//MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
//Lesser General Public License for more details.
//
//You should have received a copy of the GNU Lesser General Public
//License along with this library; if not, write to the Free Software
//Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// ---------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotNetWorkQueue.Configuration;
using DotNetWorkQueue.Transport.RelationalDatabase;
using DotNetWorkQueue.Transport.RelationalDatabase.Basic.Command;
using DotNetWorkQueue.Transport.RelationalDatabase.Basic.Query;
using DotNetWorkQueue.Transport.Shared;
using DotNetWorkQueue.Validation;
using Npgsql;

namespace DotNetWorkQueue.Transport.PostgreSQL.Basic.Message
{
    /// <summary>
    /// Handles receiving a message
    /// </summary>
    internal class ReceiveMessage
    {
        private readonly QueueConsumerConfiguration _configuration;
        private readonly RelationalDatabase.IQueryHandler<ReceiveMessageQuery<NpgsqlConnection, NpgsqlTransaction>, IReceivedMessageInternal> _receiveMessage;
        private readonly RelationalDatabase.IQueryHandler<ReceiveMessageQueryAsync<NpgsqlConnection, NpgsqlTransaction>, Task<IReceivedMessageInternal>> _receiveMessageAsync;
        private readonly ICommandHandler<SetStatusTableStatusCommand> _setStatusCommandHandler;
        private readonly ICancelWork _cancelToken;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReceiveMessage" /> class.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="receiveMessage">The receive message.</param>
        /// <param name="setStatusCommandHandler">The set status command handler.</param>
        /// <param name="cancelToken">The cancel token.</param>
        /// <param name="receiveMessageAsync">The receive message asynchronous.</param>
        public ReceiveMessage(QueueConsumerConfiguration configuration, RelationalDatabase.IQueryHandler<ReceiveMessageQuery<NpgsqlConnection, NpgsqlTransaction>, IReceivedMessageInternal> receiveMessage,
            ICommandHandler<SetStatusTableStatusCommand> setStatusCommandHandler,
            IQueueCancelWork cancelToken, RelationalDatabase.IQueryHandler<ReceiveMessageQueryAsync<NpgsqlConnection, NpgsqlTransaction>, Task<IReceivedMessageInternal>> receiveMessageAsync)
        {
            Guard.NotNull(() => configuration, configuration);
            Guard.NotNull(() => receiveMessage, receiveMessage);
            Guard.NotNull(() => setStatusCommandHandler, setStatusCommandHandler);
            Guard.NotNull(() => cancelToken, cancelToken);
            Guard.NotNull(() => receiveMessageAsync, receiveMessageAsync);

            _configuration = configuration;
            _receiveMessage = receiveMessage;
            _setStatusCommandHandler = setStatusCommandHandler;
            _cancelToken = cancelToken;
            _receiveMessageAsync = receiveMessageAsync;
        }

        /// <summary>
        /// Returns the next message, if any.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="connectionHolder">The connection.</param>
        /// <param name="noMessageFoundActon">The no message found action.</param>
        /// <param name="routes">The routes.</param>
        /// <returns>
        /// A message if one is found; null otherwise
        /// </returns>
        public IReceivedMessageInternal GetMessage(IMessageContext context, IConnectionHolder<NpgsqlConnection, NpgsqlTransaction, NpgsqlCommand> connectionHolder,
            Action<IConnectionHolder<NpgsqlConnection, NpgsqlTransaction, NpgsqlCommand>> noMessageFoundActon, List<string> routes )
        {
            //if stopping, exit now
            if (_cancelToken.Tokens.Any(t => t.IsCancellationRequested))
            {
                noMessageFoundActon(connectionHolder);
                return null;
            }

            //ask for the next message
            var receivedTransportMessage =
                _receiveMessage.Handle(new ReceiveMessageQuery<NpgsqlConnection, NpgsqlTransaction>(connectionHolder.Connection,
                    connectionHolder.Transaction, routes));

            return ProcessMessage(receivedTransportMessage, connectionHolder, context, noMessageFoundActon);
        }

        /// <summary>
        /// Returns the next message, if any.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="connectionHolder">The connection.</param>
        /// <param name="noMessageFoundActon">The no message found action.</param>
        /// <param name="routes">The routes.</param>
        /// <returns>
        /// A message if one is found; null otherwise
        /// </returns>
        public async Task<IReceivedMessageInternal> GetMessageAsync(IMessageContext context, IConnectionHolder<NpgsqlConnection, NpgsqlTransaction, NpgsqlCommand> connectionHolder,
            Action<IConnectionHolder<NpgsqlConnection, NpgsqlTransaction, NpgsqlCommand>> noMessageFoundActon, List<string> routes )
        {
            //if stopping, exit now
            if (_cancelToken.Tokens.Any(t => t.IsCancellationRequested))
            {
                noMessageFoundActon(connectionHolder);
                return null;
            }

            //ask for the next message, or a specific message if we have a messageID
            var receivedTransportMessage = await 
                _receiveMessageAsync.Handle(new ReceiveMessageQueryAsync<NpgsqlConnection, NpgsqlTransaction>(connectionHolder.Connection,
                    connectionHolder.Transaction, routes)).ConfigureAwait(false);

            return ProcessMessage(receivedTransportMessage, connectionHolder, context, noMessageFoundActon);
        }

        private IReceivedMessageInternal ProcessMessage(IReceivedMessageInternal receivedTransportMessage,
            IConnectionHolder<NpgsqlConnection, NpgsqlTransaction, NpgsqlCommand> connectionHolder,
            IMessageContext context,
            Action<IConnectionHolder<NpgsqlConnection, NpgsqlTransaction, NpgsqlCommand>> noMessageFoundActon)
        {
            //if no message (null) run the no message action and return
            if (receivedTransportMessage == null)
            {
                noMessageFoundActon(connectionHolder);
                return null;
            }

            //set the message ID on the context for later usage
            context.SetMessageAndHeaders(receivedTransportMessage.MessageId, receivedTransportMessage.Headers);

            //we need to update the status table here, as we don't do it as part of the de-queue
            if (_configuration.Options().EnableStatusTable)
            {
                _setStatusCommandHandler.Handle(
                    new SetStatusTableStatusCommand(
                        (long)receivedTransportMessage.MessageId.Id.Value, QueueStatuses.Processing));
            }
            return receivedTransportMessage;
        }
    }
}
