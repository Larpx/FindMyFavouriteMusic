namespace FindMyFavouriteMusic.Models.Results;

/// <summary>
/// 通用操作结果，包含返回值
/// </summary>
public class Result<T> : Result
{
    /// <summary>成功时的返回值</summary>
    public T? Value { get; }

    private Result(bool isSuccess, T? value, string? error, Exception? exception)
        : base(isSuccess, error, exception)
    {
        Value = value;
    }

    /// <summary>创建成功结果</summary>
    public static Result<T> Success(T value) => new(true, value, null, null);

    /// <summary>创建失败结果</summary>
    public static new Result<T> Failure(string error, Exception? exception = null) => new(false, default, error, exception);

    /// <summary>从异常创建失败结果</summary>
    public static new Result<T> Failure(Exception exception) => new(false, default, exception.Message, exception);
}
