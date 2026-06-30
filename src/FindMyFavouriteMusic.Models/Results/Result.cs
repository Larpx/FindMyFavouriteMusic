namespace FindMyFavouriteMusic.Models.Results;

/// <summary>
/// 通用操作结果，不包含返回值
/// </summary>
public class Result
{
    /// <summary>操作是否成功</summary>
    public bool IsSuccess { get; }

    /// <summary>错误信息</summary>
    public string? Error { get; }

    /// <summary>异常对象</summary>
    public Exception? Exception { get; }

    protected Result(bool isSuccess, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        Error = error;
        Exception = exception;
    }

    /// <summary>创建成功结果</summary>
    public static Result Success() => new(true, null, null);

    /// <summary>创建失败结果</summary>
    public static Result Failure(string error, Exception? exception = null) => new(false, error, exception);

    /// <summary>从异常创建失败结果</summary>
    public static Result Failure(Exception exception) => new(false, exception.Message, exception);
}
