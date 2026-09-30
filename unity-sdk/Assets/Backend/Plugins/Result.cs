using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Used to return results from coroutines.
/// </summary>
public class Result
{
    /// <summary>
    /// Indicates if the operation is complete or not
    /// This will return true both in success and failure cases after completion, you should check isSuccessful
    /// to see if the operation was completed successfully or not
    /// </summary>
    public bool isComplete { get; private set; }

    /// <summary>
    /// Indicates if the process is completed successfully or not,
    /// only valid if the operation is complete
    /// </summary>
    public bool isSuccessful { get; private set; }

    /// <summary>
    /// Error message if the operation is failed.
    /// Only valid if the operation is complete and also failed
    /// </summary>
    public string errorMessage { get; private set; }

    /// <summary>
    /// The error code of the failed operation,
    /// Only valid if the operation is complete and failed
    /// </summary>
    public int errorCode { get; private set; }

    private MonoBehaviour owner;

    /// <summary>
    /// Completes the result successfully.
    /// Only call this on the Result objects you create and manage and not on the ones returned from
    /// any API
    /// </summary>
    public void Succeed()
    {
        isSuccessful = true;
        isComplete = true;
    }

    public Result(MonoBehaviour owner)
    {
        this.owner = owner;
    }

    /// <summary>
    /// You should call this to get a yieldable value in your courinte
    /// You yield return the return value of this operation to wait until it is completed
    /// either successfully or not
    /// </summary>
    /// <returns></returns>
    public Coroutine Wait()
    {
        return owner.StartCoroutine(WaitUntilDone());
    }

    private IEnumerator WaitUntilDone()
    {
        while (!isComplete)
            yield return null;
    }

    /// <summary>
    /// Completes and fails a Result object.
    /// Only call this on the Result objects you create and manage and not on the ones returned from any API
    /// </summary>
    /// <param name="code">Error code of the failure</param>
    /// <param name="error">Error message of the failure</param>
    public void Fail(int code, string error)
    {
        isSuccessful = false;
        errorMessage = error;
        errorCode = code;
        isComplete = true;
    }
}

/// <summary>
/// Used to return results from coroutines.
/// </summary>
/// <typeparam name="T"></typeparam>
public class Result<T>
{
    /// <summary>
    /// Indicates if the operation is complete or not
    /// This will return true both in success and failure cases after completion, you should check isSuccessful
    /// to see if the operation was completed successfully or not
    /// </summary>
    public bool isComplete { get; private set; }

    /// <summary>
    /// Indicates if the process is completed successfully or not,
    /// only valid if the operation is complete
    /// </summary>
    public bool isSuccessful { get; private set; }

    /// <summary>
    /// Error message if the operation is failed.
    /// Only valid if the operation is complete and also failed
    /// </summary>
    public string errorMessage { get; private set; }

    /// <summary>
    /// The error code of the failed operation,
    /// Only valid if the operation is complete and failed
    /// </summary>
    public int errorCode { get; private set; }

    /// <summary>
    /// Value of the result object
    /// Only valid if the operation was successfully completed, 
    /// otherwise throws an exception of type InvalidOperationException
    /// </summary>
    public T value
    {
        get
        {
            if (!isComplete || !isSuccessful)
                throw new System.InvalidOperationException("Trying to get value of Incomplete or failed result");
            return _value;
        }
    }

    private MonoBehaviour owner;
    private T _value;

    /// <summary>
    /// Completes the result successfully.
    /// Only call this on the Result objects you create and manage and not on the ones returned from
    /// any API
    /// </summary>
    /// <param name="val">The result of the successful operation which can be read from the value property</param>
    public void Succeed(T val)
    {
        isSuccessful = true;
        isComplete = true;
        _value = val;
    }

    public Result(MonoBehaviour owner)
    {
        this.owner = owner;
    }

    /// <summary>
    /// You should call this to get a yieldable value in your courinte
    /// You yield return the return value of this operation to wait until it is completed
    /// either successfully or not
    /// </summary>
    /// <returns></returns>
    public Coroutine Wait()
    {
        return owner.StartCoroutine(WaitUntilDone());
    }

    private IEnumerator WaitUntilDone()
    {
        while (!isComplete)
            yield return null;
    }

    /// <summary>
    /// Completes and fails a Result object.
    /// Only call this on the Result objects you create and manage and not on the ones returned from any API
    /// </summary>
    /// <param name="code">Error code of the failure</param>
    /// <param name="error">Error message of the failure</param>
    public void Fail(int code, string error)
    {
        isSuccessful = false;
        errorMessage = error;
        errorCode = code;
        isComplete = true;
    }
}

/// <summary>
/// Used to return results from coroutines.
/// </summary>
/// <typeparam name="T"></typeparam>
public class Result<T, Q>
{
    /// <summary>
    /// Indicates if the operation is complete or not
    /// This will return true both in success and failure cases after completion, you should check isSuccessful
    /// to see if the operation was completed successfully or not
    /// </summary>
    public bool isComplete { get; private set; }

    /// <summary>
    /// Indicates if the process is completed successfully or not,
    /// only valid if the operation is complete
    /// </summary>
    public bool isSuccessful { get; private set; }

    /// <summary>
    /// Error message if the operation is failed.
    /// Only valid if the operation is complete and also failed
    /// </summary>
    public string errorMessage { get; private set; }

    /// <summary>
    /// The error code of the failed operation,
    /// Only valid if the operation is complete and failed
    /// </summary>
    public int errorCode { get; private set; }

    /// <summary>
    /// First value of the result object
    /// Only valid if the operation was successfully completed, 
    /// otherwise throws an exception of type InvalidOperationException
    /// </summary>
    public T value1
    {
        get
        {
            if (!isComplete || !isSuccessful)
                throw new System.InvalidOperationException("Trying to get value of Incomplete or failed result");
            return _value;
        }
    }

    /// <summary>
    /// Second value of the result object
    /// Only valid if the operation was successfully completed, 
    /// otherwise throws an exception of type InvalidOperationException
    /// </summary>
    public Q value2
    {
        get
        {
            if (!isComplete || !isSuccessful)
                throw new System.InvalidOperationException("Trying to get value of Incomplete or failed result");
            return _value2;
        }
    }

    private MonoBehaviour owner;
    private T _value;
    private Q _value2;

    /// <summary>
    /// Completes the result successfully.
    /// Only call this on the Result objects you create and manage and not on the ones returned from
    /// any API
    /// </summary>
    /// <param name="val">First part of result of the successful operation which can be read from value1 property</param>
    /// <param name="val2">Second part of result of the successful operation which can be read from value2 property</param>
    public void Succeed(T val, Q val2)
    {
        isSuccessful = true;
        isComplete = true;
        _value = val;
        _value2 = val2;
    }

    public Result(MonoBehaviour owner)
    {
        this.owner = owner;
    }

    /// <summary>
    /// You should call this to get a yieldable value in your courinte
    /// You yield return the return value of this operation to wait until it is completed
    /// either successfully or not
    /// </summary>
    /// <returns></returns>
    public Coroutine Wait()
    {
        return owner.StartCoroutine(WaitUntilDone());
    }

    private IEnumerator WaitUntilDone()
    {
        while (!isComplete)
            yield return null;
    }

    /// <summary>
    /// Completes and fails a Result object.
    /// Only call this on the Result objects you create and manage and not on the ones returned from any API
    /// </summary>
    /// <param name="code">Error code of the failure</param>
    /// <param name="error">Error message of the failure</param>
    public void Fail(int code, string error)
    {
        isSuccessful = false;
        errorMessage = error;
        errorCode = code;
        isComplete = true;
    }
}