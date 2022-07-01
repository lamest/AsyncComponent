/*
MIT License

Copyright (c) 2022 Ivan Sukhikh
*/

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino;

namespace AsyncComponent;

public abstract class AsyncComponentBase : GH_Component
{
    private CancellationTokenSource _cts = new();

    protected AsyncComponentBase(string name, string nickname, string description, string category, string subCategory) :
        base(name, nickname, description, category, subCategory)
    {
    }

    private Worker CurrentWorker { get; set; }

    protected abstract Worker CreateWorker(Action<string> progressReporter);

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        _cts.Cancel();

        if (IsManualExpire)
        {
            //assuming SolveInstance call happens right after ExpireSolution call and in the same thread
            //that way we only get here after the async solve is done
            if (ErrorMessage != null)
            {
                var w = GH_RuntimeMessageLevel.Error;
                AddRuntimeMessage(w, ErrorMessage);
                Message = "Error";
            }
            else
            {
                string doneMessage = null;
                CurrentWorker?.SetOutput(DA, out doneMessage);
                Message = string.IsNullOrWhiteSpace(doneMessage) ? "Done" : doneMessage;
            }

            CurrentWorker = null;

            if (IsExpired)
            {
                OnDisplayExpired(true);
                IsExpired = true;
            }

            return;
        }

        IsExpired = false;
        _cts = new CancellationTokenSource();

        var token = _cts.Token;
        var newWorker = CreateWorker(p =>
        {
            RhinoApp.InvokeOnUiThread(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    Message = string.IsNullOrWhiteSpace(p) ? "Working..." : p;
                    OnDisplayExpired(true);
                }
            });
        });
        newWorker.GatherInput(DA, Params);

        CurrentWorker = newWorker;
        Task.Run(async () => await SolveAsync(newWorker, token), token);
    }

    public bool IsExpired { get; set; }

    private async Task SolveAsync(Worker worker, CancellationToken token)
    {
        string errorMessage = null;
        try
        {
            await worker.DoWorkAsync(token);
        }
        catch (Exception ex)
        {
            //RhinoApp.InvokeOnUiThread(new Action(() => throw new Exception("SolveAsync failed", ex)));
            errorMessage = ex.Message;
        }

        RhinoApp.InvokeOnUiThread(() => Done(token, errorMessage));
    }


    private void Done(CancellationToken token, string errorMessage)
    {
        //assert is the main thread
        Debug.Assert(Thread.CurrentThread.ManagedThreadId == 1);

        if (token.IsCancellationRequested)
        {
            return;
        }

        //assuming we are in the main thread and nobody will call SolveInstance before us to create a race
        try
        {
            ErrorMessage = errorMessage;
            IsManualExpire = true;
            ExpireSolution(true);
        }
        finally
        {
            IsManualExpire = false;
            ErrorMessage = null;
        }
    }

    public string ErrorMessage { get; set; }

    private bool IsManualExpire { get; set; }

    public void Cancel()
    {
        _cts.Cancel();
    }

    public abstract class Worker
    {
        protected readonly Action<string> _progressReporter;

        public Worker(Action<string> progressReporter)
        {
            _progressReporter = progressReporter;
        }

        internal bool IsDone { get; private set; }

        public abstract Task DoWorkAsync(CancellationToken token);
        public abstract void GatherInput(IGH_DataAccess data, GH_ComponentParamServer p);

        internal void SetDone()
        {
            IsDone = true;
        }

        public abstract void SetOutput(IGH_DataAccess data, out string doneMessage);
    }

    public abstract class WorkerWithTimer : Worker
    {
        protected TimeSpan _lastWorkTime;

        protected WorkerWithTimer(Action<string> progressReporter) : base(progressReporter)
        {
        }

        public abstract Task DoTimeMeasuredWorkAsync(CancellationToken token);

        public override async Task DoWorkAsync(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await DoTimeMeasuredWorkAsync(token);
            }
            finally
            {
                //in case of reentrancy, the token should be canceled so we will not spoil _lastWorkTime 
                if (!token.IsCancellationRequested)
                {
                    _lastWorkTime = sw.Elapsed;
                }
            }
        }
    }
}