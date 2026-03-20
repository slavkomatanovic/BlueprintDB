namespace Blueprint.App.Backend;

public sealed class TransferResult
{
    public int TablesOk      { get; }
    public int TablesSkipped { get; }
    public IReadOnlyList<(string Table, string Error)> Errors { get; }
    public bool Success => Errors.Count == 0;

    public TransferResult(int ok, int skipped, IReadOnlyList<(string, string)> errors)
    {
        TablesOk      = ok;
        TablesSkipped = skipped;
        Errors        = errors;
    }
}
