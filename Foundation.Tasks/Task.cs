using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Foundation.Tasks
{
    /// <summary>
    /// Describes the Tasks State
    /// </summary>
    public enum TaskStatus
    {
        /// <summary>
        /// Ready to run
        /// </summary>
        Created,
        /// <summary>
        /// Working
        /// </summary>
        Running,
        /// <summary>
        /// Exception as thrown or otherwise stopped early
        /// </summary>
        Faulted,
        /// <summary>
        /// Complete without error
        /// </summary>
        Success,
        /// <summary>
        /// Dispose has been called
        /// </summary>
        Disposed,
    }

    /// <summary>
    /// Execution strategy for the Task
    /// </summary>
    public enum TaskStrategy
    {
        /// <summary>
        /// Dispatches the task to a background thread
        /// </summary>
        BackgroundThread,
        /// <summary>
        /// Dispatches the task to the main thread
        /// </summary>
        MainThread,
        /// <summary>
        /// Dispatches the task to the current thread
        /// </summary>
        CurrentThread,
        /// <summary>
        /// Runs the task as a coroutine
        /// </summary>
        Coroutine,
        /// <summary>
        /// Does nothing. For custom tasks.
        /// </summary>
        Custom,
    }

    /// <summary>
    /// A task encapsulates future work that may be waited on.
    /// - Support running actions in background threads 
    /// - Supports running coroutines with return results
    /// - Use the WaitForRoutine method to wait for the task in a coroutine
    /// </summary>
    /// <example>
    /// <code>
    ///     var task = Task.Run(() =>
    ///     {
    ///        //Debug.Log does not work in
    ///        Debug.Log("Sleeping...");
    ///        Task.Delay(2000);
    ///        Debug.Log("Slept");
    ///    });
    ///    // wait for it
    ///    yield return StartCoroutine(task.WaitRoutine());
    ///
    ///    // check exceptions
    ///    if(task.IsFaulted)
    ///        Debug.LogException(task.Exception)
    ///</code>
    ///</example>
    public partial class Task : IDisposable
    {
        #region options
        /// <summary>
        /// Forces use of a single thread for debugging
        /// </summary>
        public static bool DisableMultiThread = false;

        /// <summary>
        /// Logs Exceptions
        /// </summary>
        public static bool LogErrors = false;
        #endregion

        #region fields
        // ReSharper disable InconsistentNaming

        /// <summary>
        /// Parameter
        /// </summary> 
        public object Paramater;

        /// <summary>
        /// Execution option
        /// </summary>
        public TaskStrategy Strategy;

        Action _action;
        Delegate _action2;
        protected IEnumerator _routine;

        protected List<Delegate> OnComplete = new List<Delegate>(1);

        #endregion

        #region properties

        private TaskStatus _status;
        public TaskStatus Status
        {
            get { return _status; }
            set
            {
                if (_status == value)
                    return;
                _status = value;


                if (IsCompleted)
                    OnTaskComplete();
            }
        }

        public Exception Exception;
        #endregion

        #region computed properties
        public bool IsRunning
        {
            get { return !IsCompleted; }
        }

        public bool IsCompleted
        {
            get { return Status == TaskStatus.Success || Status == TaskStatus.Faulted; }
        }

        public bool IsFaulted
        {
            get { return Status == TaskStatus.Faulted; }
        }

        public bool IsSuccess
        {
            get { return Status == TaskStatus.Success; }
        }
        #endregion

        #region constructor

        static Task()
        {
            TaskManager.ConfirmInit();
        }

        /// <summary>
        /// Creates a new task
        /// </summary>
        protected Task()
        {
            Status = TaskStatus.Created;
        }

        /// <summary>
        /// Creates a new task
        /// </summary>
        public Task(TaskStrategy mode)
            : this()
        {
            Strategy = mode;
        }

        /// <summary>
        /// Creates a new Task in a Faulted state
        /// </summary>
        /// <param name="ex"></param>
        public Task(Exception ex)
        {
            Exception = ex;
            Strategy = TaskStrategy.Custom;
            Status = TaskStatus.Faulted;
        }

        /// <summary>
        /// Creates a new background task
        /// </summary>
        /// <param name="action"></param>
        public Task(Action action)
            : this()
        {
            _action = action;
            Strategy = TaskStrategy.BackgroundThread;
        }

        /// <summary>
        /// Creates a new Task 
        /// </summary>
        /// <param name="action"></param>
        /// <param name="mode"></param>
        public Task(Action action, TaskStrategy mode)
            : this()
        {
            if (mode == TaskStrategy.Coroutine)
                throw new ArgumentException("Action tasks may not be coroutines");

            _action = action;
            Strategy = mode;
        }

        /// <summary>
        /// Creates a new Coroutine Task
        /// </summary>
        /// <param name="action"></param>
        public Task(IEnumerator action)
            : this()
        {
            if (action == null)
                throw new ArgumentNullException("action");

            _routine = action;
            Strategy = TaskStrategy.Coroutine;
        }


        /// <summary>
        /// Creates a new Coroutine Task
        /// </summary>
        /// <param name="action"></param>
        /// <param name="param"></param>
        public Task(IEnumerator action, object param)
            : this()
        {
            if (action == null)
                throw new ArgumentNullException("action");

            _routine = action;
            Strategy = TaskStrategy.Coroutine;
            Paramater = param;
        }

        /// <summary>
        /// Creates a new background task with a paramater
        /// </summary>
        /// <param name="action"></param>
        /// <param name="paramater"></param>
        public Task(Delegate action, object paramater)
            : this()
        {
            _action2 = action;
            Strategy = TaskStrategy.BackgroundThread;
            Paramater = paramater;
        }

        /// <summary>
        /// Creates a new Task with a paramater
        /// </summary>
        /// <param name="action"></param>
        /// <param name="paramater"></param>
        /// <param name="mode"></param>
        public Task(Delegate action, object paramater, TaskStrategy mode)
            : this()
        {
            if (mode == TaskStrategy.Coroutine)
                throw new ArgumentException("Action tasks may not be coroutines");

            _action2 = action;
            Strategy = mode;
            Paramater = paramater;
        }

        #endregion

        #region Private

        protected virtual void Execute()
        {
            try
            {
                if (_action2 != null)
                {
                    _action2.DynamicInvoke(Paramater);
                }
                else if (_action != null)
                {
                    _action();
                }
                Status = TaskStatus.Success;
            }
            catch (Exception ex)
            {
                Exception = ex;
                Status = TaskStatus.Faulted;

                if (LogErrors)
                    UnityEngine.Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Executes the task in background thread
        /// </summary>
        protected void RunOnBackgroundThread()
        {
            Status = TaskStatus.Running;
            ThreadPool.QueueUserWorkItem(state => Execute());
        }

        /// <summary>
        /// Executes the task in background thread
        /// </summary>
        protected void RunOnCurrentThread()
        {
            Status = TaskStatus.Running;
            Execute();
        }

        /// <summary>
        /// Executes the task on the main thread
        /// </summary>
        protected void RunOnMainThread()
        {
            Status = TaskStatus.Running;
            TaskManager.RunOnMainThread(Execute);
        }

        /// <summary>
        /// Executes the task in a coroutine
        /// </summary>
        protected void RunAsCoroutine()
        {
            Status = TaskStatus.Running;

            TaskManager.StartRoutine(new TaskManager.CoroutineCommand
            {
                Coroutine = _routine,
                OnComplete = OnRoutineComplete
            });
        }

        protected virtual void OnTaskComplete()
        {
            if (OnComplete != null)
            {
                for (int i = 0;i < OnComplete.Count;i++)
                {
                    OnComplete[i].DynamicInvoke(this);
                }
            }
        }

        protected void OnRoutineComplete()
        {
            if (Status == TaskStatus.Running || Status == TaskStatus.Created)
                Status = TaskStatus.Success;
        }
        #endregion

        #region public methods

        /// <summary>
        /// Executes the task
        /// </summary>
        public void Start()
        {
            if (IsCompleted)
            {
                return;
            }

            switch (Strategy)
            {
                case TaskStrategy.BackgroundThread:
                    if (DisableMultiThread)
                        RunOnCurrentThread();
                    else
                        RunOnBackgroundThread();
                    break;
                case TaskStrategy.CurrentThread:
                    RunOnCurrentThread();
                    break;
                case TaskStrategy.MainThread:
                    RunOnMainThread();
                    break;
                case TaskStrategy.Coroutine:
                    RunAsCoroutine();
                    break;
            }
        }

        /// <summary>
        /// Called after the task is complete
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public Task ContinueWith(Action<Task> action)
        {
            if (IsCompleted)
            {
                action(this);
            }
            else
            {
                if (OnComplete == null)
                    OnComplete = new List<Delegate>(1);

                OnComplete.Add(action);
            }
            return this;
        }

        /// <summary>
        /// Called after the task is complete
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public T ContinueWith<T>(Action<T> action) where T : Task
        {
            if (IsCompleted)
            {
                action(this as T);
            }
            else
            {
                OnComplete.Add(action);
            }
            return this as T;
        }

        /// <summary>
        /// will throw if faulted
        /// </summary>
        /// <returns></returns>
        public Task ThrowIfFaulted()
        {
            if (IsFaulted)
                throw Exception;
            return this;
        }

        /// <summary>
        /// Wait for the task to complete in an iterator coroutine
        /// </summary>
        /// <returns></returns>
        public IEnumerator WaitRoutine()
        {
            yield return 1;

            while (IsRunning)
            {
                yield return 1;
            }
        }

        /// <summary>
        /// Waits for the task to complete
        /// </summary>
        public Task Wait()
        {
            if (TaskManager.IsMainThread && !DisableMultiThread)
            {
                UnityEngine.Debug.LogWarning("Use WaitRoutine in coroutine to wait in main thread");
            }

            Delay(10);

            while (IsRunning)
            {
                Delay(10);
            }

            return this;
        }

        /// <summary>
        /// Thread.Sleep
        /// </summary>
        /// <param name="millisecondTimeout"></param>
        public static void Delay(int millisecondTimeout)
        {
            Thread.Sleep(millisecondTimeout);
        }

        public virtual void Dispose()
        {
            Status = TaskStatus.Created;
            Paramater = null;
            Exception = null;
            _action = null;
            _action2 = null;
            _routine = null;
            OnComplete = null;
            _action = null;
        }

        public virtual void Reset()
        {
            Status = TaskStatus.Created;
            Paramater = null;
            Exception = null;
            _action = null;
            _action2 = null;
            _routine = null;
            OnComplete = null;
            _action = null;
        }
        #endregion
    }
}
