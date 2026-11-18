namespace Fishy.Processing;

public interface ITransform
{
    string Name { get; }
    int Apply(ProcessingContext context);
}

public interface IPostWriteTransform : ITransform
{
    void AfterWrite(ProcessingContext context);
}
