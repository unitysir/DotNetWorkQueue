﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DotNetWorkQueue;
using DotNetWorkQueue.Transport.Redis.Basic;
using SampleShared;
using Serilog;

namespace RedisScheduler
{
    class Program
    {
        static void Main(string[] args)
        {
            //we are using serilog for sample purposes; any https://github.com/damianh/LibLog provider can be used
            var log = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Debug()
                .CreateLogger();
            Log.Logger = log;
            log.Information("Startup");
            log.Information(SharedConfiguration.AllSettings);

            var queueName = ConfigurationManager.AppSettings.ReadSetting("QueueName");
            var connectionString = ConfigurationManager.AppSettings.ReadSetting("Database");

            using (var jobContainer = new JobSchedulerContainer(serviceRegister =>
                Injectors.AddInjectors(log, SharedConfiguration.EnableTrace, SharedConfiguration.EnableMetrics, SharedConfiguration.EnableCompression, SharedConfiguration.EnableEncryption, "RedisScheduler", serviceRegister)))
            {
                using (var scheduler = jobContainer.CreateJobScheduler(serviceRegister =>
                    Injectors.AddInjectors(log, SharedConfiguration.EnableTrace, SharedConfiguration.EnableMetrics, SharedConfiguration.EnableCompression, SharedConfiguration.EnableEncryption, "RedisScheduler", serviceRegister),
                    serviceRegister =>
                        Injectors.AddInjectors(log, SharedConfiguration.EnableTrace, SharedConfiguration.EnableMetrics, SharedConfiguration.EnableCompression, SharedConfiguration.EnableEncryption, "RedisScheduler", serviceRegister)))
                {
                    //start may be called before or after adding jobs
                    scheduler.Start();

                    var keepRunning = true;
                    IScheduledJob job1 = null;
                    IScheduledJob job2 = null;
                    IScheduledJob job3 = null;
                    while (keepRunning)
                    {
                        Console.WriteLine(@"a) Schedule job1
b) Schedule job2
c) Schedule job3

d) View scheduled jobs

e) Remove job1
f) Remove job2
g) Remove job3

q) Quit");
                        var key = char.ToLower(Console.ReadKey(true).KeyChar);

                        try
                        {
                            switch (key)
                            {
                                case 'a':
                                    job1 = scheduler.AddUpdateJob<RedisQueueInit, RedisJobQueueCreation>("test job1",
                                        queueName,
                                        connectionString,
                                        "sec(0,5,10,15,20,25,30,35,40,45,50,55)",
                                        (message, workerNotification) => Console.WriteLine("test job1 " + message.MessageId.Id.Value));
                                    log.Information("job scheduled");
                                    break;
                                case 'b':
                                    job2 = scheduler.AddUpdateJob<RedisQueueInit, RedisJobQueueCreation>("test job2",
                                        queueName,
                                        connectionString,
                                        "min(*)",
                                        (message, workerNotification) => Console.WriteLine("test job2 " + message.MessageId.Id.Value));
                                    log.Information("job scheduled");
                                    break;
                                case 'c':
                                    job3 = scheduler.AddUpdateJob<RedisQueueInit, RedisJobQueueCreation>("test job3",
                                        queueName,
                                        connectionString,
                                        "sec(30)",
                                        (message, workerNotification) => Console.WriteLine("test job3 " + message.MessageId.Id.Value));
                                    log.Information("job scheduled");
                                    break;
                                case 'd':
                                    var jobs = scheduler.GetAllJobs();
                                    foreach (var job in jobs)
                                    {
                                        Log.Information("Job: {@job}", job);
                                    }
                                    break;
                                case 'e':
                                    if (job1 != null)
                                    {
                                        job1.StopSchedule();
                                        if (scheduler.RemoveJob(job1.Name))
                                        {
                                            job1 = null;
                                            log.Information("job removed");
                                        }
                                    }
                                    break;
                                case 'f':
                                    if (job2 != null)
                                    {
                                        job2.StopSchedule();
                                        if (scheduler.RemoveJob(job2.Name))
                                        {
                                            job2 = null;
                                            log.Information("job removed");
                                        }
                                    }
                                    break;
                                case 'g':
                                    if (job3 != null)
                                    {
                                        job3.StopSchedule();
                                        if (scheduler.RemoveJob(job3.Name))
                                        {
                                            job3 = null;
                                            log.Information("job removed");
                                        }
                                    }
                                    break;
                                case 'q':
                                    Console.WriteLine("Quitting");
                                    keepRunning = false;
                                    break;
                            }
                        }
                        catch (Exception e)
                        {
                            log.Error(e, "Failed");
                        }
                    }
                }
            }
        }
    }
}