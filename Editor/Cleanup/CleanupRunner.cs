using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Observable state of a running cleanup task: what it is doing, how far along it is, how long it has
    /// been running and whether the user asked to cancel. UI code subscribes to <see cref="Changed"/>.
    /// </summary>
    public sealed class CleanupProgress
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _stageIndex;
        private int _stageCount = 1;
        private bool _stageCountLocked;
        private int _stageTotal;
        private int _stageCurrent;

        public CleanupProgress(string title)
        {
            Title = title;
        }

        /// <summary>Name of the whole task, e.g. "Full project audit".</summary>
        public string Title { get; private set; }

        /// <summary>Name of the current stage, e.g. "Scanning scenes".</summary>
        public string Stage { get; private set; } = string.Empty;

        /// <summary>What is being processed right now (usually an asset path).</summary>
        public string Detail { get; private set; } = string.Empty;

        /// <summary>True once the user asked to stop; sweeps check this between assets.</summary>
        public bool CancelRequested { get; private set; }

        /// <summary>True when the task ended because it was cancelled.</summary>
        public bool WasCancelled { get; internal set; }

        /// <summary>True when the task ended because of an unhandled exception.</summary>
        public bool Failed { get; internal set; }

        public bool IsFinished { get; internal set; }

        public double ElapsedSeconds => _clock.Elapsed.TotalSeconds;

        public int StageIndex => _stageIndex;
        public int StageCount => _stageCount;
        public int ItemsDone => _stageCurrent;
        public int ItemsTotal => _stageTotal;

        /// <summary>Overall progress from 0 to 1 across all stages.</summary>
        public float Fraction
        {
            get
            {
                var stageFraction = _stageTotal <= 0 ? 0f : Math.Min(1f, (float)_stageCurrent / _stageTotal);
                return Math.Min(1f, (_stageIndex + stageFraction) / Math.Max(1, _stageCount));
            }
        }

        /// <summary>Estimated seconds left, or null while there is not enough data to guess.</summary>
        public double? EtaSeconds
        {
            get
            {
                var fraction = Fraction;
                if (fraction < 0.03f || ElapsedSeconds < 1.0) return null;
                return ElapsedSeconds * (1.0 - fraction) / fraction;
            }
        }

        public event Action Changed;

        /// <summary>
        /// Declares how many stages (sub-tasks) the whole task has. Meant for the outermost routine of a
        /// composite task: once set, inner routines can no longer change it (see <see cref="EnsureStageCount"/>).
        /// </summary>
        public void SetStageCount(int count)
        {
            _stageCount = Math.Max(1, count);
            _stageCountLocked = true;
            Raise();
        }

        /// <summary>
        /// Like <see cref="SetStageCount"/> but ignored when an outer routine already declared the total,
        /// so a routine can describe itself when run alone and still nest inside a bigger task.
        /// </summary>
        public void EnsureStageCount(int count)
        {
            if (_stageCountLocked) return;

            _stageCount = Math.Max(1, count);
            Raise();
        }

        /// <summary>
        /// Moves to the next stage (the first call stays on stage 0) and declares how many items it has.
        /// Every sweep calls this once, so a composite task advances one stage per sweep.
        /// </summary>
        public void NextStage(string stage, int totalItems)
        {
            if (!string.IsNullOrEmpty(Stage) || _stageCurrent > 0 || _stageTotal > 0)
                _stageIndex = Math.Min(_stageIndex + 1, _stageCount - 1);

            Stage = stage ?? string.Empty;
            Detail = string.Empty;
            _stageTotal = Math.Max(0, totalItems);
            _stageCurrent = 0;
            Raise();
        }

        /// <summary>Marks one more item as processed and describes the next one.</summary>
        public void Advance(string detail)
        {
            _stageCurrent++;
            Detail = detail ?? string.Empty;
            Raise();
        }

        /// <summary>Marks several items as processed at once (batched work).</summary>
        public void AdvanceBy(int count, string detail)
        {
            _stageCurrent += Math.Max(0, count);
            Detail = detail ?? string.Empty;
            Raise();
        }

        /// <summary>Describes the item being processed without advancing.</summary>
        public void SetDetail(string detail)
        {
            Detail = detail ?? string.Empty;
            Raise();
        }

        public void RequestCancel()
        {
            if (CancelRequested) return;
            CancelRequested = true;
            Raise();
        }

        internal void Raise()
        {
            Changed?.Invoke();
        }

        /// <summary>Human readable "1m 05s" style text for elapsed / remaining time.</summary>
        public static string FormatDuration(double seconds)
        {
            var total = (int)Math.Max(0, Math.Round(seconds));
            return total < 60 ? $"{total}s" : $"{total / 60}m {total % 60:00}s";
        }
    }

    /// <summary>
    /// Runs cleanup work without freezing the editor. A task is an <see cref="IEnumerator"/> that yields
    /// after each asset; the runner resumes it from <see cref="EditorApplication.update"/> with a small time
    /// budget per frame, so windows keep repainting, scroll and react to the Cancel button while a project
    /// wide sweep is running. <see cref="RunBlocking"/> drains the same task synchronously (menu items and
    /// public API) behind the classic modal progress bar so even those calls are never silent.
    /// </summary>
    public static class CleanupRunner
    {
        private const double FrameBudgetMilliseconds = 12.0;

        private static IEnumerator _routine;
        private static CleanupProgress _progress;
        private static Action<CleanupProgress> _onFinished;
        private static int _backgroundTaskId = -1;

        /// <summary>True while an asynchronous task is running. Only one task runs at a time.</summary>
        public static bool IsBusy => _routine != null;

        /// <summary>The task currently running, or null.</summary>
        public static CleanupProgress Current => _progress;

        /// <summary>Raised when a task starts (progress is never null).</summary>
        public static event Action<CleanupProgress> TaskStarted;

        /// <summary>Raised after a task ended, whether it completed, was cancelled or failed.</summary>
        public static event Action<CleanupProgress> TaskFinished;

        /// <summary>
        /// Starts a task on the editor loop and returns immediately. Returns null (and logs) when another
        /// task is still running. <paramref name="onFinished"/> runs on the main thread after the task ended.
        /// </summary>
        public static CleanupProgress Start(string title, Func<CleanupProgress, IEnumerator> factory,
            Action<CleanupProgress> onFinished = null)
        {
            if (IsBusy)
            {
                Debug.LogWarning($"[UnityUtils] '{_progress.Title}' is still running; wait for it to finish or cancel it.");
                return null;
            }

            _progress = new CleanupProgress(title);
            _routine = new NestedRoutine(factory(_progress));
            _onFinished = onFinished;

            StartBackgroundTask(_progress);
            _progress.Changed += ReportBackgroundTask;

            EditorApplication.update += Tick;
            TaskStarted?.Invoke(_progress);
            _progress.Raise();
            return _progress;
        }

        /// <summary>
        /// Runs a task to the end before returning. The editor is busy meanwhile, but a modal progress bar
        /// with a Cancel button is shown and the task stays cancellable.
        /// </summary>
        public static void RunBlocking(string title, Func<CleanupProgress, IEnumerator> factory)
        {
            var progress = new CleanupProgress(title);
            Action show = () =>
            {
                var detail = string.IsNullOrEmpty(progress.Detail) ? progress.Stage : progress.Detail;
                if (EditorUtility.DisplayCancelableProgressBar(title, detail, progress.Fraction))
                    progress.RequestCancel();
            };

            progress.Changed += show;
            var routine = new NestedRoutine(factory(progress));

            try
            {
                while (routine.MoveNext())
                {
                }
            }
            finally
            {
                progress.Changed -= show;
                Dispose(routine);
                EditorUtility.ClearProgressBar();
                progress.IsFinished = true;
                progress.WasCancelled = progress.CancelRequested;
            }
        }

        /// <summary>
        /// Lets a task yield another iterator (<c>yield return OtherRoutine()</c>) and run it to the end
        /// before continuing, like a coroutine call. Any other yielded value just ends the current frame.
        /// </summary>
        private sealed class NestedRoutine : IEnumerator, IDisposable
        {
            private readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();

            public NestedRoutine(IEnumerator root)
            {
                _stack.Push(root);
            }

            public object Current => null;

            public bool MoveNext()
            {
                while (_stack.Count > 0)
                {
                    var top = _stack.Peek();

                    if (!top.MoveNext())
                    {
                        _stack.Pop();
                        DisposeOne(top);
                        continue;
                    }

                    if (top.Current is IEnumerator inner)
                    {
                        _stack.Push(inner);
                        continue;
                    }

                    return true;
                }

                return false;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
                while (_stack.Count > 0)
                    DisposeOne(_stack.Pop());
            }

            private static void DisposeOne(IEnumerator routine)
            {
                try
                {
                    (routine as IDisposable)?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UnityUtils] Cleanup task did not dispose cleanly: {ex.Message}");
                }
            }
        }

        private static void Tick()
        {
            var routine = _routine;
            if (routine == null)
            {
                EditorApplication.update -= Tick;
                return;
            }

            var clock = Stopwatch.StartNew();
            var finished = false;

            try
            {
                while (true)
                {
                    if (!routine.MoveNext())
                    {
                        finished = true;
                        break;
                    }

                    if (clock.Elapsed.TotalMilliseconds >= FrameBudgetMilliseconds)
                        break;
                }
            }
            catch (Exception ex)
            {
                _progress.Failed = true;
                Debug.LogError($"[UnityUtils] '{_progress.Title}' failed: {ex}");
                finished = true;
            }

            if (finished)
                Finish();
        }

        private static void Finish()
        {
            var progress = _progress;
            var onFinished = _onFinished;

            EditorApplication.update -= Tick;
            Dispose(_routine);
            _routine = null;
            _onFinished = null;
            _progress = null;

            progress.Changed -= ReportBackgroundTask;
            progress.IsFinished = true;
            progress.WasCancelled = progress.CancelRequested && !progress.Failed;
            EndBackgroundTask(progress);
            progress.Raise();

            try
            {
                onFinished?.Invoke(progress);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityUtils] Completion handler of '{progress.Title}' failed: {ex}");
            }

            TaskFinished?.Invoke(progress);
        }

        private static void Dispose(IEnumerator routine)
        {
            // Compiler generated iterators implement IDisposable; disposing runs their finally blocks
            // (for example restoring the scene setup after a cancelled sweep).
            try
            {
                (routine as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityUtils] Cleanup task did not dispose cleanly: {ex.Message}");
            }
        }

        // Mirror the task in Unity's Background Tasks window (bottom-right indicator, with its own Cancel).
        private static void StartBackgroundTask(CleanupProgress progress)
        {
            try
            {
                _backgroundTaskId = Progress.Start(progress.Title, null, Progress.Options.None);
                Progress.RegisterCancelCallback(_backgroundTaskId, () =>
                {
                    progress.RequestCancel();
                    return true;
                });
            }
            catch (Exception)
            {
                _backgroundTaskId = -1; // The Progress API is a nicety, never a requirement.
            }
        }

        private static void ReportBackgroundTask()
        {
            if (_backgroundTaskId < 0 || _progress == null) return;

            try
            {
                var detail = string.IsNullOrEmpty(_progress.Detail) ? _progress.Stage : _progress.Detail;
                Progress.Report(_backgroundTaskId, _progress.Fraction, detail);
            }
            catch (Exception)
            {
                _backgroundTaskId = -1;
            }
        }

        private static void EndBackgroundTask(CleanupProgress progress)
        {
            if (_backgroundTaskId < 0) return;

            try
            {
                var status = progress.Failed ? Progress.Status.Failed
                    : progress.WasCancelled ? Progress.Status.Canceled
                    : Progress.Status.Succeeded;
                Progress.Finish(_backgroundTaskId, status);
            }
            catch (Exception)
            {
                // Ignore: the task itself already ended.
            }

            _backgroundTaskId = -1;
        }
    }
}
