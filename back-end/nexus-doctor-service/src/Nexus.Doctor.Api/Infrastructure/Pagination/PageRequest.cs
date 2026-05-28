namespace Nexus.Doctors.Api.Infrastructure.Pagination;

public sealed record PageRequest(int Page = 1, int Size = 20)
{
    public const int MaxSize = 100;
    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedSize => Size < 1 ? 1 : (Size > MaxSize ? MaxSize : Size);
    public int Skip => (NormalizedPage - 1) * NormalizedSize;
}
