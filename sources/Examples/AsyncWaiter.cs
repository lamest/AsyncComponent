/*
MIT License

Copyright (c) 2022 Ivan Sukhikh
*/

using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using AsyncComponent;
using Examples.Properties;
using Grasshopper.Kernel;

namespace Examples;

public class AsyncWaiter : AsyncComponentBase
{
    public AsyncWaiter() : base("Async waiter", "Async",
        "Wait for the period of time, report progress and complete asynchronously", "Async examples", "Tools")
    {
    }

    public override Guid ComponentGuid { get; } = new("edccfb23-0ce5-468f-9bcb-98de9eab9cd9");

    protected override Bitmap Icon => Resources.logo32;

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Time", "T", "Time to wait in milliseconds", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
    }

    protected override Worker CreateWorker(Action<string> progressReporter)
    {
        return new AsyncWaiterWorker(progressReporter);
    }

    protected class AsyncWaiterWorker : WorkerWithTimer
    {
        private readonly int _reportPeriod = 1000;
        private bool _allDataRead;
        private double _waitTime;

        public AsyncWaiterWorker(Action<string> progressReporter) : base(progressReporter)
        {
        }

        public override async Task DoTimeMeasuredWorkAsync(CancellationToken token)
        {
            if (!_allDataRead)
            {
                return;
            }

            _progressReporter.Invoke("0%");
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var timespan = TimeSpan.FromMilliseconds(_waitTime);
            cts.CancelAfter(timespan);
            var sw = Stopwatch.StartNew();
            var linkedToken = cts.Token;
            var lastReportTime = 0d;
            while (!linkedToken.IsCancellationRequested)
            {
                if (sw.ElapsedMilliseconds - lastReportTime > _reportPeriod)
                {
                    lastReportTime = sw.ElapsedMilliseconds;
                    var percent = sw.Elapsed.TotalMilliseconds * 100 / _waitTime;
                    _progressReporter.Invoke($"{percent:F2}%");
                }

                try
                {
                    await Task.Delay(_reportPeriod, linkedToken);
                }
                catch (TaskCanceledException)
                {
                }
            }

            _progressReporter.Invoke("100%");
        }


        public override void SetOutput(IGH_DataAccess data, out string m)
        {
            m = $"Done, {_lastWorkTime.TotalSeconds:F2}s";
        }

        public override void GatherInput(IGH_DataAccess data, GH_ComponentParamServer p)
        {
            //reset
            _allDataRead = true;
            _waitTime = 0d;

            //read
            _allDataRead &= data.GetData(0, ref _waitTime);
        }
    }
}