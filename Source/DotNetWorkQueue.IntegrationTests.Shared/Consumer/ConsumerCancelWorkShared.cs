﻿using System;
using System.Threading;
using DotNetWorkQueue.Logging;
using Xunit;

namespace DotNetWorkQueue.IntegrationTests.Shared.Consumer
{
    public class ConsumerCancelWorkShared<TTransportInit, TMessage>
        where TTransportInit : ITransportInit, new()
         where TMessage : class
    {
        private string _queueName;
        private string _connectionString;
        private int _workerCount;
        private TimeSpan _heartBeatTime;
        private TimeSpan _heartBeatMonitorTime;
        private int _runTime;
        private IConsumerQueue _queue;
        private QueueContainer<TTransportInit> _badQueueContainer;
        private Action<IContainer> _badQueueAdditions;

        public void RunConsumer(string queueName, string connectionString, bool addInterceptors,
            ILogProvider logProvider,
            int runTime, int messageCount,
            int workerCount, int timeOut, Action<IContainer> badQueueAdditions,
            TimeSpan heartBeatTime, TimeSpan heartBeatMonitorTime, string updateTime, string route, bool enableChaos)
        {
            _queueName = queueName;
            _connectionString = connectionString;
            _workerCount = workerCount;
            _runTime = runTime;
            _badQueueAdditions = badQueueAdditions;

            _heartBeatTime = heartBeatTime;
            _heartBeatMonitorTime = heartBeatMonitorTime;

            _queue = CreateConsumerInternalThread(updateTime, route);
            var t = new Thread(RunBadQueue);
            t.Start();

            if (enableChaos)
                timeOut *= 2;

            //run consumer
            RunConsumerInternal(queueName, connectionString, addInterceptors, logProvider, runTime,
                messageCount, workerCount, timeOut, _queue, heartBeatTime, heartBeatMonitorTime, updateTime, route, enableChaos);
        }


        private void RunConsumerInternal(string queueName, string connectionString, bool addInterceptors,
            ILogProvider logProvider,
            int runTime, int messageCount,
            int workerCount, int timeOut, IDisposable queueBad,
            TimeSpan heartBeatTime, TimeSpan heartBeatMonitorTime, string updateTime, string route, bool enableChaos)
        {

            using (var metrics = new Metrics.Metrics(queueName))
            {
                var processedCount = new IncrementWrapper();
                var addInterceptorConsumer = InterceptorAdding.No;
                if (addInterceptors)
                {
                    addInterceptorConsumer = InterceptorAdding.ConfigurationOnly; 
                }
                using (
                    var creator = SharedSetup.CreateCreator<TTransportInit>(addInterceptorConsumer, logProvider, metrics, false, enableChaos)
                    )
                {

                    using (
                        var queue =
                            creator.CreateConsumer(queueName,
                                connectionString))
                    {
                        SharedSetup.SetupDefaultConsumerQueue(queue.Configuration, workerCount, heartBeatTime,
                            heartBeatMonitorTime, updateTime, route);
                        var waitForFinish = new ManualResetEventSlim(false);
                        waitForFinish.Reset();
                        //start looking for work
                        queue.Start<TMessage>((message, notifications) =>
                        {
                            MessageHandlingShared.HandleFakeMessages<TMessage>(null, runTime, processedCount, messageCount,
                                waitForFinish);
                        });

                        var time = runTime*1000/2;
                        waitForFinish.Wait(time);

                        queueBad.Dispose();
                        _badQueueContainer.Dispose();

                        waitForFinish.Wait(timeOut*1000 - time);
                    }
                    Assert.Equal(messageCount, processedCount.ProcessedCount);
                    VerifyMetrics.VerifyProcessedCount(queueName, metrics.GetCurrentMetrics(), messageCount);
                    LoggerShared.CheckForErrors(queueName);
                }
            }
        }

        private IConsumerQueue CreateConsumerInternalThread(string updateTime, string route)
        {
            _badQueueContainer = SharedSetup.CreateCreator<TTransportInit>(_badQueueAdditions);

            var queue =
                _badQueueContainer.CreateConsumer(_queueName,
                    _connectionString);

            SharedSetup.SetupDefaultConsumerQueue(queue.Configuration, _workerCount, _heartBeatTime,
                _heartBeatMonitorTime, updateTime, route);
            return queue;
        }

        private void RunBadQueue()
        {
            //start looking for work
            try
            {
                _queue.Start<TMessage>((message, notifications) =>
                {
                    MessageHandlingShared.HandleFakeMessagesThreadAbort(_runTime * 1000 / 2);
                });
            }
            catch
            {
               //bad queue failing is weird, but doesn't really need to be handled right here
            }
        }
    }
}
