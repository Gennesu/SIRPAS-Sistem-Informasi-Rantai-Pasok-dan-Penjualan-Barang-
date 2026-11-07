namespace Platform.Web.SharedKernel;

public enum ErrorKind
{
    None = 0,
    Validation,
    NotFound,
    Forbidden,
    Conflict,
    Unexpected
}

/// <summary>Hasil operasi service tanpa nilai balik. Pesan Error aman ditampilkan ke pengguna.</summary>
public sealed class Result
{
    private Result(bool succeeded, ErrorKind kind, string? error)
    {
        Succeeded = succeeded;
        Kind = kind;
        Error = error;
    }

    public bool Succeeded { get; }
    public ErrorKind Kind { get; }
    public string? Error { get; }

    public static Result Ok() => new(true, ErrorKind.None, null);
    public static Result Fail(ErrorKind kind, string error) => new(false, kind, error);
}

/// <summary>Hasil operasi service dengan nilai balik.</summary>
public sealed class Result<T>
{
    private Result(bool succeeded, T? value, ErrorKind kind, string? error)
    {
        Succeeded = succeeded;
        Value = value;
        Kind = kind;
        Error = error;
    }

    public bool Succeeded { get; }
    public T? Value { get; }
    public ErrorKind Kind { get; }
    public string? Error { get; }

    public static Result<T> Ok(T value) => new(true, value, ErrorKind.None, null);
    public static Result<T> Fail(ErrorKind kind, string error) => new(false, default, kind, error);
}
