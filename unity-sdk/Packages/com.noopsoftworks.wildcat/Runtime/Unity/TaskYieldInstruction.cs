using System;
using System.Threading.Tasks;
using UnityEngine;

namespace WildCat.Unity
{
    /// <summary>
    /// Lets a coroutine wait for a WildCat call: <c>yield return task.AsCoroutine();</c>, then
    /// look at <see cref="IsSuccessful"/> and <see cref="Error"/>. Never throws; the outcome is in
    /// the properties.
    /// </summary>
    public class TaskYieldInstruction : CustomYieldInstruction
    {
        public TaskYieldInstruction(Task task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));
            Task = task;
        }

        public Task Task { get; }

        public override bool keepWaiting
        {
            get { return !Task.IsCompleted; }
        }

        /// <summary>The call finished, one way or another.</summary>
        public bool IsDone
        {
            get { return Task.IsCompleted; }
        }

        /// <summary>The call finished without an error.</summary>
        public bool IsSuccessful
        {
            get { return Task.Status == TaskStatus.RanToCompletion; }
        }

        /// <summary>
        /// Why the call failed: a <see cref="WildCatException"/> (server error), a TimeoutException,
        /// a <see cref="WildCatDisconnectedException"/>, an OperationCanceledException, or null.
        /// </summary>
        public Exception Error
        {
            get
            {
                if (Task.IsCanceled)
                    return new OperationCanceledException("The call was cancelled.");
                return Task.IsFaulted ? Task.Exception.GetBaseException() : null;
            }
        }

        /// <summary>The server's error code, or 0 when the call did not fail with a <see cref="WildCatException"/>.</summary>
        public int ErrorCode
        {
            get
            {
                WildCatException error = Error as WildCatException;
                return error == null ? 0 : error.ErrorCode;
            }
        }

        /// <summary>The error's message, or null.</summary>
        public string ErrorMessage
        {
            get
            {
                Exception error = Error;
                return error == null ? null : error.Message;
            }
        }
    }

    /// <summary>A <see cref="TaskYieldInstruction"/> with the call's result.</summary>
    public sealed class TaskYieldInstruction<T> : TaskYieldInstruction
    {
        public TaskYieldInstruction(Task<T> task)
            : base(task)
        {
        }

        /// <summary>The result when <see cref="TaskYieldInstruction.IsSuccessful"/>, otherwise the default value.</summary>
        public T Result
        {
            get { return IsSuccessful ? ((Task<T>)Task).Result : default(T); }
        }
    }
}
